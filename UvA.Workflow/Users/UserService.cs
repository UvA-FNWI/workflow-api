using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Bson.Serialization.Attributes;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Users;

public abstract class UserServiceBase(IUserRepository userRepository, IMemoryCache memoryCache)
{
    private IUserRepository UserRepository { get; } = userRepository;
    private static TimeSpan UserCacheExpiration => TimeSpan.FromMinutes(15);
    private static string GetCacheKeyForUser(string userName) => $"user:{userName}";
    public const string ApiUserName = "__apiuser";

    /// <summary>
    /// Adds a new user or updates an existing user in the repository. If the user does not exist,
    /// it creates a new user with the provided details. If the user exists, it updates the user's
    /// information if any changes are detected. The result is cached for a specified duration.
    /// </summary>
    /// <param name="username">A string representing the unique external identifier for the user.</param>
    /// <param name="displayName">A string representing the display name of the user.</param>
    /// <param name="email">A string containing the email address of the user.</param>
    /// <param name="providerKey">Identifies the source provider for the user.</param>
    /// <param name="organization">An Organization object containing the id and name of the user's organization.</param>
    /// <param name="picture">A string containing the picture url of the user.</param>
    /// <param name="ct">A <see cref="CancellationToken"/> used to observe cancellation requests.</param>
    /// <returns>A <see cref="User"/> object representing the added or updated user.</returns>
    public async Task<User> AddOrUpdateUser(string username, string displayName, string email, string providerKey,
        Organization? organization, string? picture, CancellationToken ct)
    {
        username = username.ToLower();
        providerKey = UserProviderKeys.Normalize(providerKey);
        var cacheKey = GetCacheKeyForUser(username);
        if (!memoryCache.TryGetValue(cacheKey, out User? user))
        {
            user = await UserRepository.GetByExternalId(username, ct);
        }

        if (user == null)
        {
            user = new User
            {
                UserName = username,
                DisplayName = displayName,
                Email = email,
                Picture = picture,
                ProviderKey = providerKey,
                Organization = organization,
                IsActive = true
            };
            await UserRepository.Create(user, ct);
        }
        else
        {
            var changed = false;
            if (user.DisplayName != displayName)
            {
                changed = true;
                user.DisplayName = displayName;
            }

            if (user.Email != email)
            {
                changed = true;
                user.Email = email;
            }

            if (!UserProviderKeys.AreEqual(user.ProviderKey, providerKey))
            {
                changed = true;
                user.ProviderKey = providerKey;
            }

            if (organization != null && user.Organization == null)
            {
                changed = true;
                user.Organization = organization;
            }

            if (picture != null && user.Picture != picture)
            {
                changed = true;
                user.Picture = picture;
            }

            if (changed)
                await UserRepository.Update(user, ct);
        }

        memoryCache.Set(cacheKey, user, UserCacheExpiration);

        return user;
    }

    /// <summary>
    /// Retrieves a user by their username from the cache, or the user repository if not cached. If the user is found in the repository, it is added to the cache for future requests.
    /// </summary>
    /// <param name="username">The unique username of the user to retrieve.</param>
    /// <param name="ct">A <see cref="CancellationToken"/> used to observe cancellation requests.</param>
    /// <returns>A <see cref="User"/> object matching the specified username if found, or null if no such user exists.</returns>
    public async Task<User?> GetUser(string username, CancellationToken ct)
    {
        username = username.ToLower();
        var cacheKey = GetCacheKeyForUser(username);
        if (memoryCache.TryGetValue(cacheKey, out User? user)) return user;
        if (username == ApiUserName)
            user = new User { UserName = username, DisplayName = "Api", Email = "api@invalid.uva.nl" };
        else
            user = await UserRepository.GetByExternalId(username, ct);
        if (user != null)
            memoryCache.Set(cacheKey, user, UserCacheExpiration);
        return user;
    }

    protected bool IsCached(string username) => memoryCache.TryGetValue(GetCacheKeyForUser(username), out _);
}

public class UserService(
    ICurrentUserAccessor currentUserAccessor,
    IUserRepository userRepository,
    IOrganizationService organizationService,
    IMemoryCache cache,
    IEnumerable<IUserDirectory> userDirectories,
    IEnumerable<IUserSearchSource> userSearchSources,
    IWorkflowInstanceRepository instanceRepository,
    ModelService modelService
) : UserServiceBase(userRepository, cache), IUserService
{
    private readonly IMemoryCache _cache = cache;
    private readonly IReadOnlyList<IUserDirectory> _userDirectories = userDirectories.ToList();
    private readonly IReadOnlyList<IUserSearchSource> _userSearchSources = userSearchSources.ToList();
    private static string GetCacheKeyForRoles(string userName) => $"roles:{userName}";
    private static TimeSpan RolesCacheExpiration => TimeSpan.FromMinutes(15);

    /// <summary>
    /// Retrieves the current authenticated user from the HTTP context or cache. If the user is not present in cache, it retrieves the user from the repository and caches the result for a specified duration.
    /// </summary>
    /// <param name="ct">A <see cref="CancellationToken"/> used to observe cancellation requests.</param>
    /// <returns>A <see cref="User"/> object representing the current user if authenticated, or null if the user is not authenticated or not found.</returns>
    public async Task<User?> GetCurrentUser(CancellationToken ct = default)
    {
        var userName = currentUserAccessor.GetCurrentUserName();
        return string.IsNullOrWhiteSpace(userName)
            ? null
            : await GetUser(userName, ct);
    }


    public async Task<IEnumerable<string>> GetRoles(User user, CancellationToken ct = default)
    {
        var cacheKey = GetCacheKeyForRoles(user.UserName);
        if (_cache.TryGetValue(cacheKey, out string[]? roles)) return roles!;
        if (user.UserName == ApiUserName)
            roles = ["Api"];
        else
        {
            var directory = _userDirectories.FirstOrDefault(source =>
                UserProviderKeys.AreEqual(source.ProviderKey, user.ProviderKey));
            try
            {
                roles = directory == null
                    ? []
                    : (await directory.GetRoles(user, ct)).ToArray();
            }
            catch
            {
                // Directory lookup unavailable (e.g. DataNose down or unconfigured). Don't block
                // the request; GetGlobalRoles still appends Registered.
                roles = [];
            }
        }

        _cache.Set(cacheKey, roles, RolesCacheExpiration);
        return roles;
    }

    /// <summary>
    /// Retrieves the roles of the current authenticated user. If the user is not authenticated or not found, returns an empty collection.
    /// </summary>
    /// <param name="ct">A <see cref="CancellationToken"/> used to observe cancellation requests.</param>
    /// <returns>An enumerable collection of strings representing the roles assigned to the current user, or an empty collection if the user is not authenticated or roles cannot be retrieved.</returns>
    public async Task<IEnumerable<string>> GetRolesOfCurrentUser(CancellationToken ct = default)
    {
        var user = await GetCurrentUser(ct);
        return user is null ? [] : await GetRoles(user, ct);
    }

    public async Task<IEnumerable<UserSearchResult>> FindUsers(string query, bool includeExternalUsers,
        CancellationToken ct)
    {
        var resultsBySource = await Task.WhenAll(_userSearchSources.Select(searchSource =>
            searchSource.FindUsers(query, ct)));

        var results = new List<UserSearchResult>();
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenUserNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var users in resultsBySource)
        {
            foreach (var user in users)
            {
                if (!includeExternalUsers && user.IsExternal)
                    continue;

                if (!string.IsNullOrWhiteSpace(user.Email) && !seenEmails.Add(user.Email))
                    continue;

                if (!seenUserNames.Add(user.UserName))
                    continue;

                results.Add(user);
            }
        }

        return results;
    }

    public async Task<Organization?> GetOrganizationForUser(string uid, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(uid))
            return null;

        try
        {
            foreach (var directory in _userDirectories)
            {
                var directoryOrganization = await directory.GetOrganization(uid, ct);
                if (directoryOrganization != null)
                    return await organizationService.GetOrCreateOrganization(directoryOrganization.Name, ct);
            }

            return null;
        }
        catch
        {
            // Directory lookup unavailable (e.g. DataNose down). Don't block login, leave it unset.
            return null;
        }
    }

    /// <summary>
    /// Finds all instances containing this user in any user-type property and replaces the
    /// fields in the embedded snapshot with the value from the current User state.
    /// </summary>
    public async Task SyncUserInInstances(User user, Expression<Func<InstanceUser, object>>[] fields,
        CancellationToken ct)
    {
        if (!ObjectId.TryParse(user.Id, out var userId)) return;

        var userPropertyNames = modelService.WorkflowDefinitions.Values
            .SelectMany(wd => wd.Properties)
            .Where(p => p.DataType == DataType.User)
            .Select(p => p.Name)
            .Distinct()
            .ToList();

        if (userPropertyNames.Count == 0) return;

        var filter = Builders<WorkflowInstance>.Filter.Or(
            userPropertyNames.Select(name =>
                Builders<WorkflowInstance>.Filter.Eq($"Properties.{name}._id", userId)));

        var instances = await instanceRepository.GetByFilter(filter, ct);

        var fieldKeys = fields.Select(GetBsonFieldName).ToList();
        var updatedUserDoc = InstanceUser.FromUser(user).ToBsonDocument();

        await Task.WhenAll(instances.Select(instance =>
            SyncUserInInstance(instance, user.Id, userId, fieldKeys, updatedUserDoc, ct)));
    }

    private async Task SyncUserInInstance(WorkflowInstance instance, string userId, ObjectId userObjectId,
        List<string> fieldKeys, BsonDocument updatedUserDoc, CancellationToken ct)
    {
        var workflowDef = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var scalarUpdates = new List<UpdateDefinition<WorkflowInstance>>();
        var arrayUpdates = new List<UpdateDefinition<WorkflowInstance>>();

        foreach (var property in workflowDef.Properties.Where(p => p.DataType == DataType.User))
        {
            var rawValue = instance.GetProperty(property.Name);
            if (rawValue == null || rawValue.IsBsonNull) continue;

            if (property.IsArray)
            {
                if (ObjectContext.GetValue(rawValue, property) is not InstanceUser[] users ||
                    users.All(u => u.Id != userId)) continue;

                foreach (var key in fieldKeys)
                    arrayUpdates.Add(Builders<WorkflowInstance>.Update
                        .Set($"Properties.{property.Name}.$[elem].{key}", updatedUserDoc[key]));
            }
            else
            {
                if (ObjectContext.GetValue(rawValue, property) is not InstanceUser instanceUser ||
                    instanceUser.Id != userId) continue;

                foreach (var key in fieldKeys)
                    scalarUpdates.Add(Builders<WorkflowInstance>.Update
                        .Set($"Properties.{property.Name}.{key}", updatedUserDoc[key]));
            }
        }

        if (scalarUpdates.Count > 0)
            await instanceRepository.UpdateFields(instance.Id,
                Builders<WorkflowInstance>.Update.Combine(scalarUpdates), ct);

        if (arrayUpdates.Count > 0)
        {
            var arrayFilter = new BsonDocumentArrayFilterDefinition<WorkflowInstance>(
                new BsonDocument("elem._id", userObjectId));
            await instanceRepository.UpdateArrayFields(instance.Id,
                Builders<WorkflowInstance>.Update.Combine(arrayUpdates), [arrayFilter], ct);
        }
    }

    /// <summary>
    /// Resolves the BSON field name for an <see cref="InstanceUser"/> property from a lambda expression.
    /// Uses the <see cref="BsonElementAttribute"/> name if declared on the property, otherwise falls back
    /// to the C# property name.
    /// </summary>
    /// <remarks>
    /// Reference type properties return a <see cref="MemberExpression"/> directly. Value type properties
    /// are boxed into a <see cref="UnaryExpression"/> when the lambda return type is <c>object</c>, so
    /// the operand is unwrapped first.
    /// </remarks>
    private static string GetBsonFieldName(Expression<Func<InstanceUser, object>> field)
    {
        // Value types get boxed into a UnaryExpression when the return type is object
        var body = field.Body is UnaryExpression boxing ? boxing.Operand : field.Body;
        var member = (MemberExpression)body;

        var bsonElement = member.Member.GetCustomAttribute<BsonElementAttribute>();
        return bsonElement?.ElementName ?? member.Member.Name;
    }
}