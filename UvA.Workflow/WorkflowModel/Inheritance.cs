using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Entities.Domain;

public partial class ModelParser
{
    private void ApplyInheritance(WorkflowDefinition target, WorkflowDefinition source)
    {
        target.Parent = source;

        foreach (var sourceForm in source.Forms)
        {
            // If the name of the form matches the source form or the workflow definition (default form) it uses that form instead of the parent form 
            if (target.Forms.TryGetValue(sourceForm.Name, out var targetForm) ||
                target.Forms.TryGetValue(target.Name, out targetForm))
                ApplyInheritance(targetForm, sourceForm);
            else
                target.Forms.Add(sourceForm.Clone());
        }
        // todo: This also works, but might be too simple?
        // if (!target.Forms.Any())
        //     foreach (var sourceForm in source.Forms)
        //         target.Forms.Add(sourceForm.Clone());

        foreach (var property in source.Properties.Where(p => !target.Properties.Contains(p.Name)))
            target.Properties.Add(property);

        foreach (var sourceStep in source.AllSteps)
        {
            if (target.AllSteps.TryGetValue(sourceStep.Name, out var targetStep))
                ApplyInheritance(targetStep, sourceStep);
            else
                target.AllSteps.Add(sourceStep);
        }

        foreach (var ev in source.Events)
            target.Events.Add(ev);

        foreach (var screen in source.Screens)
            target.Screens.Add(screen);

        foreach (var role in Roles)
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
        target.Fields = source.Fields.Concat(target.Fields).ToArray();
    }

    private void ApplyInheritance(Form target, Form source)
    {
    }

    private void ApplyInheritance(Step target, Step source)
    {
    }
}