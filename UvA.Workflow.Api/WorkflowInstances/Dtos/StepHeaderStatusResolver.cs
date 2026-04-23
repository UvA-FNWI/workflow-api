using UvA.Workflow.Events;

namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public class StepHeaderStatusResolver(ModelService modelService)
{
    public StepHeaderStatusDto? Resolve(Step step, WorkflowInstance instance)
    {
        if (step.HeaderStatus == null || step.HeaderStatus.Count == 0)
            return null;

        var workflowDefinition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var context = ObjectContext.Create(instance, modelService);
        var matchedStatus = step.HeaderStatus
            .Select(configuration => new
            {
                Configuration = configuration,
                Event = instance.Events.GetValueOrDefault(configuration.Event)
            })
            .Where(x => x.Event?.Date != null)
            .Where(x => EventSuppressionHelper.IsEventActive(x.Configuration.Event, instance, workflowDefinition))
            .OrderByDescending(x => x.Event!.Date)
            .FirstOrDefault();

        if (matchedStatus == null)
            return null;

        return new StepHeaderStatusDto(
            matchedStatus.Configuration.Type,
            matchedStatus.Configuration.LabelTemplate.Apply(context)
        );
    }
}