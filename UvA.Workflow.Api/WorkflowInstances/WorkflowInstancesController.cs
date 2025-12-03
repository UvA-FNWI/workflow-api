using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using UvA.Workflow.Api.Authentication;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.WorkflowInstances.Dtos;

namespace UvA.Workflow.Api.WorkflowInstances;

public class WorkflowInstancesController(
    IUserService userService,
    WorkflowInstanceService service,
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
        var actions = input.ParentId == null
            ? await rightsService.GetAllowedActions(input.EntityType, RoleAction.CreateInstance)
            : [];
        if (actions.Length == 0)
            return Forbid();

        var initial = input.InitialProperties?.ToDictionary(k => k.Key, BsonValue (_) => BsonNull.Value);
        if (initial != null)
        {
            foreach (var entry in input.InitialProperties ?? [])
            {
                var property = modelService.EntityTypes[input.EntityType].Properties.GetValueOrDefault(entry.Key);
                if (property == null)
                    return BadRequest($"Property {entry.Key} does not exist");
                initial[entry.Key] = await answerConversionService.ConvertToValue(
                    new AnswerInput(entry.Value), property, ct);
            }
        }

        var instance = await service.Create(
            input.EntityType,
            user,
            ct,
            actions.First().UserProperty,
            input.ParentId,
            initial
        );

        await instanceService.UpdateCurrentStep(instance, ct);

        var result = await workflowInstanceDtoFactory.Create(instance, ct);

        return CreatedAtAction(nameof(GetById), new { id = instance.Id }, result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<WorkflowInstanceDto>> GetById(string id, CancellationToken ct)
    {
        var instance = await repository.GetById(id, ct);
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
    [HttpGet("instances/{entityType}")]
    public async Task<ActionResult<IEnumerable<Dictionary<string, object>>>> GetInstances(string entityType,
        [FromQuery] string[] properties, CancellationToken ct)
    {
        if (!await rightsService.CanAny(entityType, RoleAction.ViewAdminTools))
            return Forbidden();

        var entity = modelService.EntityTypes[entityType];
        var res = await repository.GetAllByType(entityType, properties.ToDictionary(
            p => p,
            p => entity.GetKey(p)
        ), ct);

        return Ok(res.Select(i => i.ToDictionary(
            k => k.Key == "_id" ? "Id" : k.Key,
            v => BsonConversionTools.ConvertBasicBsonValue(v.Value)))
        );
    }
}