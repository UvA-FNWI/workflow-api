namespace UvA.Workflow.Api.Users.Dtos;

public record PersonDto(string Id, string DisplayName, string Email, string UserName)
{
    public static PersonDto Create(User user) => new(user.Id, user.DisplayName, user.Email, user.UserName);
}

public record PeopleSearchDto(PersonDto[] People, bool HasMore);