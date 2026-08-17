using System.Text.Json;
using UvA.Workflow.Assessments;
using UvA.Workflow.Notifications;
using UvA.Workflow.Submissions;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;
using Domain_Action = UvA.Workflow.WorkflowModel.Action;

namespace UvA.Workflow.WorkflowInstances;

/// <summary>
/// A group of workflow instances that need extra values before their text and conditions can be evaluated.
/// Groups can be handled together so shared values only have to be loaded once.
/// </summary>
/// <param name="WorkflowDefinition">Describes the properties available on these instances.</param>
/// <param name="Contexts">Contains the instance values and receives the loaded values.</param>
/// <param name="Lookups">The values used by text, conditions, or the response.</param>
public sealed record EnrichmentBatch(
    WorkflowDefinition WorkflowDefinition,
    ICollection<ObjectContext> Contexts,
    IEnumerable<Lookup> Lookups);

public class InstanceService(
    IWorkflowInstanceRepository workflowInstanceRepository,
    ModelService modelService,
    IUserService userService,
    RightsService rightsService,
    MailBuilder mailBuilder,
    IAssessmentService assessmentService
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
                if (property is { DataType: DataType.Object, IsArray: false, WorkflowDefinition: not null })
                {
                    Collect(property.WorkflowDefinition.Properties, parts);
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
    public async Task Enrich(
        IEnumerable<EnrichmentBatch> batches,
        CancellationToken ct,
        bool replaceStep = true)
    {
        // Assessments need some course settings in addition to the values requested by the caller.
        var states = batches.Select(batch =>
        {
            var lookups = batch.Lookups.ToList();
            var hasAssessment = lookups.Any(lookup =>
                                    lookup is PropertyLookup property && property.Parts[0] == "Assessment")
                                && batch.WorkflowDefinition.AssessmentConfiguration != null;
            if (hasAssessment)
                lookups.AddRange(
                    new PropertyLookup("Course.GradingBasis"),
                    new PropertyLookup("Course.GradeGap"));
            return new EnrichmentState(batch, lookups, hasAssessment);
        }).ToArray();

        var referenceEnrichments = states
            .SelectMany(GetReferenceEnrichments)
            .ToArray();

        // Work out which referenced instances and values are needed. All workflow types are stored together,
        // so they can be loaded in one database call even when the source instances have different types.
        var targetGroups = referenceEnrichments
            .GroupBy(enrichment => enrichment.TargetDefinition.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var referenceIds = targetGroups
            .SelectMany(targetGroup => targetGroup
                .SelectMany(enrichment => enrichment.Contexts.SelectMany(context =>
                    GetReferenceIds(context.Get(enrichment.ReferenceProperty)))))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (referenceIds.Length > 0)
        {
            var projectedProperties = targetGroups
                .SelectMany(targetGroup => targetGroup.SelectMany(enrichment => enrichment.ProjectedProperties))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var results = await workflowInstanceRepository.GetAllById(
                referenceIds,
                projectedProperties.ToDictionary(property => property, property => $"$Properties.{property}"),
                ct);
            var resultsById = results.ToDictionary(result => result["_id"].ToString()!);

            // Interpret each result using its own workflow definition, then add it to the instances that use it.
            foreach (var targetGroup in targetGroups)
            {
                var targetDefinition = targetGroup.First().TargetDefinition;
                var targetIds = targetGroup
                    .SelectMany(enrichment => enrichment.Contexts.SelectMany(context =>
                        GetReferenceIds(context.Get(enrichment.ReferenceProperty))))
                    .Distinct(StringComparer.Ordinal);
                var resultContexts = targetIds
                    .Where(resultsById.ContainsKey)
                    .ToDictionary(
                        id => id,
                        id => modelService.CreateContext(targetDefinition.Name, resultsById[id]).Values,
                        StringComparer.Ordinal);

                foreach (var enrichment in targetGroup)
                {
                    foreach (var context in enrichment.Contexts)
                    {
                        context.Values[enrichment.ReferenceProperty] =
                            context.Get(enrichment.ReferenceProperty) switch
                            {
                                string reference => resultContexts.GetValueOrDefault(reference),
                                string[] references => references
                                    .Select(resultContexts.GetValueOrDefault)
                                    .Where(result => result != null)
                                    .ToArray(),
                                var value => value
                            };
                    }
                }
            }
        }

        // Calculate assessment values using the settings of the original workflow instances.
        foreach (var state in states.Where(state => state.HasAssessment))
        {
            foreach (var context in state.Batch.Contexts)
            {
                var config = state.Batch.WorkflowDefinition.AssessmentConfiguration;
                config!.Enrich(context.Get("Course.GradingBasis") as string, context.Get("Course.GradeGap") as bool?);
                var result = assessmentService.GetAssessmentResult(
                    state.Batch.WorkflowDefinition,
                    context,
                    config);
                var dict = result.PartResults.ToDictionary(r => r.Name,
                    object (r) => r.AllResults.ToDictionary(s => s.Name,
                        object (s) =>
                        {
                            var questionResults = s.PageResults
                                .SelectMany(p => p.QuestionResults)
                                .ToDictionary(q => q.Name, object (q) => q.Answer);
                            questionResults.Add("WeightedAverage", s.WeightedAverage);
                            return questionResults;
                        }));
                dict["FinalGrade"] = result.FinalGradeRounded;
                context.Values["Assessment"] = dict;
            }
        }

        // Replace internal step names using the titles from the original workflow definition.
        if (replaceStep)
        {
            foreach (var state in states)
            {
                foreach (var context in state.Batch.Contexts)
                {
                    if (context.Values.TryGetValue("CurrentStep", out var i) && i is string stepName)
                        context.Values["CurrentStep"] =
                            state.Batch.WorkflowDefinition.AllSteps.GetOrDefault(stepName)?.DisplayTitle ?? stepName;
                }
            }
        }
    }

    /// <summary>
    /// Groups values that belong to the same referenced instance. For example, <c>Course.Name</c> and
    /// <c>Course.Type</c> can be loaded together for each course.
    /// </summary>
    private static IEnumerable<ReferenceEnrichment> GetReferenceEnrichments(EnrichmentState state)
    {
        var workflowDefinition = state.Batch.WorkflowDefinition;
        var referenceProperties = state.Lookups
            .OfType<PropertyLookup>()
            .Distinct()
            .Where(property => property.Parts.Length > 1)
            .Where(property =>
                workflowDefinition.Properties.GetOrDefault(property.Parts[0])?.DataType == DataType.Reference)
            .GroupBy(property => property.Parts[0]);

        foreach (var referenceProperty in referenceProperties)
        {
            var targetDefinition = workflowDefinition.Properties
                .GetOrDefault(referenceProperty.Key)
                ?.WorkflowDefinition;
            if (targetDefinition == null)
                continue;

            yield return new ReferenceEnrichment(
                targetDefinition,
                state.Batch.Contexts,
                referenceProperty.Key,
                referenceProperty.Select(property => property.Parts[1]).Distinct().ToArray());
        }
    }

    /// <summary>Returns the IDs stored in a single reference or a list of references.</summary>
    private static IEnumerable<string> GetReferenceIds(object? value)
        => value switch
        {
            string reference => [reference],
            string[] references => references,
            _ => []
        };

    private sealed record EnrichmentState(
        EnrichmentBatch Batch,
        List<Lookup> Lookups,
        bool HasAssessment);

    private sealed record ReferenceEnrichment(
        WorkflowDefinition TargetDefinition,
        ICollection<ObjectContext> Contexts,
        string ReferenceProperty,
        string[] ProjectedProperties);

    /// Builds a mail message, enriching referenced recipient properties (incl. template defaults) first.
    public async Task<MailMessage> BuildMail(WorkflowInstance instance, SendMessage sendMail, CancellationToken ct)
    {
        var workflowDefinition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var context = modelService.CreateContext(instance);
        var recipientLookups = MailBuilder.ResolveRecipientLookups(workflowDefinition, sendMail);
        await Enrich(workflowDefinition, [context], recipientLookups, ct, replaceStep: false);
        return mailBuilder.Build(instance, sendMail, modelService, context);
    }

    public async Task UpdateCurrentStep(WorkflowInstance instance, CancellationToken ct)
    {
        var workflowDefinition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var context = modelService.CreateContext(instance);
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
        var allowed = await rightsService.GetAllowedActions(instance,
            RoleAction.Submit, RoleAction.CreateRelatedInstance, RoleAction.Execute);

        var actions = new List<AllowedAction>();
        var workflowDef = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var activeSteps = modelService.GetActiveSteps(instance);

        // Submittable forms
        actions.AddRange(allowed
            .Where(a => a.Type == RoleAction.Submit)
            .SelectMany(a => a.AllForms.Select(f => new { Action = a, Form = f }))
            .Where(f => !FormSubmissionState.Resolve(instance, modelService.GetForm(instance, f.Form), workflowDef)
                .IsSubmitted)
            .Distinct()
            .Select(f =>
            {
                var form = modelService.GetForm(instance, f.Form);
                return new AllowedAction(f.Action, form, DisplaySteps: GetDisplaySteps(f.Action, form));
            })
        );

        // Create related entities
        var related = allowed
            .Where(a => a.Type == RoleAction.CreateRelatedInstance)
            .DistinctBy(a => a.Property);

        foreach (var rel in related)
            if (await CheckLimit(instance, rel, ct))
            {
                var propDef = modelService.GetProperty(instance, rel.Property!);
                if (propDef is not null)
                {
                    actions.Add(new AllowedAction(rel,
                        WorkflowDefinition: propDef.WorkflowDefinition,
                        DisplaySteps: GetDisplaySteps(rel)));
                }
            }

        // Executable actions
        foreach (var a in allowed.Where(a => a.Type == RoleAction.Execute))
        {
            // Build mail message for actions that send mail
            var sendMail = a.OnAction.FirstOrDefault(t =>
                t.SendMail != null && t.Condition.IsMet(modelService.CreateContext(instance)))?.SendMail;

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
            var matchingActionSteps = action.Steps.Intersect(activeSteps).ToArray();
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
        var hiddenForms = allowedHidden.SelectMany(a => a.AllForms).Distinct().ToList();

        var workflowDef = modelService.WorkflowDefinitions[instance.WorkflowDefinition];

        return forms
            .Values
            .Distinct()
            .Select(form => new
            {
                Form = form,
                State = FormSubmissionState.Resolve(instance, form, workflowDef)
            })
            .Where(x => x.State.IsSubmitted)
            .OrderBy(x => workflowDef.Forms.FindIndex(f => f.Name == x.Form.Name))
            .ThenBy(x => x.State.DateSubmitted)
            .Select(x => new AllowedSubmission(x.State, x.Form,
                modelService.GetQuestionStatus(instance, x.Form, hiddenForms.Contains(x.Form.Name)),
                allowed.Any(a => a.Type == RoleAction.View && a.MatchesForm(x.Form.Name))));
    }

    public async Task<IEnumerable<WorkflowInstance>> GetPossibleChoices(WorkflowInstance instance,
        PropertyDefinition property,
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