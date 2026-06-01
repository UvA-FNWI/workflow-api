using UvA.Workflow.Users;

namespace UvA.Workflow.Api.Users;

public class UserOrganizationDefaults : IUserOrganizationDefaults
{
    private static readonly Organization DefaultInternalOrganization = new("uva", "UvA");

    public Organization? ApplyDefault(string providerKey, Organization? organization)
        => UserProviderKeys.IsInternal(providerKey) && organization == null
            ? DefaultInternalOrganization
            : organization;

    public UserSearchResult ApplyDefault(UserSearchResult user)
        => user with { Organization = ApplyDefault(user.ProviderKey, user.Organization) };
}