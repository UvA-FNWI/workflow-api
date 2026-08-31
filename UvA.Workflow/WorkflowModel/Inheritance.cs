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

        HashSet<string> flattedStepNames = [..target.StepNames, ..target.AllSteps.SelectMany(s => s.ChildNames)];
        foreach (var sourceStep in source.AllSteps)
        {
            if (target.AllSteps.TryGetValue(sourceStep.Name, out var targetStep))
                ApplyInheritance(targetStep, sourceStep);
            else if (target.StepNames.Count == 0 || flattedStepNames.Contains(sourceStep.Name)
                                                 || flattedStepNames.Contains(sourceStep.RootStep.Name))
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

        foreach (var screen in source.Screens.Where(s => !target.Screens.Contains(s.Name)))
            target.Screens.Add(screen);

        foreach (var set in source.ValueSets)
        {
            if (target.ValueSets.Contains(set.Name))
                throw new Exception($"Definition '{target.Name}' redeclares value set '{set.Name}', " +
                                    $"which it inherits from '{source.Name}'.");
            target.ValueSets.Add(set);
        }

        if (!Cleared(target, d => d.GlobalActions))
            target.GlobalActions = MergeActions(target.GlobalActions, source.GlobalActions);

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
        if (!target.Declared(s => s.HeaderStatus)) target.HeaderStatus = source.HeaderStatus;
        if (!target.Declared(s => s.Condition)) target.Condition = source.Condition;
        if (!target.Declared(s => s.Ends)) target.Ends = source.Ends;
        if (!target.Declared(s => s.HierarchyMode)) target.HierarchyMode = source.HierarchyMode;
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
}