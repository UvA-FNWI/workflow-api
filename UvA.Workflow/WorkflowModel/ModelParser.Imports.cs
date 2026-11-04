namespace UvA.Workflow.WorkflowModel;

public partial class ModelParser
{
    private static void ValidateImports(WorkflowDefinition definition)
    {
        foreach (var step in definition.AllSteps.Where(s => s.Import != null))
        {
            var import = step.Import!;
            var prefix = $"Import '{step.Name}' in '{definition.Name}'";
            if (!definition.Steps.Contains(step) || step.ParentStep != null || step.Mode != StepMode.Normal ||
                step.DeclaredKeys.Any(key => key is not ("name" or "import")) ||
                step.Title != null || step.Ends != null || step.Condition != null || step.Deadline != null ||
                step.Icon != null || step.ResultsType != StepResultsType.Normal || step.Before != null ||
                step.HierarchyMode != StepHierarchyMode.Sequential ||
                step.ChildrenLayout != StepChildrenLayout.Combined ||
                step.Children.Length != 0 || step.Actions.Count != 0 || step.Properties.Count != 0 ||
                step.Events.Count != 0 || step.Progress.Count != 0)
                throw new Exception($"{prefix} must be an ordinary top-level step with only name and import.");

            if (string.IsNullOrWhiteSpace(import.Reference) ||
                definition.Properties.GetOrDefault(import.Reference) is not ReferencePropertyDefinition
                {
                    IsArray: false, WorkflowDefinition: { } target
                })
                throw new Exception($"{prefix} requires a single-valued reference property.");

            if (import.Participants.Any(name =>
                    string.IsNullOrWhiteSpace(name) ||
                    target.Properties.GetOrDefault(name) is not UserPropertyDefinition))
                throw new Exception($"{prefix} participants must name User properties on '{target.Name}'.");

            var source = target.AllSteps.FirstOrDefault(s => s.Name == import.Step);
            if (source == null || !target.Steps.Contains(source) || source.ParentStep != null ||
                source.Children.Length != 0 || source.Mode != StepMode.Normal ||
                target.AllSteps.Any(s => s.Import != null))
                throw new Exception($"{prefix} must target an ordinary top-level leaf step without chained imports.");

            if (definition.Forms.Any(form => form.Step == step.Name) ||
                definition.AllActions.Any(action => action.Steps.Contains(step.Name)))
                throw new Exception($"{prefix} cannot own local forms or actions.");
        }
    }
}