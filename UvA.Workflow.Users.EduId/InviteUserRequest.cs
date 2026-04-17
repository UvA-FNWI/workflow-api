using System.ComponentModel.DataAnnotations;

namespace UvA.Workflow.Users.EduId;

public record InviteUserRequest(
    [Required] [EmailAddress] string Email,
    [Required] [MinLength(1)] string UserName
);