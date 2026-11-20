using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Submissions;

public partial class FormPreviewService
{
    private async Task<(WorkflowInstance Snapshot, ObjectContext Context)> CreateSnapshot(
        WorkflowInstance instance, Form selected, CancellationToken ct, string? answerInstanceId = null)
    {
        var sources = selected.ActualForm.Pages.SelectMany(page => page.Sources ?? [])
            .Concat(selected.ActualForm.PropertyDefinitions.SelectMany(question => question.Sources ?? []))
            .ToHashSet();
        var selectedPaths = selected.WorkflowDefinition.Forms
            .Where(form => form == selected || sources.Contains(form.Name) || sources.Contains(form.PropertyName ?? ""))
            .SelectMany(form =>
                form.ActualForm.PropertyDefinitions.Select(question => form.PropertyName ?? question.Name))
            .Concat(sources).ToHashSet();
        var visiting = new HashSet<string>();
        return await Build(instance, []);

        async Task<(WorkflowInstance, ObjectContext)> Build(WorkflowInstance real, IEnumerable<string> inheritedPaths)
        {
            visiting.Add(real.Id);
            var definition = modelService.WorkflowDefinitions[real.WorkflowDefinition];
            var context = await ContextLoader.CreateContext(modelService, repository, real,
                definition.Fields.SelectMany(field => field.Condition?.Properties ?? []), ct);
            var paths = new HashSet<string>(inheritedPaths);
            var canView = await rightsService.Can(real, RoleAction.View);
            if (canView)
            {
                paths.UnionWith(definition.Fields
                    .Where(field => !field.CurrentStep && field.ValueTemplate == null && field.Condition.IsMet(context))
                    .Select(field => field.Property).OfType<string>());
                var roles = await rightsService.GetViewerRoles(real, ct);
                foreach (var card in definition.InfoCards.Where(card => card.Enabled && card.Type != null &&
                                                                        (card.Sources is not { Length: > 0 } ||
                                                                         card.Sources.Intersect(roles).Any()) &&
                                                                        (card.ExcludedSources is not { Length: > 0 } ||
                                                                         !card.ExcludedSources.Intersect(roles).Any())))
                {
                    paths.UnionWith(card.Fields
                        .Where(field =>
                            !field.CurrentStep && field.ValueTemplate == null && field.Condition.IsMet(context))
                        .Select(field => field.Property).OfType<string>());
                    if (card.User != null)
                        paths.UnionWith(new[] { card.User + ".DisplayName", card.User + ".Picture" });
                }
            }

            var views = await rightsService.GetAllowedActions(real, RoleAction.View);
            var hidden = await rightsService.GetAllowedActions(real, RoleAction.ViewHidden);
            foreach (var form in definition.Forms.Where(form => form.TargetForm == null &&
                                                                views.Any(view => view.MatchesForm(form.Name)) &&
                                                                FormSubmissionState.Resolve(real, form, definition)
                                                                    .IsSubmitted))
                paths.UnionWith(modelService.GetQuestionStatus(real, form, hidden, context: context)
                    .Where(question => question.Value.IsVisible).Select(question => question.Key));

            var blocked = definition.Forms.Where(form => form.TargetForm != null)
                .Select(form => form.PropertyName!).ToHashSet();
            if (real.Id == (answerInstanceId ?? instance.Id))
                blocked.UnionWith(selectedPaths);
            paths.RemoveWhere(path => blocked.Contains(path.Split('.')[0]));
            var snapshot = new WorkflowInstance
            {
                Id = real.Id, WorkflowDefinition = real.WorkflowDefinition,
                CurrentStep = canView ? real.CurrentStep : null,
                CreatedOn = real.CreatedOn, Events = canView ? new(real.Events) : [], Properties = []
            };
            foreach (var path in paths)
            {
                var parts = path.Split('.');
                var property = definition.Properties.GetOrDefault(parts[0]);
                if (property == null || property.IsComputed || property.DataType == DataType.File)
                    continue;
                var copyParts = property is ReferencePropertyDefinition ? [parts[0]] : parts;
                var value = real.GetProperty(copyParts);
                if (value != null)
                    snapshot.SetProperty(FilterStoredValue(value, property, parts.Length > 1), copyParts);
            }

            var projected = modelService.CreateContext(snapshot);
            foreach (var reference in definition.Properties.OfType<ReferencePropertyDefinition>()
                         .Where(property => snapshot.HasAnswer(property.Name)))
            {
                var ids = snapshot.GetProperty(reference.Name) switch
                {
                    BsonString id => [id.AsString],
                    BsonArray idsArray => idsArray.Select(id => id.AsString).ToArray(),
                    _ => Array.Empty<string>()
                };
                var loaded = new List<Dictionary<Lookup, object?>>();
                foreach (var id in ids.Where(id => !visiting.Contains(id)))
                {
                    var target = await repository.GetById(id, ct);
                    if (target == null || target.WorkflowDefinition != reference.WorkflowDefinition?.Name)
                        continue;
                    var nestedPaths = paths
                        .Where(path => path.StartsWith(reference.Name + ".", StringComparison.Ordinal))
                        .Select(path => path[(reference.Name.Length + 1)..]);
                    var (_, targetContext) = await Build(target, nestedPaths);
                    var values = new Dictionary<Lookup, object?>(targetContext.Values);
                    foreach (var (key, value) in targetContext.LoadedReferenceValues)
                        values[key] = value;
                    loaded.Add(values);
                }

                projected.LoadedReferenceValues[reference.Name] =
                    reference.IsArray ? loaded.ToArray() : loaded.FirstOrDefault();
            }

            projected.ComputeProperties(definition);
            visiting.Remove(real.Id);
            return (snapshot, projected);
        }
    }

    private static BsonValue FilterStoredValue(BsonValue value, PropertyDefinition property, bool explicitPath)
    {
        if (explicitPath || property is not ObjectPropertyDefinition { WorkflowDefinition: { } definition })
            return value.DeepClone();
        if (value is BsonArray array)
            return new BsonArray(array.Select(item => FilterStoredValue(item, property, false)));
        if (value is not BsonDocument document)
            return value.DeepClone();
        return new BsonDocument(definition.Properties.Where(child => !child.IsComputed &&
                                                                     child.Visibility != PropertyVisibility.Hidden &&
                                                                     child.DataType != DataType.File &&
                                                                     document.Contains(child.Name))
            .Select(child => new BsonElement(child.Name, FilterStoredValue(document[child.Name], child, false))));
    }
}