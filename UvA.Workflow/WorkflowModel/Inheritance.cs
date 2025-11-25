namespace UvA.Workflow.Entities.Domain;

public partial class ModelParser
{
    private void ApplyInheritance(WorkflowDefinition target, WorkflowDefinition source)
    {
        target.Parent = source;

        foreach (var sourceForm in source.Forms)
        {
            if (target.Forms.TryGetValue(sourceForm.Key, out var targetForm))
                ApplyInheritance(targetForm, sourceForm.Value);
            else
                target.Forms.Add(sourceForm.Key, sourceForm.Value);
        }

        foreach (var property in source.Properties.Where(p => !target.Properties.ContainsKey(p.Key)))
            target.Properties.Add(property.Key, property.Value);

        foreach (var sourceStep in source.AllSteps)
        {
            if (target.AllSteps.TryGetValue(sourceStep.Key, out var targetStep))
                ApplyInheritance(targetStep, sourceStep.Value);
            else
                target.AllSteps.Add(sourceStep.Key, sourceStep.Value);
        }

        foreach (var ev in source.Events)
            target.Events.Add(ev.Key, ev.Value);

        foreach (var screen in source.Screens)
            target.Screens.Add(screen.Key, screen.Value);

        foreach (var role in Roles.Values)
        foreach (var action in role.Actions.Where(a => a.WorkflowDefinition == source.Name).ToArray())
        {
            var newAction = action.Clone();
            newAction.WorkflowDefinition = target.Name;
            role.Actions.Add(newAction);
        }

        if (target.StepNames.Count == 0)
            target.StepNames = source.StepNames;
        target.Title ??= source.Title;
        target.TitlePlural ??= source.TitlePlural;
        target.IsEmbedded = source.IsEmbedded;
        target.IsAlwaysVisible = source.IsAlwaysVisible;
        target.HeaderFields = source.HeaderFields.Concat(target.HeaderFields).ToArray();
    }

    private void ApplyInheritance(Form target, Form source)
    {
    }

    private void ApplyInheritance(Step target, Step source)
    {
    }
}