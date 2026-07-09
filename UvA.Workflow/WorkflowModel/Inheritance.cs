namespace UvA.Workflow.WorkflowModel;

public partial class ModelParser
{
    private void ApplyInheritance(WorkflowDefinition target, WorkflowDefinition source)
    {
        target.Parent = source;

        foreach (var sourceForm in source.Forms)
        {
            // Target form matches on inheritsFrom property on form or on form name
            var targetForm = target.Forms.FirstOrDefault(tf => (tf.InheritsFrom ?? tf.Name) == sourceForm.Name);
            if (targetForm != null)
                ApplyInheritance(targetForm, sourceForm);
            else
                target.Forms.Add(sourceForm.Clone());
        }

        foreach (var property in source.Properties.Where(p =>
                     !target.Properties.Contains(p.Name) && !target.AllSteps.SelectMany(s => s.Properties)
                         .Select(s => s.Name).Contains(p.Name)))
            target.Properties.Add(property);

        foreach (var sourceStep in source.AllSteps)
        {
            if (target.AllSteps.TryGetValue(sourceStep.Name, out var targetStep))
                ApplyInheritance(targetStep, sourceStep);
            else
                target.AllSteps.Add(sourceStep);
        }

        foreach (var sourceMessage in source.Emails)
        {
            if (target.Emails.TryGetValue(sourceMessage.Name, out var targetMessage))
                ApplyInheritance(targetMessage, sourceMessage);
            else target.Emails.Add(sourceMessage);
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
        target.InstanceTitle ??= source.InstanceTitle;
        target.IsEmbedded = source.IsEmbedded;
        target.IsAlwaysVisible = source.IsAlwaysVisible;

        var targetProperties = target.Fields.Select(f => f.Property).ToHashSet();
        target.Fields = source.Fields.Where(f => !targetProperties.Contains(f.Property)).Concat(target.Fields)
            .ToArray();

        target.RelatedUsers = source.RelatedUsers
            .Where(sourceRelatedUser => target.RelatedUsers.All(targetRelatedUser =>
                targetRelatedUser.Property != sourceRelatedUser.Property))
            .Concat(target.RelatedUsers)
            .ToArray();
        target.RelatedUserGrouping = MergeRelatedUserGrouping(target.RelatedUserGrouping, source.RelatedUserGrouping);
    }

    private static RelatedUserGrouping? MergeRelatedUserGrouping(RelatedUserGrouping? target,
        RelatedUserGrouping? source)
    {
        if (source == null)
            return target;

        if (target == null)
            return new RelatedUserGrouping { Groups = source.Groups };

        return new RelatedUserGrouping
        {
            Groups = source.Groups
                .Where(sourceGroup => target.Groups.All(targetGroup => targetGroup.Name != sourceGroup.Name))
                .Concat(target.Groups)
                .ToArray()
        };
    }

    private void ApplyInheritance(Form target, Form source)
    {
        foreach (var sourcePage in source.Pages.Where(p => !target.Pages.Contains(p.Name)))
            target.Pages.Insert(0, sourcePage.Clone()); // prepend parent pages before child-specific ones
    }

    private void ApplyInheritance(Step target, Step source)
    {
    }

    private void ApplyInheritance(SendMessage target, SendMessage source)
    {
    }
}