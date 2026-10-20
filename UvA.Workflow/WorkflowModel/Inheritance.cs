using System.Collections;
using System.Linq.Expressions;

namespace UvA.Workflow.WorkflowModel;

public partial class ModelParser
{
    // Explicit empty lists clear; omitted lists inherit or merge.
    private static bool Cleared<T, TCollection>(T target, Expression<Func<T, TCollection>> property)
        where T : IDeclaredKeys
        where TCollection : ICollection =>
        target.Declared(property) && property.Compile()(target).Count == 0;

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

        if (!Cleared(target, d => d.Properties))
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
        if (!Cleared(target, d => d.Events))
            foreach (var ev in source.Events.Where(e => !target.Events.Contains(e.Name)))
                target.Events.Add(ev.Clone());

        foreach (var sourceScreen in source.Screens)
        {
            var targetScreen = target.Screens.GetOrDefault(sourceScreen.Name);
            if (targetScreen != null)
                ApplyInheritance(targetScreen, sourceScreen, target.Name);
        }

        foreach (var set in source.ValueSets)
        {
            if (target.ValueSets.Contains(set.Name))
                throw new Exception($"Definition '{target.Name}' redeclares value set '{set.Name}', " +
                                    $"which it inherits from '{source.Name}'.");
            target.ValueSets.Add(set);
        }

        if (!Cleared(target, d => d.GlobalActions))
            target.GlobalActions = MergeActions(target.GlobalActions, source.GlobalActions);

        var sourceGlobals = source.GlobalActions.ToHashSet();
        foreach (var sourceRole in source.Roles)
        {
            var targetRole = target.Roles.GetOrDefault(sourceRole.Name);
            if (targetRole == null)
            {
                targetRole = sourceRole.Clone();
                targetRole.Actions = [];
                target.Roles.Add(targetRole);
            }
            else
                ApplyInheritance(targetRole, sourceRole);

            var inherited = sourceRole.Actions.Where(a => a.WorkflowDefinition == null ||
                                                          (a.WorkflowDefinition == source.Name &&
                                                           !sourceGlobals.Contains(a) &&
                                                           !a.Steps.Any(declaredStepNames.Contains)));

            targetRole.Actions = MergeActions(targetRole.Actions, inherited.ToList());
            foreach (var action in targetRole.Actions.Where(a => a.WorkflowDefinition == source.Name))
                action.WorkflowDefinition = target.Name;
        }

        if (!target.Declared(d => d.StepNames)) target.StepNames = source.StepNames;
        if (!target.Declared(d => d.Title)) target.Title = source.Title;
        if (!target.Declared(d => d.TitlePlural)) target.TitlePlural = source.TitlePlural;
        if (!target.Declared(d => d.InstanceTitle)) target.InstanceTitle = source.InstanceTitle;
        if (!target.Declared(d => d.IsEmbedded)) target.IsEmbedded = source.IsEmbedded;
        if (!target.Declared(d => d.IsAlwaysVisible)) target.IsAlwaysVisible = source.IsAlwaysVisible;
        if (!target.Declared(d => d.AssessmentConfiguration))
            target.AssessmentConfiguration = source.AssessmentConfiguration;

        if (!Cleared(target, d => d.Fields))
        {
            var targetProperties = target.Fields.Select(f => f.Property).ToHashSet();
            target.Fields = source.Fields.Where(f => !targetProperties.Contains(f.Property))
                .Concat(target.Fields).ToArray();
        }

        if (!Cleared(target, d => d.InfoCards))
            target.InfoCards = MergeInfoCards(target.InfoCards, source.InfoCards);
    }

    private static InfoCard[] MergeInfoCards(InfoCard[] target, InfoCard[] source)
    {
        var result = source.ToList();
        foreach (var card in target)
        {
            var index = result.FindIndex(inherited => inherited.Name == card.Name);
            if (index < 0) result.Add(card);
            else result[index] = card;
        }

        return result.ToArray();
    }

    private static List<Action> MergeActions(List<Action> target, List<Action> source)
    {
        var targetNames = target.Select(a => a.Name).Where(n => n != null).ToHashSet();
        return source.Where(a => a.Name == null || !targetNames.Contains(a.Name))
            .Select(a => a.Clone()).Concat(target).ToList();
    }

    private void ApplyInheritance(Form target, Form source)
    {
        if (!target.Declared(s => s.Step)) target.Step = source.Step;

        if (!target.Declared(f => f.Confirmation))
            target.Confirmation = source.Confirmation;

        if (Cleared(target, f => f.Pages))
            return;
        foreach (var sourcePage in source.Pages.Where(p => !target.Pages.Contains(p.Name)))
            target.Pages.Insert(0, sourcePage.Clone());
    }

    private void ApplyInheritance(Step target, Step source)
    {
        if (!target.Declared(s => s.Title)) target.Title = source.Title;
        if (!target.Declared(s => s.Progress)) target.Progress = source.Progress;
        if (!target.Declared(s => s.Icon)) target.Icon = source.Icon;
        if (!target.Declared(s => s.Condition)) target.Condition = source.Condition;
        if (!target.Declared(s => s.Deadline)) target.Deadline = source.Deadline;
        if (!target.Declared(s => s.Ends)) target.Ends = source.Ends;
        if (!target.Declared(s => s.Mode)) target.Mode = source.Mode;
        if (!target.Declared(s => s.Before)) target.Before = source.Before;
        if (!target.Declared(s => s.HierarchyMode)) target.HierarchyMode = source.HierarchyMode;
        if (!target.Declared(s => s.ChildrenLayout)) target.ChildrenLayout = source.ChildrenLayout;
        if (!target.Declared(s => s.ResultsType)) target.ResultsType = source.ResultsType;
        if (!target.Declared(s => s.ChildNames)) target.ChildNames = source.ChildNames;

        // Reset-parent processing mutates event definitions, so inherited events cannot be shared.
        if (!Cleared(target, s => s.Events))
            foreach (var ev in source.Events.Where(e => !target.Events.Contains(e.Name)))
                target.Events.Add(ev.Clone());

        if (!Cleared(target, s => s.Actions))
            target.Actions = MergeActions(target.Actions, source.Actions);
    }

    private void ApplyInheritance(SendMessage target, SendMessage source)
    {
    }

    private void ApplyInheritance(Role target, Role source)
    {
        if (!target.Declared(r => r.Title)) target.Title = source.Title;
        if (!target.Declared(r => r.Order)) target.Order = source.Order;
        if (!target.Declared(r => r.InheritFrom)) target.InheritFrom = source.InheritFrom;
        if (!target.Declared(r => r.GenericEmail)) target.GenericEmail = source.GenericEmail;
    }

    private void ApplyInheritance(
        Screen target,
        Screen source,
        string definitionName)
    {
        if (!Cleared(target, s => s.Columns))
            target.Columns = MergeColumns(target.Columns, source.Columns);

        if (!target.Declared(s => s.BulkEdit))
            target.BulkEdit = source.BulkEdit?.Clone();
        else if (target.BulkEdit != null && source.BulkEdit != null)
            ApplyInheritance(target.BulkEdit, source.BulkEdit);

        if (source.Grouping != null)
        {
            target.Grouping = MergeGrouping(
                target.Grouping,
                source.Grouping,
                target.Name,
                definitionName);
        }
    }

    private void ApplyInheritance(BulkEditConfig target, BulkEditConfig source)
    {
        if (!target.Declared(b => b.Identifier))
            target.Identifier = source.Identifier.Clone();

        if (!target.Declared(b => b.ReadOnlyProperties) ||
            target.ReadOnlyProperties is { Length: > 0 })
        {
            target.ReadOnlyProperties = (target.ReadOnlyProperties ?? [])
                .Concat(source.ReadOnlyProperties ?? [])
                .DistinctBy(property => property.Property)
                .Select(property => property.Clone())
                .ToArray();
        }

        if (!Cleared(target, b => b.EditableProperties))
        {
            target.EditableProperties = source.EditableProperties
                .Concat(target.EditableProperties)
                .Distinct()
                .ToArray();
        }
    }

    private static Column[] MergeColumns(Column[] target, Column[] source)
    {
        var result = source.Select(column => column.Clone()).ToList();

        foreach (var column in target)
        {
            var index = column.Name == null
                ? -1
                : result.FindIndex(inherited => inherited.Name == column.Name);

            if (index < 0)
            {
                result.Add(column);
                continue;
            }

            var inherited = result[index];
            var replacement = column.Clone();
            replacement.Order ??= inherited.Order;
            result[index] = replacement;
        }

        return result.ToArray();
    }

    private static ScreenGrouping MergeGrouping(
        ScreenGrouping? target,
        ScreenGrouping source,
        string screenName,
        string definitionName)
    {
        var context = $"Screen '{screenName}' in '{definitionName}'";

        if (target == null)
            throw new Exception(
                $"{context} must declare step mappings for its inherited groups.");

        ValidateGroupNames(source.Groups, context);
        ValidateGroupNames(target.Groups, context);


        var targetGroupsByName = target.Groups.ToLookup(group => group.Name);
        var sourceNames = source.Groups.Select(group => group.Name).ToHashSet();
        var groups = new List<StepGroup>();

        foreach (var sourceGroup in source.Groups)
        {
            var targetGroup = targetGroupsByName[sourceGroup.Name].FirstOrDefault();

            groups.Add(new StepGroup
            {
                Name = sourceGroup.Name,
                Title = targetGroup?.Title ?? sourceGroup.Title,
                Steps = targetGroup == null ? [] : [.. targetGroup.Steps],
                Order = targetGroup?.Order ?? sourceGroup.Order,
                Enabled = targetGroup?.Enabled ?? sourceGroup.Enabled
            });
        }

        foreach (var targetGroup in target.Groups)
        {
            if (sourceNames.Contains(targetGroup.Name))
                continue;

            if (targetGroup.Title == null)
                throw new Exception(
                    $"{context} requires a title for new group '{targetGroup.Name}'.");

            groups.Add(new StepGroup
            {
                Name = targetGroup.Name,
                Title = targetGroup.Title,
                Steps = [.. targetGroup.Steps],
                Order = targetGroup.Order,
                Enabled = targetGroup.Enabled
            });
        }

        var duplicateStep = groups
            .SelectMany(group => group.Steps)
            .GroupBy(step => step)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateStep != null)
            throw new Exception(
                $"{context} maps step '{duplicateStep.Key ?? "null"}' more than once.");

        return new ScreenGrouping { Groups = groups.OrderBy(group => group.Order ?? 0).ToArray() };
    }

    private static void ValidateGroupNames(IEnumerable<StepGroup> groups, string context)
    {
        var duplicate = groups
            .GroupBy(group => group.Name)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate != null)
        {
            throw new Exception(
                $"{context} has duplicate group '{duplicate.Key}'.");
        }
    }
}