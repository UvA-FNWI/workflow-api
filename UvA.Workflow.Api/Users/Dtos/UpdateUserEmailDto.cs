using System.ComponentModel.DataAnnotations;

namespace UvA.Workflow.Api.Users.Dtos;

public record UpdateUserEmailDto(
    [Required] ExternalUserDto ExternalUser,
    [Required] string InstanceId);