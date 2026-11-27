using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.WorkflowDefinitions.Dtos;

public record WorkflowDefinitionDto(
    string Name,
    BilingualString? Title,
    BilingualString TitlePlural,
    int? Index,
    bool IsAlwaysVisible,
    string? InheritsFrom,
    bool IsEmbedded,
    string[] Screens,
    bool CanCreateInstance,
    bool IsPropertyOnly,
    IReadOnlyList<string> Languages
)
{
    public static WorkflowDefinitionDto Create(WorkflowDefinition workflowDefinition,
        bool canCreateInstance = false)
    {
        var languages = workflowDefinition.EffectiveLanguages;
        return new WorkflowDefinitionDto(
            workflowDefinition.Name,
            BilingualString.Resolve(workflowDefinition.Title, languages),
            BilingualString.Resolve(workflowDefinition.TitlePlural, languages),
            workflowDefinition.Index,
            workflowDefinition.IsAlwaysVisible,
            workflowDefinition.InheritsFrom,
            workflowDefinition.IsEmbedded,
            workflowDefinition.Screens.Select(s => s.Name).ToArray(),
            canCreateInstance,
            workflowDefinition.Steps.Count == 0,
            languages
        );
    }
}