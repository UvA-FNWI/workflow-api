using System.ComponentModel.DataAnnotations;

namespace UvA.Workflow.Api.Invites;

public record InviteUserRequest(
    [Required] [EmailAddress] string Email,
    [Required] [MinLength(1)] string UserName
);