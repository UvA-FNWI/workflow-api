using System.ComponentModel.DataAnnotations;

namespace UvA.Workflow.Api.Users.Dtos;

public record UpdateUserEmailDto(
    [Required] CreateExternalUserDto ExternalUser,
    [Required] string InstanceId);