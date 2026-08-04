using UvA.Workflow.WorkflowModel;

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

        var displayText = currentStep.Progress?.ProgressTextTemplate?.Apply(context)
                          ?? currentStep.DisplayTitle;
        return new ProgressInformationDto(displayText, currentStep.Progress?.Color);
    }
}