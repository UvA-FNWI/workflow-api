using UvA.Workflow.DataNose;

namespace UvA.Workflow.Api.Users.Dtos;

public record UserInfoDto(string UserName, string DisplayName, string Email)
{
    public static UserInfoDto Create(UserInfo userInfo) => new(userInfo.UserName, userInfo.DisplayName, userInfo.Email);
}