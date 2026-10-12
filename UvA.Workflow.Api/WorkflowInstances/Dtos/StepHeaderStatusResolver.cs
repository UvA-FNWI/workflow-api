using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public class StepHeaderStatusResolver(ModelService modelService)
{
    public StepHeaderStatusDto? Resolve(Step step, WorkflowInstance instance)
    {
        var headerEntries = step.Progress.Where(p => p.Header != null).ToList();

        var context = ObjectContext.Create(instance, modelService);
        var matchedStatus = headerEntries
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
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Specificity)
            .FirstOrDefault();

        var configuration = matchedStatus?.Configuration;

        // If no progress entry is matched, check parent steps for a default progress entry.
        for (var candidate = step; configuration is null && candidate is not null; candidate = candidate.ParentStep)
        {
            configuration = candidate.Progress.FirstOrDefault(progress =>
                progress.Header is not null && progress.EffectiveCondition is null);
        }

        if (configuration?.Header is not { } header)
            return null;

        return new StepHeaderStatusDto(
            header.Type,
            configuration.HeaderLabelTemplate?.Apply(context)
        );
    }
}