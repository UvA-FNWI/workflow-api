using System.ComponentModel.DataAnnotations;

namespace UvA.Workflow.Api.Users.Dtos;

/// <summary>Request to start impersonating a user, by their workflow username.</summary>
public record StartUserImpersonationDto([Required] string UserName);

/// <summary>The signed impersonation token and its expiry.</summary>
public record UserImpersonationStartedDto(string Token, DateTime ExpiresAtUtc);