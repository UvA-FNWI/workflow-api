using System.Text.Json;
using UvA.Workflow.Api.Users.Dtos;

namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public record AssignRelatedUserRequest(
    JsonElement? User,
    CreateExternalUserDto? ExternalUser = null);
