using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using UvA.Workflow.Api.Authentication;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Events;

namespace UvA.Workflow.Api.WorkflowInstances;

public class WorkflowInstancesController(
    IUserService userService,
    WorkflowInstanceService workflowInstanceService,
    RightsService rightsService,
    WorkflowInstanceDtoFactory workflowInstanceDtoFactory,
    IWorkflowInstanceRepository repository,
    InstanceService instanceService,
    AnswerConversionService answerConversionService,
    ModelService modelService
) : ApiControllerBase
{
    //[Authorize(AuthenticationSchemes = AuthenticationExtensions.AllSchemes)] TODO: enable again
    [HttpPost]
    public async Task<ActionResult<WorkflowInstanceDto>> Create(
        [FromBody] CreateWorkflowInstanceDto input, CancellationToken ct)
    {
        var user = await userService.GetCurrentUser(ct);
        if (user == null) return Unauthorized();
        var actions = await rightsService.GetAllowedActions(input.WorkflowDefinition, RoleAction.CreateInstance);
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
            initial,
            input.InitialEvents?.ToDictionary(e => e.Key, e => new InstanceEvent { Id = e.Key, Date = e.Value })
        );

        await instanceService.UpdateCurrentStep(instance, ct);

        var result = await workflowInstanceDtoFactory.Create(instance, ct);

        return CreatedAtAction(nameof(GetById), new { id = instance.Id }, result);
    }

    [HttpPost("{parentInstanceId}/{propertyName}")]
    public async Task<ActionResult<WorkflowInstanceDto>> Create(string parentInstanceId, string propertyName,
        [FromBody] CreateWorkflowInstanceDto input, CancellationToken ct)
    {
        var user = await userService.GetCurrentUser(ct);
        if (user == null) return Unauthorized();
        var actions = await rightsService.GetAllowedActions(input.WorkflowDefinition, RoleAction.CreateRelatedInstance);
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
            initial,
            input.InitialEvents?.ToDictionary(e => e.Key, e => new InstanceEvent { Id = e.Key, Date = e.Value })
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

    //[Authorize(AuthenticationSchemes = AuthenticationExtensions.AllSchemes)] TODO: enable again
    [HttpGet("instances/{workflowDefinition}")]
    public async Task<ActionResult<IEnumerable<Dictionary<string, object>>>> GetInstances(string workflowDefinition,
        [FromQuery] string[] properties, CancellationToken ct)
    {
        if (!await rightsService.CanAny(workflowDefinition, RoleAction.ViewAdminTools))
            return Forbidden();

        var entity = modelService.WorkflowDefinitions[workflowDefinition];
        var res = await repository.GetAllByType(workflowDefinition, properties.ToDictionary(
            p => p,
            p => entity.GetKey(p)
        ), ct);

        return Ok(res.Select(i => i.ToDictionary(
            k => k.Key == "_id" ? "Id" : k.Key,
            v => BsonConversionTools.ConvertBasicBsonValue(v.Value)))
        );
    }
}