using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.WorkflowModel;
using Domain_Action = UvA.Workflow.Entities.Domain.Action;

namespace UvA.Workflow.WorkflowInstances;

public class InstanceService(
    IWorkflowInstanceRepository workflowInstanceRepository,
    ModelService modelService,
    IUserService userService,
    RightsService rightsService
)
{
    /// <summary>
    /// Populates references in object contexts based on the specified entity type and lookup properties.
    /// </summary>
    /// <param name="workflowDefinition">The entity type defining the properties to be enriched.</param>
    /// <param name="context">The object context whose values will be updated.</param>
    /// <param name="properties">The collection of lookup properties to be used for enrichment.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous enrichment operation.</returns>
    public async Task Enrich(WorkflowDefinition workflowDefinition, ObjectContext context,
        IEnumerable<Lookup> properties,
        CancellationToken ct)
    {
        // Resolve (replace) references in the context with their referenced objects
        var referenceProperties = properties
            .Where(p => p is PropertyLookup)
            .Cast<PropertyLookup>()
            .Distinct()
            .Where(p => p.Parts.Length > 1)
            .Where(p => workflowDefinition.Properties.Get(p.Parts[0]).DataType == DataType.Reference)
            .GroupBy(p => p.Parts[0]);
        foreach (var referenceProperty in referenceProperties)
        {
            if (workflowDefinition.Properties.Get(referenceProperty.Key).WorkflowDefinition != null)
            {
                var instanceId = context.Get(referenceProperty.Key) as string;
                if (instanceId != null)
                {
                    var instance = await workflowInstanceRepository.GetById(instanceId, ct);
                    context.Values[referenceProperty.Key] = instance;
                }
            }
        }

        // Add CurrentStep to context
        if (context.Values.TryGetValue("CurrentStep", out var i) && i is string stepName)
            context.Values["CurrentStep"] = workflowDefinition.AllSteps.Get(stepName).DisplayTitle;
    }

    public async Task UpdateCurrentStep(WorkflowInstance instance, CancellationToken ct)
    {
        var workflowDefinition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var context = modelService.CreateContext(instance);
        await Enrich(workflowDefinition, context, workflowDefinition.Steps.SelectMany(s => s.Lookups), ct);
        string? targetStep = null;
        foreach (var step in workflowDefinition.FlattenedSteps)
        {
            if (step.Condition.IsMet(context) && !step.HasEnded(context))
            {
                targetStep = step.Name;
                break;
            }
        }

        if (instance.CurrentStep != targetStep)
        {
            instance.CurrentStep = targetStep;
            if (!string.IsNullOrEmpty(instance.Id))
                await workflowInstanceRepository.UpdateField(instance.Id, i => i.CurrentStep, targetStep, ct);
        }
    }

    public async Task<Dictionary<string, ObjectContext>> GetProperties(string id, PropertyDefinition[] properties,
        CancellationToken ct)
    {
        var projection = properties.ToDictionary(p => p.Name, p => $"$Properties.{p.Name}");

        var res = await workflowInstanceRepository.GetAllById([id], projection, ct);
        return res.ToDictionary(r => r["_id"].ToString()!, r => new ObjectContext(
            properties.ToDictionary(p => (Lookup)p.Name, p => ObjectContext.GetValue(r[p.Name], p))
        ));
    }

    public async Task<List<ObjectContext>> GetScreen(string workflowDefinition, Screen screen, CancellationToken ct,
        string? sourceInstanceId = null)
    {
        var entity = modelService.WorkflowDefinitions[workflowDefinition];
        var props = screen.Columns.SelectMany(c => c.Properties).ToArray();
        var projection = props
            .Select(p => p.ToString().Split('.')[0])
            .Distinct()
            .ToDictionary(p => p, p => entity.GetKey(p));

        var res = sourceInstanceId != null
            ? await workflowInstanceRepository.GetAllByParentId(sourceInstanceId, projection, ct)
            : await workflowInstanceRepository.GetAllByType(workflowDefinition, projection, ct);
        return res.ConvertAll(r =>
        {
            var dict = projection.Keys
                .ToDictionary(
                    t => (Lookup)t,
                    t => ObjectContext.GetValue(r.GetValueOrDefault(t), entity.GetDataType(t),
                        entity.Properties.FirstOrDefault(p => p.Name == t))
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
        return users.Count(u => u.Id == user!.Id) < action.Limit.Value;
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
        WorkflowDefinition? WorkflowDefinition = null);

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
            {
                var propDef = modelService.GetQuestion(instance, rel.Property!);
                if (propDef is not null)
                {
                    actions.Add(new AllowedAction(rel, WorkflowDefinition: propDef.WorkflowDefinition));
                }
            }

        // Executable actions
        foreach (var a in allowed.Where(a => a.Type == RoleAction.Execute))
            actions.Add(new AllowedAction(a,
                Mail: await Mail.FromModel(
                    instance,
                    a.OnAction.FirstOrDefault(t =>
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

        var forms = allowed
            .SelectMany(a => a.AllForms)
            .SelectMany(a => a == Domain_Action.All
                ? modelService.WorkflowDefinitions[instance.WorkflowDefinition].Forms.Select(f => f.Name)
                : [a])
            .Distinct()
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