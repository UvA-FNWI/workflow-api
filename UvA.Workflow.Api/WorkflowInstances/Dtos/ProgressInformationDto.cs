using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public record ProgressInformationDto(
    BilingualString Text,
    StatusColor? Color)
{
    private static readonly ProgressInformationDto Completed =
        new(new BilingualString("Completed", "Afgerond"), StatusColor.Green);

    public static ProgressInformationDto Resolve(
        WorkflowDefinition workflowDefinition,
        string? internalName,
        ObjectContext context)
    {
        var currentStep = workflowDefinition.AllSteps.Find(step => step.Name == internalName);

        if (currentStep == null)
            return Completed;

        var progress = currentStep.Progress.FirstOrDefault(candidate =>
                           candidate.Condition?.IsMet(context) == true)
                       ?? currentStep.Progress.FirstOrDefault(candidate => candidate.Condition == null);
        var displayText = progress?.ProgressTextTemplate?.Apply(context)
                          ?? currentStep.DisplayTitle;
        return new ProgressInformationDto(displayText, progress?.Color);
    }
}