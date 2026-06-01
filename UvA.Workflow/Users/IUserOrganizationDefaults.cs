namespace UvA.Workflow.Users;

public interface IUserOrganizationDefaults
{
    Organization? ApplyDefault(string providerKey, Organization? organization);

    UserSearchResult ApplyDefault(UserSearchResult user);
}