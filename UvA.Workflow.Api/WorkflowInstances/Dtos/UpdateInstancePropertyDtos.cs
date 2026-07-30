using System.Text.Json;
using UvA.Workflow.Api.Users.Dtos;

namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public record UpdateInstancePropertyRequest(
    JsonElement? Value,
    CreateExternalUserDto? ExternalUser = null);

public record UpdateInstancePropertyResponse(
    UserSearchResultDto? User = null);