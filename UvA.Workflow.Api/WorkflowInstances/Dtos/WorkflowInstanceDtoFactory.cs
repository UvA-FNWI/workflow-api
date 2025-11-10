using UvA.Workflow.Api.EntityTypes.Dtos;
using UvA.Workflow.Api.Submissions.Dtos;

namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public class WorkflowInstanceDtoFactory(
    InstanceService instanceService,
    ModelService modelService,
    SubmissionDtoFactory submissionDtoFactory
        RightsService rightsService)
{
    /// <summary>
    /// Creates a WorkflowInstanceDto from a WorkflowInstance domain entity
    /// </summary>
    public async Task<WorkflowInstanceDto> Create(WorkflowInstance instance, CancellationToken ct)
    {
        var actions = await instanceService.GetAllowedActions(instance, ct);
        var submissions = await instanceService.GetAllowedSubmissions(instance, ct);
        var entityType = modelService.EntityTypes[instance.EntityType];
        var permissions = await rightsService.GetAllowedActions(instance, RoleAction.ViewAdminTools);

        return new WorkflowInstanceDto(
            instance.Id,
            entityType.InstanceTitleTemplate?.Apply(modelService.CreateContext(instance)),
            EntityTypeDto.Create(modelService.EntityTypes[instance.EntityType]),
            instance.CurrentStep,
            instance.ParentId,
            actions.Select(ActionDto.Create).ToArray(),
            [],
            entityType.Steps.Select(s => StepDto.Create(s, instance)).ToArray(),
            submissions
                .Select(s => submissionDtoFactory.Create(instance, s.Form, s.Event, s.QuestionStatus))
                .ToArray(),
            permissions.Select(a => a.Type).Distinct().ToArray()
        );
    }
}