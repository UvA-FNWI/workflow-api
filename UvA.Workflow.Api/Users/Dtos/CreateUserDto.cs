using System.ComponentModel.DataAnnotations;

namespace UvA.Workflow.Api.Users.Dtos;

/// <summary>
/// DTO for creating a new user
/// </summary>
public record CreateUserDto(
    [Required] string UserName,
    [Required] string DisplayName,
    [Required] [EmailAddress] string Email
);