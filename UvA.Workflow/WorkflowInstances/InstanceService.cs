using System.Text.Json;
using UvA.Workflow.Notifications;
using UvA.Workflow.Submissions;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;
using Domain_Action = UvA.Workflow.WorkflowModel.Action;

namespace UvA.Workflow.WorkflowInstances;

public class InstanceService(
    IWorkflowInstanceRepository workflowInstanceRepository,
    ModelService modelService,
    IUserService userService,
    RightsService rightsService,
    MailBuilder mailBuilder,
    WorkflowContextEnricher contextEnricher,
    WorkflowStateResolver stateResolver
)
{
    public Dictionary<string, JsonElement?> GetPropertyValues(WorkflowInstance instance)
    {
        var values = new Dictionary<string, JsonElement?>();
        Collect(modelService.WorkflowDefinitions[instance.WorkflowDefinition].Properties, []);
        return values;

        void Collect(IEnumerable<PropertyDefinition> properties, string[] prefix)
        {
            foreach (var property in properties)
            {
                string[] parts = [.. prefix, property.Name];
                if (property is ObjectPropertyDefinition { IsArray: false, WorkflowDefinition: not null } embedded)
                {
                    Collect(embedded.WorkflowDefinition.Properties, parts);
                    continue;
                }

                try
                {
                    values[string.Join('.', parts)] = Answer.GetValue(property, instance.GetProperty(parts));
                }
                catch
                {
                    values[string.Join('.', parts)] = null;
                }
            }
        }
    }

    /// <summary>
    /// Loads values referenced through another workflow instance, such as <c>Course.Name</c>, and adds them
    /// to the supplied contexts. This overload is for callers working with one workflow definition.
    /// </summary>
    /// <param name="workflowDefinition">The entity type defining the properties to be enriched.</param>
    /// <param name="contexts">The object contexts whose values will be updated.</param>
    /// <param name="properties">The collection of lookup properties to be used for enrichment.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <param name="replaceStep">If <c>true</c>, replace the step name by the localized title</param>
    /// <returns>A task that completes when all requested values have been added.</returns>
    public Task Enrich(WorkflowDefinition workflowDefinition,
        ICollection<ObjectContext> contexts,
        IEnumerable<Lookup> properties,
        CancellationToken ct,
        bool replaceStep = true)
        => Enrich(
            [new EnrichmentBatch(workflowDefinition, contexts, properties)],
            ct,
            replaceStep);

    /// <summary>
    /// Loads extra values for several workflow definitions together. For example, if Project and
    /// Project-RMSS both need course information, all of those courses are loaded in one database call.
    /// Each result is then added back to the instance that referred to it. Values already present on the
    /// instance, including events, do not require another database call.
    /// </summary>
    /// <param name="batches">The groups of instances and values to load together.</param>
    /// <param name="ct">Stops the work if the request is cancelled.</param>
    /// <param name="replaceStep">
    /// Whether to replace internal step names with their display titles.
    /// </param>
    /// <returns>A task that completes when all requested values have been added.</returns>
    public Task Enrich(IEnumerable<EnrichmentBatch> batches, CancellationToken ct, bool replaceStep = true)
        => contextEnricher.Enrich(batches, ct, replaceStep);

    public Task<ObjectContext> ResolveContext(WorkflowInstance instance, CancellationToken ct)
        => stateResolver.Resolve(instance, ct);

    public Task<IReadOnlyDictionary<string, ObjectContext>> ResolveContexts(
        IReadOnlyCollection<WorkflowInstance> instances, CancellationToken ct)
        => stateResolver.Resolve(instances, ct);

    /// Builds a mail message, enriching referenced recipient properties (incl. template defaults) first.
    public async Task<MailMessage> BuildMail(WorkflowInstance instance, SendMessage sendMail, CancellationToken ct,
        ObjectContext? context = null)
    {
        var workflowDefinition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        context ??= await ResolveContext(instance, ct);
        var recipientLookups = MailBuilder.ResolveRecipientLookups(workflowDefinition, sendMail);
        await Enrich(workflowDefinition, [context], recipientLookups, ct, replaceStep: false);
        return mailBuilder.Build(instance, sendMail, modelService, context);
    }

    public async Task UpdateCurrentStep(WorkflowInstance instance, CancellationToken ct)
    {
        var workflowDefinition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var context = await stateResolver.Resolve(instance, ct);
        if (!workflowDefinition.HasImportedSteps)
            await Enrich(workflowDefinition, [context], workflowDefinition.Steps.SelectMany(s => s.Lookups), ct);
        var targetStep = modelService.FindOpenStep(instance, context)?.Name;

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
            return new ObjectContext(dict, ObjectContext.GetValueSets(entity));
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
            .SelectMany(r => r!.IsBsonArray
                ? r.AsBsonArray.Select(v => BsonSerializer.Deserialize<InstanceUser>(v.AsBsonDocument))
                : [BsonSerializer.Deserialize<InstanceUser>(r.AsBsonDocument)]);
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
        MailMessage? Mail = null,
        WorkflowDefinition? WorkflowDefinition = null,
        string[]? DisplaySteps = null);

    public async Task<ICollection<AllowedAction>> GetAllowedActions(WorkflowInstance instance, CancellationToken ct)
    {
        var actions = new List<AllowedAction>();
        var workflowDef = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var allowed = await rightsService.GetAllowedActions(instance,
            RoleAction.Submit, RoleAction.CreateRelatedInstance, RoleAction.Execute);
        var context = await stateResolver.Resolve(instance, ct);
        var activeSteps = modelService.GetActiveSteps(instance, context);

        // Submittable forms
        actions.AddRange(allowed
            .Where(a => a.Type == RoleAction.Submit)
            .SelectMany(a => a.AllForms
                .SelectMany(name => name == Domain_Action.All ? workflowDef.Forms.Select(f => f.Name) : [name])
                .Select(name => new { Action = a, Form = modelService.GetForm(instance, name) }))
            .Where(f => f.Form.Repeatable || !FormSubmissionState.Resolve(instance, f.Form, workflowDef).IsSubmitted)
            .Distinct()
            .Select(f => new AllowedAction(f.Action, f.Form, DisplaySteps: GetDisplaySteps(f.Action, f.Form)))
        );

        // Create related entities
        var related = allowed
            .Where(a => a.Type == RoleAction.CreateRelatedInstance)
            .DistinctBy(a => a.Property);

        foreach (var rel in related)
            if (await CheckLimit(instance, rel, ct))
            {
                var propDef = modelService.GetProperty(instance, rel.Property!);
                if (propDef is WorkflowPropertyDefinition workflowProperty)
                {
                    actions.Add(new AllowedAction(rel,
                        WorkflowDefinition: workflowProperty.WorkflowDefinition,
                        DisplaySteps: GetDisplaySteps(rel)));
                }
            }

        // Executable actions
        foreach (var a in allowed.Where(a => a.Type == RoleAction.Execute && !string.IsNullOrWhiteSpace(a.Name)))
        {
            // Build mail message for actions that send mail
            var sendMail = a.OnAction.FirstOrDefault(t =>
                t.SendMail != null && t.Condition.IsMet(context))?.SendMail;

            MailMessage? mail = null;
            if (sendMail is not null)
                mail = await BuildMail(instance, sendMail, ct);

            actions.Add(new AllowedAction(a, Mail: mail, DisplaySteps: GetDisplaySteps(a)));
        }

        return actions
            .OrderBy(a => Array.IndexOf(allowed, a.Action))
            .ToList();

        string[] GetDisplaySteps(Domain_Action action, Form? form = null)
        {
            var matchingActionSteps = modelService.GetAvailableStepsForAction(instance, action, activeSteps, context);
            if (matchingActionSteps.Length != 0)
                return matchingActionSteps;

            // Fall back to the form's own step when the action has no active steps
            return form?.Step != null ? [form.Step] : [];
        }
    }

    public record AllowedSubmission(
        FormSubmissionState SubmissionState,
        Form Form,
        Dictionary<string, QuestionStatus> QuestionStatus,
        bool CanView);

    public async Task<IEnumerable<AllowedSubmission>> GetAllowedSubmissions(WorkflowInstance instance,
        CancellationToken ct, bool includeResults = false)
    {
        var allowed = await rightsService.GetAllowedActions(instance,
            includeResults ? [RoleAction.View, RoleAction.ViewResults] : [RoleAction.View]);
        var allowedHidden = await rightsService.GetAllowedActions(instance, RoleAction.ViewHidden);

        var forms = allowed
            .SelectMany(a => a.AllForms)
            .SelectMany(a => a == Domain_Action.All
                ? modelService.WorkflowDefinitions[instance.WorkflowDefinition].Forms.Select(f => f.Name)
                : [a])
            .Distinct()
            .ToDictionary(f => f, f => modelService.GetForm(instance, f));

        var workflowDef = modelService.WorkflowDefinitions[instance.WorkflowDefinition];

        return forms
            .Values
            .Distinct()
            .Where(form => !form.Repeatable)
            .Select(form => new
            {
                Form = form,
                State = FormSubmissionState.Resolve(instance, form, workflowDef)
            })
            .Where(x => x.State.IsSubmitted)
            .OrderBy(x => workflowDef.Forms.FindIndex(f => f.Name == x.Form.Name))
            .ThenBy(x => x.State.DateSubmitted)
            .Select(x => new AllowedSubmission(x.State, x.Form,
                modelService.GetQuestionStatus(instance, x.Form, allowedHidden),
                allowed.Any(a => a.Type == RoleAction.View && a.MatchesForm(x.Form.Name))));
    }

    public async Task<IEnumerable<WorkflowInstance>> GetPossibleChoices(WorkflowInstance instance,
        ReferencePropertyDefinition property,
        CancellationToken ct)
    {
        if (property.WorkflowDefinition == null)
            throw new Exception("Missing reference");

        var instances = await workflowInstanceRepository.GetByWorkflowDefinition(property.WorkflowDefinition.Name,
            FilterDefinition<WorkflowInstance>.Empty, ct);
        var context = modelService.CreateContext(instance);

        if (property.Filter != null)
            await Enrich(property.ParentType, [context], property.Filter.Properties, ct);

        return instances.Where(i =>
        {
            context.Values[property.WorkflowDefinition.Name] = modelService.CreateContext(i).Values;
            return property.Filter.IsMet(context);
        }).ToList();
    }
}