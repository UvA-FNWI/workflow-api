namespace UvA.Workflow.Users.EntraId;

public class EntraGraphOptions
{
    public const string Section = "EntraId";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Authority { get; set; }
    public int RequestTimeoutSeconds { get; set; } = 15;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException($"{Section}:ClientId is required for Entra ID Graph");

        if (string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidOperationException($"{Section}:ClientSecret is required for Entra ID Graph");

        if (string.IsNullOrWhiteSpace(Authority))
            throw new InvalidOperationException($"{Section}:Authority is required for Entra ID Graph");

        if (RequestTimeoutSeconds is < 1 or > 120)
            throw new InvalidOperationException($"{Section}:RequestTimeoutSeconds must be between 1 and 120");
    }
}