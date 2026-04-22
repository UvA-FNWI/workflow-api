using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Entities.Domain;

public partial class ModelParser
{
    private void ApplyInheritance(WorkflowDefinition target, WorkflowDefinition source)
    {
        target.Parent = source;

        if (!target.Forms.Any())
        {
            foreach (var sourceForm in source.Forms)
                target.Forms.Add(sourceForm.Clone());
        }
        else
        {
            foreach (var targetForm in target.Forms)
            {
                var parentFormName = target.InheritsFrom ?? targetForm.Name;
                if (source.Forms.TryGetValue(parentFormName, out var sourceForm))
                    ApplyInheritance(targetForm, sourceForm);
            }
        }

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
        foreach (var sourcePage in source.Pages.Where(p => !target.Pages.Contains(p.Name)))
            target.Pages.Insert(0, sourcePage.Clone()); // prepend parent pages before child-specific ones
    }

    private void ApplyInheritance(Step target, Step source)
    {
    }
}