using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using UvA.Workflow.Api.Authentication;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Notifications;
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
    IAnswerService answerService,
    ModelService modelService,
    RoleImpersonationService impersonationService,
    IEduIdUserService eduIdUserService,
    UndoService undoService,
    IMailLogRepository mailLogRepository
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

    [HttpPost("{id}/Undo")]
    public async Task<ActionResult<WorkflowInstanceDto>> Undo(
        string id, [FromBody] UndoRequest input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.OperationId))
            return BadRequest("InvalidUndoOperation", "Operation identifier is required.");

        var reason = input.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
            return BadRequest("InvalidUndoReason", "Undo reason must contain between 1 and 1,000 characters.");

        var currentUser = await userService.GetCurrentUser(ct);
        if (currentUser == null)
            return Unauthorized();

        var instance = await repository.GetById(id, ct);
        if (instance == null)
            return WorkflowInstanceNotFound;

        var realUser = await userService.GetRealUser(ct);
        if (realUser == null)
            return Unauthorized();

        try
        {
            await undoService.Undo(instance, input.OperationId, reason, realUser, ct);
        }
        catch (ForbiddenWorkflowActionException)
        {
            return Forbidden();
        }
        catch (UndoCandidateChangedException exception)
        {
            return Conflict(exception.Code, exception.Message);
        }

        return Ok(await workflowInstanceDtoFactory.Create(instance, ct));
    }

    /// <summary>
    /// Returns all properties and values available to the admin data view.
    /// </summary>
    [HttpGet("{id}/Properties")]
    public async Task<ActionResult<InstancePropertiesDto>> GetProperties(string id, CancellationToken ct)
    {
        var instance = await repository.GetById(id, ct);
        if (instance == null)
            return WorkflowInstanceNotFound;

        if (!await rightsService.Can(instance, [RoleAction.ViewAdminTools], RightsEvaluationMode.RealUser))
            return Forbidden();

        var definition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];

        // The admin view ignores property conditions and visibility.
        var properties = definition.Properties
            .Select(p => QuestionDto.Create(p, totalWeight: 0))
            .ToArray();

        return Ok(new InstancePropertiesDto(properties, await instanceService.GetPropertyValues(instance, ct)));
    }

    /// <summary>
    /// Returns all choices for a property to the admin data view.
    /// </summary>
    [HttpGet("{id}/Properties/{path}/Choices")]
    public async Task<ActionResult<IEnumerable<ChoiceDto>>> GetPropertyChoices(string id, string path,
        CancellationToken ct)
    {
        var instance = await repository.GetById(id, ct);
        if (instance == null)
            return WorkflowInstanceNotFound;

        if (!await rightsService.Can(instance, [RoleAction.ViewAdminTools], RightsEvaluationMode.RealUser))
            return Forbidden();

        var property = modelService.GetProperty(instance, path.Split('.'));
        if (property == null)
            return NotFound("PropertyNotFound", $"Property '{path}' does not exist");

        if (property is not ReferencePropertyDefinition { WorkflowDefinition: not null } reference)
            return Ok(Array.Empty<ChoiceDto>());
        var insts = await instanceService.GetPossibleChoices(instance, reference, ct);
        var definition = reference.WorkflowDefinition;
        return Ok(insts.Select(i => new ChoiceDto(
            i.Id,
            definition.InstanceTitleTemplate?.Execute(modelService.CreateContext(i)) ?? "nameless",
            null,
            null)));
    }

    /// <summary>
    /// Overrides a single property. Always recorded in the instance journal.
    /// </summary>
    [HttpPost("{id}/Properties/{path}")]
    public async Task<ActionResult> SaveProperty(string id, string path,
        [FromBody] SaveInstancePropertyRequest input, CancellationToken ct)
    {
        var instance = await repository.GetById(id, ct);
        if (instance == null)
            return WorkflowInstanceNotFound;

        var parts = path.Split('.');
        if (parts.Length > 2)
            return BadRequest("PropertyPathTooDeep",
                $"Cannot edit '{path}': only properties nested at most one level deep can be saved.");

        if (modelService.GetProperty(instance, parts) == null)
            return NotFound("PropertyNotFound", $"Property '{path}' does not exist");

        if (!await rightsService.CanEditProperty(instance, path))
            return Forbidden();

        await answerService.SaveProperty(instance, parts, input.Value, shouldLog: true, ct);

        return NoContent();
    }

    [HttpGet("{id}/Impersonation/Roles")]
    public async Task<ActionResult<IEnumerable<ImpersonationRoleDto>>> GetImpersonationRoles(string id,
        CancellationToken ct)
    {
        var instance = await repository.GetById(id, ct);
        if (instance == null)
            return WorkflowInstanceNotFound;

        if (!await rightsService.Can(instance, [RoleAction.ImpersonateRoles], RightsEvaluationMode.RealUser))
            return Forbidden();

        var roles = (await rightsService.GetImpersonationTargetRoles(instance))
            .Select(ImpersonationRoleDto.Create)
            .ToArray();

        return Ok(roles);
    }

    /// <summary>Returns allowed actions for each impersonatable role, grouped by linked step.</summary>
    [HttpGet("{id}/Impersonation/Actions")]
    public async Task<ActionResult<IEnumerable<ActiveStepDto>>> GetImpersonationActions(string id,
        CancellationToken ct)
    {
        var instance = await repository.GetById(id, ct);
        if (instance == null)
            return WorkflowInstanceNotFound;

        if (!await rightsService.Can(instance, [RoleAction.ImpersonateRoles], RightsEvaluationMode.RealUser))
            return Forbidden();

        var definition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var perRole = await rightsService.GetAllowedActionsPerTargetRole(instance,
            RoleAction.Submit, RoleAction.Edit, RoleAction.Execute, RoleAction.CreateRelatedInstance,
            RoleAction.Undo, RoleAction.View);

        var context = await instanceService.CreateContext(instance, definition.PermissionAndStepLookups, ct);
        var currentStepName = modelService.GetCurrentStep(instance, context)?.Name;
        var activeSteps = modelService.GetActiveSteps(instance, context);
        var actionSteps = perRole.SelectMany(r => r.Actions)
            .Distinct()
            .ToDictionary(a => a, a => modelService.GetAvailableStepsForAction(instance, a, activeSteps, context));

        // Include completed steps with persistent actions, preserving the active-step order.
        var steps = activeSteps
            .Concat(perRole.SelectMany(r => r.Actions.Where(a => a.Persistent)
                .SelectMany(a => actionSteps[a])))
            .Distinct()
            .Select(definition.AllSteps.GetOrDefault)
            .OfType<Step>()
            .OrderByDescending(s => s.Name == currentStepName)
            .Select(s => new ActiveStepDto(
                s.Name,
                s.DisplayTitle,
                s.Name == currentStepName,
                RolesFor(s.Name)))
            .ToArray();

        return Ok(steps);

        ActiveStepRoleDto[] RolesFor(string stepName)
            => perRole
                .Select(r => new ActiveStepRoleDto(r.Role.Name, r.Role.Title,
                    r.Actions.Where(a => (a.Type != RoleAction.View || a.Persistent) &&
                                         actionSteps[a].Contains(stepName))
                        .Select(AllowedActionDto.Create).ToArray()))
                .Where(r => r.Actions.Length > 0)
                .ToArray();
    }

    [HttpPost("{id}/Impersonation")]
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

        var normalizedRoleName = await rightsService.NormalizeImpersonationTargetRole(instance, input.Role);
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

    /// <summary>
    /// Endpoint used to create an export of grading data for PPLE
    /// TODO: remove this or use it to create some kind of generic export function 
    /// </summary>
    [Authorize(AuthenticationSchemes = WorkflowAuthenticationDefaults.AnyScheme)]
    [HttpGet("Instances/{workflowDefinition}/Full")]
    public async Task<ActionResult<IEnumerable<Dictionary<string, object>>>> GetFullInstances(string workflowDefinition,
        [FromQuery] string[] properties, CancellationToken ct)
    {
        if (!await rightsService.CanAny(workflowDefinition, RoleAction.ViewAdminTools))
            return Forbidden();

        var res = await repository.GetAll(i => i.WorkflowDefinition == workflowDefinition, ct);
        var contexts = res.Select(modelService.CreateContext).ToList();
        var entity = modelService.WorkflowDefinitions[workflowDefinition];

        await instanceService.Enrich(entity, contexts,
            properties.Select(p => new PropertyLookup(p)), ct);

        return Ok(contexts
            .OrderByDescending(i => i.Id)
            .Select(row => properties.ToDictionary(
                JsonNamingPolicy.CamelCase.ConvertName,
                p => GetResponseValue(entity, row, p)
            )));
    }

    [Authorize(AuthenticationSchemes = WorkflowAuthenticationDefaults.AnyScheme)]
    [HttpGet("Instances/{workflowDefinition}")]
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

        var requestedProperties = properties
            .Append("CreatedOn")
            .Concat(titleProperties)
            .Distinct()
            .ToArray();
        var lookups = requestedProperties.Select(property => (Lookup)property).ToArray();
        var projection = entity.GetProjection(lookups);

        var res = await repository.GetAllByType(workflowDefinition, projection, ct);
        var data = res.Select(row => (Row: row,
            Context: modelService.CreateContext(workflowDefinition, row, projection.Keys))).ToArray();
        await instanceService.Enrich(entity, data.Select(row => row.Context).ToArray(), lookups, ct, false);

        return Ok(data
            .OrderByDescending(item => item.Row.GetValueOrDefault("_id"))
            .Select(item =>
            {
                var (i, context) = item;
                var row = i.Where(entry => entry.Key == "_id" || requestedProperties.Contains(entry.Key)).ToDictionary(
                    k => k.Key == "_id" ? "Id" : k.Key,
                    v => BsonConversionTools.ConvertBasicBsonValue(v.Value));
                foreach (var property in requestedProperties.Where(property =>
                             property.Contains('.') ||
                             modelService.GetProperty(entity, property.Split('.')) is { IsComputed: true } or
                                 { DataType: DataType.Object or DataType.Reference } ||
                             property == "LastEvent" || entity.Events.Any(ev =>
                                 property == ev.Name + "Event" || property == ev.Name + "EventActive")))
                    row[property] = GetResponseValue(entity, context, property);
                if (titleTemplate != null)
                    row["Title"] = titleTemplate.Apply(context);
                return row;
            })
            .Select(row => row.ToDictionary(
                k => JsonNamingPolicy.CamelCase.ConvertName(k.Key),
                v => v.Value
            )));
    }

    [Authorize(AuthenticationSchemes = WorkflowAuthenticationDefaults.AnyScheme)]
    [HttpPost("Instances/{workflowDefinition}/RecalculateCurrentStep")]
    public async Task<ActionResult<RecalculateCurrentStepsResultDto>> RecalculateCurrentSteps(
        string workflowDefinition, CancellationToken ct)
    {
        if (!modelService.WorkflowDefinitions.ContainsKey(workflowDefinition))
            return NotFound("EntityTypeNotFound", $"Entity type '{workflowDefinition}' not found.");

        if (!await rightsService.CanAny(workflowDefinition, RoleAction.ViewAdminTools))
            return Forbidden();

        var instances = await repository.GetAll(i => i.WorkflowDefinition == workflowDefinition, ct);
        var updated = 0;
        foreach (var instance in instances)
        {
            ct.ThrowIfCancellationRequested();
            var previousStep = instance.CurrentStep;
            await instanceService.UpdateCurrentStep(instance, ct);
            if (instance.CurrentStep != previousStep)
                updated++;
        }

        return Ok(new RecalculateCurrentStepsResultDto(
            Total: instances.Count,
            Updated: updated,
            Unchanged: instances.Count - updated
        ));
    }


    [HttpPost("{id}/RelatedUsers/{property}")]
    public async Task<ActionResult> AssignRelatedUser(string id, string property,
        [FromBody] AssignRelatedUserRequest input, CancellationToken ct)
    {
        if (await userService.GetCurrentUser(ct) == null)
            return Unauthorized();

        var instance = await repository.GetById(id, ct);
        if (instance == null)
            return WorkflowInstanceNotFound;

        if (!await rightsService.CanEditProperty(instance, property))
            return Forbidden();

        var propertyDefinition = GetRelatedUserProperty(instance, property);
        if (propertyDefinition == null)
            return BadRequest("InvalidRelatedUser", $"Related user '{property}' does not exist");

        modelService.EnsureWritable(instance, property.Split('.'));
        var externalUserInput = input.ExternalUser is { } eu
            ? new ExternalUserInput(eu.DisplayName, eu.Email, eu.Organization)
            : null;

        try
        {
            var (value, createdUser) = await answerService.ValidateAndResolveValue(
                propertyDefinition, input.User, externalUserInput, ct);

            var newValue = await answerConversionService.ConvertToValue(value, propertyDefinition, ct);
            var pathParts = property.Split('.');
            if (propertyDefinition.IsArray)
                await workflowInstanceService.AppendPropertyValue(instance, pathParts, newValue, ct);
            else
                await answerService.SavePropertyValue(instance, pathParts, propertyDefinition, newValue,
                    shouldLog: true, ct);

            if (createdUser != null)
            {
                await eduIdUserService.EnsureExternalAccount(
                    createdUser.Email,
                    createdUser.DisplayName,
                    EduIdInviteDeliveryMode.SendEmail,
                    ct);
            }

            return NoContent();
        }
        catch (ExternalUserCreationException ex)
        {
            return MapExternalUserCreationError(ex);
        }
    }

    [HttpDelete("{id}/RelatedUsers/{property}/{userId}")]
    public async Task<ActionResult> RemoveRelatedUser(string id, string property, string userId, CancellationToken ct)
    {
        if (await userService.GetCurrentUser(ct) == null)
            return Unauthorized();

        var instance = await repository.GetById(id, ct);
        if (instance == null)
            return WorkflowInstanceNotFound;

        if (!await rightsService.CanEditProperty(instance, property))
            return Forbidden();

        var propertyDefinition = GetRelatedUserProperty(instance, property);
        if (propertyDefinition == null)
            return BadRequest("InvalidRelatedUser", $"Related user '{property}' does not exist");

        var pathParts = property.Split('.');
        var currentValue = instance.GetProperty(pathParts);

        bool IsRequestedUser(BsonValue? value) =>
            value is BsonDocument user && user.GetValue("_id", BsonNull.Value).ToString() == userId;

        BsonValue newValue;
        if (propertyDefinition.IsArray)
        {
            if (currentValue is not BsonArray users || users.All(user => !IsRequestedUser(user)))
                return NoContent();

            var remaining = new BsonArray(users.Where(user => !IsRequestedUser(user)));
            newValue = remaining.Count == 0 ? BsonNull.Value : remaining;
        }
        else
        {
            if (!IsRequestedUser(currentValue))
                return NoContent();

            newValue = BsonNull.Value;
        }

        await answerService.SavePropertyValue(instance, pathParts, propertyDefinition, newValue, shouldLog: true, ct);
        return NoContent();
    }

    [HttpGet("{id}/Correspondence")]
    public async Task<ActionResult<IReadOnlyList<CorrespondenceDto>>> GetCorrespondence(string id, CancellationToken ct)
    {
        var instance = await repository.GetById(id, ct);
        if (instance == null)
            return WorkflowInstanceNotFound;

        if (!await rightsService.Can(instance, RoleAction.ViewCorrespondence))
            return Forbidden();

        var entries = await mailLogRepository.GetByInstance(id, ct);
        return Ok(entries.Select(CorrespondenceDto.From).ToList());
    }

    private object? GetResponseValue(WorkflowDefinition definition, ObjectContext context, string path)
    {
        var value = context.Get(path);
        return modelService.GetProperty(definition, path.Split('.')) is
            { DataType: DataType.Reference } property
            ? Answer.SerializeValue(property, value)
            : value;
    }

    private PropertyDefinition? GetRelatedUserProperty(WorkflowInstance instance, string property)
        => modelService.WorkflowDefinitions[instance.WorkflowDefinition].EditableRelatedUsers
                .FirstOrDefault(relatedUser => relatedUser.Property == property)?.PropertyDefinition
            is { DataType: DataType.User } definition
            ? definition
            : null;
}