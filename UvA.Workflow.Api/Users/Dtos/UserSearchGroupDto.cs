namespace UvA.Workflow.Api.Users.Dtos;

public record UserSearchGroupDto(string Label, UserSearchResultDto[] Users)
{
    public static UserSearchGroupDto Create(UserSourceSearchGroup group) =>
        new(group.Label, group.Users.Select(UserSearchResultDto.Create).ToArray());
}