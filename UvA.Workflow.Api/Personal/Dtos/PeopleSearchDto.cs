using UvA.Workflow.Api.Users.Dtos;

namespace UvA.Workflow.Api.Personal.Dtos;

public record PeopleSearchDto(UserDto[] People, bool HasMore);