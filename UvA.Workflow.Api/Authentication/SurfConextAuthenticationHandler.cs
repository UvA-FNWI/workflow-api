using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;

namespace UvA.Workflow.Api.Authentication;

public class SurfConextAuthenticationHandler : AuthenticationHandler<SurfConextOptions>
{
    private const string SurfconextError = "SurfConextError";
    public const string SchemeName = "SURFconext";

    /// <summary>
    /// implements the behavior of the SurfConext scheme to authenticate users.
    /// </summary>
    public SurfConextAuthenticationHandler(
        IOptionsMonitor<SurfConextOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IHttpClientFactory httpClientFactory,
        IUserService userService,
        IMemoryCache cache)
        : base(options, logger, encoder)
    {
        this.httpClient = httpClientFactory.CreateClient(SchemeName);
        this.userService = userService;
        this.cache = cache;
    }

    private readonly HttpClient httpClient;
    private readonly IUserService userService;
    private readonly IMemoryCache cache;
    private static readonly int CacheExpirationMinutes = 10;

    /// <summary>
    /// Responsible for constructing the user's identity based on request context. Returns an AuthenticateResult indicating 
    /// whether authentication was successful and, if so, the user's identity in an authentication ticket.
    /// </summary>
    /// <returns>AuthenticateResult</returns>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Context.Request.Headers["Authorization"].Count == 0)
            return AuthenticateResult.NoResult();

        var authorizationHeader = Context.Request.Headers["Authorization"].ToString();
        if (string.IsNullOrEmpty(authorizationHeader))
            return AuthenticateResult.Fail("missing Authorization header");

        var authHeaderParts = authorizationHeader.Split(' ');
        if (authHeaderParts.Length < 2 ||
            authHeaderParts[0] != "Bearer")
            return AuthenticateResult.Fail("invalid Authorization header");

        var bearerToken = authHeaderParts[1].Trim();
        var cacheKey = $"bt_{bearerToken}";

        if (cache.TryGetValue(cacheKey, out ClaimsPrincipal? cachedPrincipal))
            return AuthenticateResult.Success(new AuthenticationTicket(cachedPrincipal!, SchemeName));

        var resp = await ValidateSurfBearerToken(bearerToken);
        if (resp == null)
            return AuthenticateResult.Fail("token validation failed");

        if (!resp.Active)
            return AuthenticateResult.Fail("inactive token");

        if (string.IsNullOrEmpty(resp.FullName) ||
            string.IsNullOrEmpty(resp.Email))
            return AuthenticateResult.Fail("missing name or email");

        if (resp.Uids is null || resp.Uids.Length == 0)
            return AuthenticateResult.Fail("missing uid");

        var principal = CreateClaimsPrincipal(resp);
        cache.Set(cacheKey, principal,
            new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(CacheExpirationMinutes)
            });

        await userService.AddOrUpdateUser(principal.Identity!.Name!, resp.FullName, resp.Email);

        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = Context.Items[SurfconextError] as string ?? "Unauthorized",
                Instance = Context.Request.Path.Value
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    // authentication using a token
    private async Task<IntrospectionResponse?> ValidateSurfBearerToken(string token)
    {
        // call to SurfConext to verify token
        var response = await httpClient.PostAsync("/oidc/introspect",
            new FormUrlEncodedContent([new KeyValuePair<string?, string?>("token", token)])
        );

        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            Logger.LogError(
                "Token validation failed: SurfConext returned status {Code}: {Response}, ClientId:{ClientId}, Secret:{ClientSecret}",
                response.StatusCode, content, OptionsMonitor.CurrentValue.ClientId,
                OptionsMonitor.CurrentValue.ClientSecret?[..4]);
            Context.Items[SurfconextError] =
                $"Token validation failed: SurfConext returned status {response.StatusCode}, check the logs for details";
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<IntrospectionResponse>(content);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Token validation failed: unable to deserialize response: {Response}", content);
            Context.Items[SurfconextError] =
                $"Token validation failed: unable to deserialize response from SurfConext, check the logs for details";
            return null;
        }
    }

    private static ClaimsPrincipal CreateClaimsPrincipal(IntrospectionResponse r)
    {
        var claims = new List<Claim>();

        if (!string.IsNullOrWhiteSpace(r.Sub))
            claims.Add(new Claim("sub", r.Sub));

        if (!string.IsNullOrWhiteSpace(r.Email))
            claims.Add(new Claim(ClaimTypes.Email, r.Email));

        if (r.EmailVerified.HasValue)
            claims.Add(new Claim("email_verified", r.EmailVerified.Value ? "true" : "false"));

        if (!string.IsNullOrWhiteSpace(r.ClientId))
            claims.Add(new Claim("client_id", r.ClientId));

        if (r.Uids is { Length: > 0 } && !string.IsNullOrWhiteSpace(r.Uids[0]))
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, UvaClaimTypes.UvanetId));
            claims.Add(new Claim(UvaClaimTypes.UvanetId, r.Uids[0]));
        }

        if (!string.IsNullOrWhiteSpace(r.Acr))
            claims.Add(new Claim("acr", r.Acr));

        if (!string.IsNullOrWhiteSpace(r.AuthenticatingAuthority))
            claims.Add(new Claim("authenticating_authority", r.AuthenticatingAuthority));

        if (!string.IsNullOrWhiteSpace(r.Iss))
            claims.Add(new Claim("iss", r.Iss));

        if (!string.IsNullOrWhiteSpace(r.TokenType))
            claims.Add(new Claim("token_type", r.TokenType));

        if (!string.IsNullOrWhiteSpace(r.Scope))
        {
            foreach (var s in r.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                claims.Add(new Claim("scope", s));
        }

        if (r.Exp.HasValue)
            claims.Add(new Claim("exp", r.Exp.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        if (r.UpdatedAt.HasValue)
            claims.Add(new Claim("updated_at",
                r.UpdatedAt.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        var identity = new ClaimsIdentity(claims, SchemeName, UvaClaimTypes.UvanetId, ClaimTypes.Role);
        return new ClaimsPrincipal(identity);
    }
}