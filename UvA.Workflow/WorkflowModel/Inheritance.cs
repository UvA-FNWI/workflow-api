namespace UvA.Workflow.WorkflowModel;

public partial class ModelParser
{
    // Explicit empty lists clear; omitted lists inherit or merge.
    private static bool Cleared(IDeclaredKeys target, string key, int count) => target.Declared(key) && count == 0;

    private void ApplyInheritance(WorkflowDefinition target, WorkflowDefinition source)
    {
        target.Parent = source;

        var declaredStepNames = target.AllSteps.Select(s => s.Name).ToHashSet();

        foreach (var sourceForm in source.Forms)
        {
            var targetForm = target.Forms.FirstOrDefault(tf => (tf.InheritsFrom ?? tf.Name) == sourceForm.Name);
            if (targetForm != null)
                ApplyInheritance(targetForm, sourceForm);
            else
                target.Forms.Add(sourceForm.Clone());
        }

        if (!Cleared(target, "properties", target.Properties.Count))
        {
            var stepProperties = target.AllSteps.SelectMany(s => s.Properties).Select(s => s.Name).ToHashSet();
            foreach (var property in source.Properties.Where(p =>
                         !target.Properties.Contains(p.Name) && !stepProperties.Contains(p.Name)))
                target.Properties.Add(property);
        }

        foreach (var sourceStep in source.AllSteps)
        {
            if (target.AllSteps.TryGetValue(sourceStep.Name, out var targetStep))
                ApplyInheritance(targetStep, sourceStep);
            else
                target.AllSteps.Add(sourceStep.Clone());
        }

        foreach (var sourceMessage in source.Emails)
        {
            if (target.Emails.TryGetValue(sourceMessage.Name, out var targetMessage))
                ApplyInheritance(targetMessage, sourceMessage);
            else target.Emails.Add(sourceMessage);
        }

        // Reset-parent processing mutates event definitions, so inherited events cannot be shared.
        if (!Cleared(target, "events", target.Events.Count))
            foreach (var ev in source.Events.Where(e => !target.Events.Contains(e.Name)))
                target.Events.Add(ev.Clone());

        foreach (var screen in source.Screens.Where(s => !target.Screens.Contains(s.Name)))
            target.Screens.Add(screen);

        if (!target.Declared("globalActions"))
            target.GlobalActions = source.GlobalActions.Select(a => a.Clone()).ToList();

        // Global and overridden-step actions are registered from the merged definition.
        var sourceGlobals = source.GlobalActions.ToHashSet();
        foreach (var role in Roles)
        foreach (var action in role.Actions
                     .Where(a => a.WorkflowDefinition == source.Name
                                 && !sourceGlobals.Contains(a)
                                 && !a.Steps.Any(declaredStepNames.Contains))
                     .ToArray())
        {
            var newAction = action.Clone();
            newAction.WorkflowDefinition = target.Name;
            role.Actions.Add(newAction);
        }

        if (!target.Declared("steps")) target.StepNames = source.StepNames;
        if (!target.Declared("title")) target.Title = source.Title;
        if (!target.Declared("titlePlural")) target.TitlePlural = source.TitlePlural;
        if (!target.Declared("instanceTitle")) target.InstanceTitle = source.InstanceTitle;
        if (!target.Declared("isEmbedded")) target.IsEmbedded = source.IsEmbedded;
        if (!target.Declared("isAlwaysVisible")) target.IsAlwaysVisible = source.IsAlwaysVisible;
        if (!target.Declared("assessments")) target.AssessmentConfiguration = source.AssessmentConfiguration;

        if (!Cleared(target, "fields", target.Fields.Length))
        {
            var targetProperties = target.Fields.Select(f => f.Property).ToHashSet();
            target.Fields = source.Fields.Where(f => !targetProperties.Contains(f.Property))
                .Concat(target.Fields).ToArray();
        }

        if (!Cleared(target, "relatedUsers", target.RelatedUsers.Length))
            target.RelatedUsers = source.RelatedUsers
                .Where(sourceRelatedUser => target.RelatedUsers.All(targetRelatedUser =>
                    targetRelatedUser.Property != sourceRelatedUser.Property))
                .Concat(target.RelatedUsers)
                .ToArray();

        target.RelatedUserGrouping = MergeRelatedUserGrouping(target.RelatedUserGrouping, source.RelatedUserGrouping);
        if (!Cleared(target, "resources", target.Resources.Length))
            target.Resources = MergeResources(target.Resources, source.Resources);
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

    private static Resource[] MergeResources(Resource[] target, Resource[] source)
    {
        var result = target.ToList();

        foreach (var sourceResource in source.Reverse())
        {
            var targetResource = result.FirstOrDefault(r => r.Name == sourceResource.Name);
            if (targetResource == null)
            {
                result.Insert(0, sourceResource);
                continue;
            }

            if (sourceResource.Items == null) continue;

            if (targetResource.Items is { Length: 0 }) continue;

            if (targetResource.Items == null)
            {
                targetResource.Items = sourceResource.Items;
                continue;
            }

            var targetItemNames = targetResource.Items.Select(i => i.Name).ToHashSet();
            targetResource.Items = targetResource.Items
                .Concat(sourceResource.Items.Where(i => !targetItemNames.Contains(i.Name)))
                .ToArray();
        }

        return result.ToArray();
    }

    private void ApplyInheritance(Form target, Form source)
    {
        if (Cleared(target, "pages", target.Pages.Count))
            return;
        foreach (var sourcePage in source.Pages.Where(p => !target.Pages.Contains(p.Name)))
            target.Pages.Insert(0, sourcePage.Clone());
    }

    private void ApplyInheritance(Step target, Step source)
    {
        if (!target.Declared("title")) target.Title = source.Title;
        if (!target.Declared("progress")) target.Progress = source.Progress;
        if (!target.Declared("icon")) target.Icon = source.Icon;
        if (!target.Declared("headerStatus")) target.HeaderStatus = source.HeaderStatus;
        if (!target.Declared("condition")) target.Condition = source.Condition;
        if (!target.Declared("ends")) target.Ends = source.Ends;
        if (!target.Declared("hierarchyMode")) target.HierarchyMode = source.HierarchyMode;
        if (!target.Declared("resultsType")) target.ResultsType = source.ResultsType;
        if (!target.Declared("children")) target.ChildNames = source.ChildNames;

        // Reset-parent processing mutates event definitions, so inherited events cannot be shared.
        if (!Cleared(target, "events", target.Events.Count))
            foreach (var ev in source.Events.Where(e => !target.Events.Contains(e.Name)))
                target.Events.Add(ev.Clone());

        if (!target.Declared("actions"))
            target.Actions = source.Actions.Select(a => a.Clone()).ToList();
    }

    private void ApplyInheritance(SendMessage target, SendMessage source)
    {
    }
}