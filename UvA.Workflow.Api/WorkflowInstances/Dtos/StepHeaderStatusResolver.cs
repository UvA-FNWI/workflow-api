using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public class StepHeaderStatusResolver(ModelService modelService)
{
    public StepHeaderStatusDto? Resolve(Step step, WorkflowInstance instance)
    {
        if (step.HeaderStatus == null || step.HeaderStatus.Count == 0)
            return null;

        var context = ObjectContext.Create(instance, modelService);
        var matchedStatus = step.HeaderStatus
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

        if (matchedStatus == null)
            return null;

        return new StepHeaderStatusDto(
            matchedStatus.Configuration.Type,
            matchedStatus.Configuration.LabelTemplate.Apply(context)
        );
    }
}