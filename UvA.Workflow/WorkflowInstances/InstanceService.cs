using UvA.Workflow.Infrastructure;
using Domain_Action = UvA.Workflow.Entities.Domain.Action;

namespace UvA.Workflow.Services;

public class InstanceService(
    IWorkflowInstanceRepository workflowInstanceRepository,
    ModelService modelService,
    IUserService userService,
    RightsService rightsService)
{
    public async Task<Dictionary<string, ObjectContext>> GetProperties(string[] ids, Question[] properties,
        CancellationToken ct)
    {
        var projection = properties.ToDictionary(p => p.Name, p => $"$Properties.{p.Name}");

        var res = await workflowInstanceRepository.GetAllById(ids, projection, ct);
        return res.ToDictionary(r => r["_id"].ToString()!, r => new ObjectContext(
            properties.ToDictionary(p => (Lookup)p.Name, p => ObjectContext.GetValue(r[p.Name], p))
        ));
    }

    public async Task<List<ObjectContext>> GetScreen(string entityType, Screen screen, CancellationToken ct,
        string? sourceInstanceId = null)
    {
        var entity = modelService.EntityTypes[entityType];
        var props = screen.Columns.SelectMany(c => c.Properties).ToArray();
        var projection = props
            .Select(p => p.ToString().Split('.')[0])
            .Distinct()
            .ToDictionary(p => p, p => entity.GetKey(p));

        var res = sourceInstanceId != null
            ? await workflowInstanceRepository.GetAllByParentId(sourceInstanceId, projection, ct)
            : await workflowInstanceRepository.GetAllByType(entityType, projection, ct);
        return res.ConvertAll(r =>
        {
            var dict = projection.Keys
                .ToDictionary(
                    t => (Lookup)t,
                    t => ObjectContext.GetValue(r.GetValueOrDefault(t), entity.GetDataType(t),
                        entity.Properties.GetValueOrDefault(t))
                );
            dict["Id"] = r["_id"].ToString();
            return new ObjectContext(dict);
        });
    }

    public async Task<bool> CheckLimit(WorkflowInstance instance, Domain_Action action, CancellationToken ct)
    {
        if (action.UserProperty == null || action.Limit == null)
            return true;
        var property = action.UserProperty;
        var results = await workflowInstanceRepository.GetAllByParentId(instance.Id, new()
        {
            [property] = $"$Properties.{property}"
        }, ct);
        var users = results
            .Select(r => r.GetValueOrDefault(property))
            .Where(r => r?.IsBsonNull == false)
            .Select(r => BsonSerializer.Deserialize<User>(r!.AsBsonDocument));
        var user = await userService.GetCurrentUser(ct);
        return users.Count(u => u.Id == user.Id) < action.Limit.Value;
    }

    public async Task UpdateEvent(WorkflowInstance instance, string eventId, CancellationToken ct)
    {
        instance.RecordEvent(eventId);
        await workflowInstanceRepository.Update(instance, ct);
    }

    /// <summary>
    /// Deletes a specific event from the given workflow instance based on the provided event ID.
    /// </summary>
    /// <param name="instance">The workflow instance from which the event will be deleted.</param>
    /// <param name="eventId">The unique identifier of the event to be deleted.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <exception cref="EntityNotFoundException">
    /// Thrown when the specified event ID is not found within the workflow instance.
    /// </exception>
    public async Task DeleteEvent(WorkflowInstance instance, string eventId, CancellationToken ct)
    {
        await rightsService.EnsureAuthorizedForAction(instance, RoleAction.ViewAdminTools);

        // TODO: needs to be updated to remove the most recent event with the specified eventId once multiple events of same id per workflowinstance is implemented
        if (instance.Events.Remove(eventId))
        {
            await workflowInstanceRepository.DeleteField(instance.Id, i => i.Events[eventId], ct);
        }
        else
            throw new EntityNotFoundException(nameof(InstanceEvent), eventId);
    }

    public Task SaveValue(WorkflowInstance instance, string? part1, string part2, CancellationToken ct)
        => workflowInstanceRepository.UpdateFields(instance.Id,
            Builders<WorkflowInstance>.Update.Set(part1 == null
                    ? (i => i.Properties[part2])
                    : (i => i.Properties[part1][part2]),
                instance.GetProperty(part1, part2)), ct);

    public Task UnsetValue(WorkflowInstance instance, string? part1, string part2, CancellationToken ct)
        => workflowInstanceRepository.UpdateFields(instance.Id,
            Builders<WorkflowInstance>.Update.Unset(part1 == null
                ? (i => i.Properties[part2])
                : (i => i.Properties[part1][part2])), ct);

    public record AllowedAction(
        Domain_Action Action,
        Form? Form = null,
        Mail? Mail = null,
        EntityType? EntityType = null);

    public async Task<ICollection<AllowedAction>> GetAllowedActions(WorkflowInstance instance, CancellationToken ct)
    {
        var allowed = await rightsService.GetAllowedActions(instance,
            RoleAction.Submit, RoleAction.CreateRelatedInstance, RoleAction.Execute);

        var actions = new List<AllowedAction>();

        // Submittable forms
        actions.AddRange(allowed
            .Where(a => a.Type == RoleAction.Submit)
            .SelectMany(a => a.AllForms.Select(f => new { Action = a, Form = f }))
            .Where(f => instance.Events.GetValueOrDefault(f.Form)?.Date == null)
            .Distinct()
            .Select(f => new AllowedAction(f.Action, modelService.GetForm(instance, f.Form)))
        );

        // Create related entities
        var related = allowed
            .Where(a => a.Type == RoleAction.CreateRelatedInstance)
            .DistinctBy(a => a.Property);

        foreach (var rel in related)
            if (await CheckLimit(instance, rel, ct))
                actions.Add(new AllowedAction(rel,
                    EntityType: modelService.GetQuestion(instance, rel.Property!).EntityType));

        // Executable actions
        foreach (var a in allowed.Where(a => a.Type == RoleAction.Execute))
            actions.Add(new AllowedAction(a,
                Mail: await Mail.FromModel(
                    instance,
                    a.Triggers.FirstOrDefault(t =>
                        t.SendMail != null && t.Condition.IsMet(modelService.CreateContext(instance)))?.SendMail,
                    modelService)));

        return actions;
    }

    public record AllowedSubmission(InstanceEvent Event, Form Form, Dictionary<string, QuestionStatus> QuestionStatus);

    public async Task<IEnumerable<AllowedSubmission>> GetAllowedSubmissions(WorkflowInstance instance,
        CancellationToken ct)
    {
        var allowed = await rightsService.GetAllowedActions(instance, RoleAction.View);
        var allowedHidden = await rightsService.GetAllowedActions(instance, RoleAction.ViewHidden);

        var forms = allowed.SelectMany(a => a.AllForms).Distinct()
            .ToDictionary(f => f, f => modelService.GetForm(instance, f));
        var hiddenForms = allowedHidden.SelectMany(a => a.AllForms).Distinct().ToList();

        var subs = instance.Events
            .Select(e => e.Value)
            .Where(s => forms.ContainsKey(s.Id))
            .OrderBy(s => s.Date)
            .ToList();
        return subs.Select(s => new AllowedSubmission(s, forms[s.Id],
            modelService.GetQuestionStatus(instance, forms[s.Id], hiddenForms.Contains(s.Id))));
    }
}