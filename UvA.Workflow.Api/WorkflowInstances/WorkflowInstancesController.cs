using Microsoft.AspNetCore.Authorization;
using UvA.Workflow.Api.Authentication;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.WorkflowInstances;

public class WorkflowInstancesController(
    IUserService userService,
    WorkflowInstanceService workflowInstanceService,
    RightsService rightsService,
    WorkflowInstanceDtoFactory workflowInstanceDtoFactory,
    IWorkflowInstanceRepository repository,
    InstanceService instanceService,
    AnswerConversionService answerConversionService,
    AnswerService answerService,
    ModelService modelService,
    RoleImpersonationService impersonationService
) : ApiControllerBase
{
    [Authorize(AuthenticationSchemes = WorkflowAuthenticationDefaults.AnyScheme)]
    [HttpPost]
    public async Task<ActionResult<WorkflowInstanceDto>> Create(
        [FromBody] CreateWorkflowInstanceDto input, CancellationToken ct)
    {
        var user = await userService.GetCurrentUser(ct);
        if (user == null) return Unauthorized();
        var actions = input.ParentId == null
            ? await rightsService.GetAllowedActions(input.WorkflowDefinition, RoleAction.CreateInstance)
            : [];
        if (actions.Length == 0)
            return Forbid();

        var initial = input.InitialProperties?.ToDictionary(k => k.Key, BsonValue (_) => BsonNull.Value);
        if (initial != null)
        {
            foreach (var entry in input.InitialProperties ?? [])
            {
                var property = modelService.WorkflowDefinitions[input.WorkflowDefinition].Properties
                    .GetOrDefault(entry.Key);
                if (property == null)
                    return BadRequest($"Property {entry.Key} does not exist");
                initial[entry.Key] = await answerConversionService.ConvertToValue(
                    entry.Value, property, ct);
            }
        }

        var instance = await workflowInstanceService.Create(
            input.WorkflowDefinition,
            user,
            ct,
            actions.FirstOrDefault(a => a.UserProperty != null)?.UserProperty,
            input.ParentId,
            initial
        );

        await instanceService.UpdateCurrentStep(instance, ct);

        var result = await workflowInstanceDtoFactory.Create(instance, ct);

        return CreatedAtAction(nameof(GetById), new { id = instance.Id }, result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<WorkflowInstanceDto>> GetById(string id, [FromQuery] int? version = null,
        CancellationToken ct = default)
    {
        var instance = version is null
            ? await repository.GetById(id, ct)
            : await workflowInstanceService.GetAsOfVersion(id, version.Value, ct);
        if (instance == null)
            return WorkflowInstanceNotFound;

        if (!await rightsService.Can(instance, RoleAction.View))
            return Forbidden();

        // Make sure the instance is in the right step before returning it
        await instanceService.UpdateCurrentStep(instance, ct);

        var result = await workflowInstanceDtoFactory.Create(instance, ct);

        return Ok(result);
    }

    [HttpGet("{id}/impersonation/roles")]
    public async Task<ActionResult<IEnumerable<ImpersonationRoleDto>>> GetImpersonationRoles(string id,
        CancellationToken ct)
    {
        var instance = await repository.GetById(id, ct);
        if (instance == null)
            return WorkflowInstanceNotFound;

        if (!await rightsService.Can(instance, [RoleAction.ImpersonateRoles], RightsEvaluationMode.RealUser))
            return Forbidden();

        var roles = rightsService.GetImpersonationTargetRoles(instance)
            .Select(ImpersonationRoleDto.Create)
            .ToArray();

        return Ok(roles);
    }

    [HttpPost("{id}/impersonation")]
    public async Task<ActionResult<StartImpersonationResultDto>> StartImpersonation(string id,
        [FromBody] StartImpersonationDto input, CancellationToken ct)
    {
        var currentUser = await userService.GetCurrentUser(ct);
        if (currentUser == null)
            return Unauthorized();

        var instance = await repository.GetById(id, ct);
        if (instance == null)
            return WorkflowInstanceNotFound;

        if (!await rightsService.Can(instance, [RoleAction.ImpersonateRoles], RightsEvaluationMode.RealUser))
            return Forbidden();

        var normalizedRoleName = rightsService.NormalizeImpersonationTargetRole(instance, input.Role);
        if (normalizedRoleName == null)
            return BadRequest("InvalidImpersonationRole",
                $"Role '{input.Role}' cannot be impersonated for workflow '{instance.WorkflowDefinition}'.");

        var token = impersonationService.CreateToken(currentUser.UserName, instance.Id, normalizedRoleName.Name);
        return Ok(new StartImpersonationResultDto(
            instance.Id,
            normalizedRoleName,
            token.Value,
            token.ExpiresAtUtc
        ));
    }

    [Authorize(AuthenticationSchemes = WorkflowAuthenticationDefaults.AnyScheme)]
    [HttpGet("instances/{workflowDefinition}")]
    public async Task<ActionResult<IEnumerable<Dictionary<string, object>>>> GetInstances(string workflowDefinition,
        [FromQuery] string[] properties, CancellationToken ct, [FromQuery] bool includeTitle = false)
    {
        if (!await rightsService.CanAny(workflowDefinition, RoleAction.ViewAdminTools))
            return Forbidden();

        var entity = modelService.WorkflowDefinitions[workflowDefinition];

        // Title is rendered from a per-definition template, so it's opt-in
        var titleTemplate = includeTitle ? entity.InstanceTitleTemplate : null;
        var titleProperties = titleTemplate is null
            ? []
            : titleTemplate.Properties
                .OfType<PropertyLookup>()
                .Select(p => p.Parts[0])
                // Id maps to the always-present _id
                .Where(p => p != "Id");

        var projection = properties
            .Append("CreatedOn")
            .Concat(titleProperties)
            .Distinct()
            .ToDictionary(p => p, entity.GetKey);

        var res = await repository.GetAllByType(workflowDefinition, projection, ct);

        return Ok(res
            .OrderByDescending(i => i.GetValueOrDefault("_id"))
            .Select(i =>
            {
                var row = i.ToDictionary(
                    k => k.Key == "_id" ? "Id" : k.Key,
                    v => BsonConversionTools.ConvertBasicBsonValue(v.Value));
                if (titleTemplate != null)
                    row["Title"] = titleTemplate.Apply(modelService.CreateContext(workflowDefinition, i));
                return row;
            })
            .Select(row => row.ToDictionary(
                k => char.ToLowerInvariant(k.Key[0]) + k.Key[1..],
                v => v.Value
            )));
    }


    [HttpPost("{id}/properties/{property}")]
    public async Task<ActionResult<UpdateInstancePropertyResponse>> AddPropertyItem(string id, string property,
        [FromBody] UpdateInstancePropertyRequest input, CancellationToken ct)
    {
        var currentUser = await userService.GetCurrentUser(ct);
        if (currentUser == null)
            return Unauthorized();

        var instance = await repository.GetById(id, ct);
        if (instance == null)
            return WorkflowInstanceNotFound;

        if (!await rightsService.Can(instance, [RoleAction.Edit], RightsEvaluationMode.RealUser))
            return Forbidden();

        var propertyDefinition = modelService.WorkflowDefinitions[instance.WorkflowDefinition].Properties
            .GetOrDefault(property);
        if (propertyDefinition == null)
            return BadRequest($"Property '{property}' does not exist");

        var externalUserInput = input.ExternalUser is { } eu
            ? new ExternalUserInput(eu.DisplayName, eu.Email, eu.Organization)
            : null;

        try
        {
            var (value, _) = await answerService.ValidateAndResolveValue(
                propertyDefinition, input.Value, externalUserInput, ct);

            await workflowInstanceService.UpdateProperty(id, property, value, answerConversionService, ct);

            return Ok();
        }
        catch (ExternalUserCreationException ex)
        {
            return MapExternalUserCreationError(ex); // now on ApiControllerBase
        }
    }

    [HttpDelete("{id}/properties/{property}/{itemId}")]
    public async Task<ActionResult> RemovePropertyItem(string id, string property, string itemId,
        CancellationToken ct)
    {
        if (await userService.GetCurrentUser(ct) == null)
            return Unauthorized();

        var instance = await repository.GetById(id, ct);
        if (instance == null)
            return WorkflowInstanceNotFound;

        if (!await rightsService.Can(instance, [RoleAction.Edit], RightsEvaluationMode.RealUser))
            return Forbidden();

        var propertyDefinition = modelService.WorkflowDefinitions[instance.WorkflowDefinition].Properties
            .GetOrDefault(property);
        if (propertyDefinition == null)
            return BadRequest("InvalidProperty", $"Property '{property}' does not exist");

        await workflowInstanceService.RemovePropertyItemById(instance.Id, property, itemId, ct);
        return Ok();
    }
}