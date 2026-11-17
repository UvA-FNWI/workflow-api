using UvA.Workflow.Expressions;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

/// <summary>
/// Resolves which progress entry applies to a step, considering conditions and defaults defined on the step
/// itself as well as inherited from its ancestors.
/// Used to resolve both a step's header pill status and its progress text.
/// </summary>
public class StepProgressResolver(ModelService modelService)
{
    public static readonly ProgressInformationDto Completed =
        new(new BilingualString("Completed", "Afgerond"), StatusColor.Green);

    public static readonly BilingualTemplate CompletedText = BilingualTemplate.Create(new BilingualString(
        "Completed ({{ dateShort(LastEvent) }})", "Afgerond ({{ dateShort(LastEvent) }})"))!;

    public StepHeaderStatusDto? ResolveHeader(Step step, WorkflowInstance instance)
        => ResolveHeader(step, ObjectContext.Create(instance, modelService));

    public StepHeaderStatusDto? ResolveHeader(Step step, ObjectContext context)
    {
        var configuration = ResolveMatchedProgress(step, context, p => p.Header != null)
                            ?? ResolveDefault(step, p => p.Header != null);

        if (configuration?.Header is not { } header)
            return null;

        return new StepHeaderStatusDto(
            header.Type,
            configuration.HeaderLabelTemplate?.Apply(context)
        );
    }

    public static ProgressInformationDto ResolveProgress(
        WorkflowDefinition workflowDefinition,
        string? internalName,
        ObjectContext context, DateTime? completionDate = null, IReadOnlySet<string>? viewableSources = null)
    {
        var currentStep = workflowDefinition.AllSteps.Find(step => step.Name == internalName);

        if (currentStep?.Import != null)
        {
            if (context.ImportedSteps.GetValueOrDefault(currentStep.Name) is not { } owner)
                return new ProgressInformationDto(
                    new BilingualString("Shared work unavailable", "Gedeeld werk niet beschikbaar"),
                    StatusColor.Red);
            return viewableSources?.Contains(owner.Instance.Id) == true
                ? ResolveProgress(owner.Definition, owner.Step.Name, owner.Context)
                : new ProgressInformationDto(new BilingualString("Shared work", "Gedeeld werk"), null);
        }

        if (currentStep == null && workflowDefinition.HasImportedSteps)
        {
            var date = completionDate ?? context.WorkflowCompletionDate;
            return date == null
                ? Completed
                : new ProgressInformationDto(
                    CompletedText.Apply(new ObjectContext(new() { ["LastEvent"] = date.Value.ToLocalTime() })),
                    StatusColor.Green);
        }

        if (currentStep == null)
            return context.Get("LastEvent") is DateTime
                ? new ProgressInformationDto(CompletedText.Apply(context), StatusColor.Green)
                : Completed;

        var progress =
            ResolveMatchedProgress(currentStep, context, p => p.ProgressTextTemplate != null || p.Color != null)
            ?? ResolveDefault(currentStep, p => p.ProgressTextTemplate != null || p.Color != null);

        var displayText = progress?.ProgressTextTemplate?.Apply(context)
                          ?? currentStep.DisplayTitle;
        return new ProgressInformationDto(displayText, progress?.Color);
    }

    public static async Task<IReadOnlySet<string>> GetViewableSources(
        IEnumerable<ObjectContext> contexts, RightsService rightsService)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        var sources = contexts.SelectMany(context => context.ImportedSteps.Values)
            .OfType<ImportedStepContext>().Select(owner => owner.Instance).DistinctBy(instance => instance.Id);
        foreach (var source in sources)
            if (await rightsService.Can(source, RoleAction.View))
                allowed.Add(source.Id);
        return allowed;
    }

    public static ProgressInformation? ResolveMatchedProgress(Step step, ObjectContext context,
        Func<ProgressInformation, bool>? filter = null)
    {
        var childParentChain = new List<Step>();
        for (var current = step; current != null; current = current.ParentStep)
            childParentChain.Add(current);

        return childParentChain
            .SelectMany(candidate => candidate.Progress.Where(p =>
                p.EffectiveCondition != null && (filter == null || filter(p))))
            .Select(configuration =>
            {
                var condition = configuration.EffectiveCondition;
                var isMet = condition != null && condition.IsMet(context);

                // Recency key: the most recent active event referenced by the condition.
                // The context only exposes dates for events that are active (not suppressed).
                var eventIds = isMet ? condition!.GetAllEventIds().ToArray() : [];
                var dates = eventIds
                    .Select(id => context.Get(id + "Event") as DateTime?)
                    .Where(date => date != null)
                    .ToList();

                return new
                {
                    Configuration = configuration,
                    IsMet = isMet,
                    Date = dates.Count > 0 ? dates.Max() : null,
                    Specificity = eventIds.Length
                };
            })
            .Where(x => x.IsMet)
            // Most recent wins; on a tie, the more specific (multi-event) status takes precedence.
            // OrderBy is stable, so remaining ties keep declaration order (step's own entries before inherited).
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Specificity)
            .Select(x => x.Configuration)
            .FirstOrDefault();
    }

    /// <summary>
    /// Finds the first unconditioned ("default") progress entry across the step and its ancestors, matching
    /// <paramref name="filter"/> if given. The step's own default takes precedence over an inherited one.
    /// </summary>
    public static ProgressInformation? ResolveDefault(Step step, Func<ProgressInformation, bool>? filter = null)
    {
        for (var candidate = step; candidate != null; candidate = candidate.ParentStep)
        {
            var match = candidate.Progress.FirstOrDefault(p =>
                p.EffectiveCondition == null && (filter == null || filter(p)));
            if (match != null)
                return match;
        }

        return null;
    }
}