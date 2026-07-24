using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.WorkflowModel;

/// <summary>
/// Generates event-suppression rules for steps flagged with <c>resetParentStep</c>.
/// See docs/reset-parent-step.md for the full specification.
/// </summary>
public partial class ModelParser
{
    /// <summary>
    /// Expands every reset flag in a fully-linked definition into ordinary suppression edges.
    /// A reset reopens the first child while keeping later outcomes visible; completing the
    /// first child again then suppresses those stale outcomes and starts a new pass.
    /// Runs once per definition, after inheritance, hierarchy linking, named-condition resolution
    /// and step-event merging are complete. A definition with no reset flags is left untouched.
    /// </summary>
    private static void ExpandResetParentSteps(WorkflowDefinition def)
    {
        // Every step-level event flagged resetParentStep, with its declaring step.
        var declarations = def.AllSteps
            .SelectMany(s => s.Events
                .Where(e => e.ResetParentStep)
                .Select(e => (Event: e.Name, Step: s)))
            .ToArray();

        // The flag on a global (Entity.yaml) event has no declaring step, so no parent to reset.
        foreach (var ev in def.Events.Where(e => e.ResetParentStep))
            if (declarations.All(d => d.Event != ev.Name))
                throw Error(def, "the flag is only valid on a step-level event (it needs a parent-step context)",
                    resetEvent: ev.Name);

        if (declarations.Length == 0)
            return;

        // One (event -> declaring step -> target) per event id; a reset event must map to one target.
        var resets = declarations.GroupBy(d => d.Event).Select(g =>
        {
            if (g.Select(x => x.Step.ParentStep?.Name).Distinct().Count() > 1)
                throw Error(def, "the same reset event is declared on children of different parent steps",
                    resetEvent: g.Key);
            return (Event: g.Key, Declaring: g.First().Step, Target: g.First().Step.ParentStep);
        }).ToArray();

        var generated = new Dictionary<string, HashSet<string>>();

        foreach (var group in resets.GroupBy(r => r.Target?.Name))
        {
            var members = group.ToArray();
            var p = members[0].Target;
            var resetEvents = members.Select(m => m.Event).ToArray();
            if (p == null)
                throw Error(def, "declaring step has no parent step to reset",
                    resetEvent: resetEvents[0], declaringStep: members[0].Declaring.Name);

            // Walk the target subtree once and reuse it for every check below.
            var subtree = Subtree(p).ToArray();
            var subtreeNames = subtree.Select(s => s.Name).ToHashSet();
            var leaves = subtree.Where(s => !s.Children.Any()).ToArray();

            ValidateTarget(def, p, members.Select(m => m.Declaring).ToArray(), resetEvents[0], subtree, leaves);

            var restartEvents = FirstChildRestartEvents(def, p, p.Children[0], resetEvents[0]);
            var completionEvents = leaves
                .SelectMany(leaf => PositiveEvents(leaf.Ends!,
                    $"ends of leaf '{leaf.Name}' under target '{p.Name}' in workflow '{def.Name}'"))
                .Distinct()
                .ToArray();
            var nestedResets = resets
                .Where(r => r.Target != null && subtreeNames.Contains(r.Target.Name))
                .Select(r => r.Event)
                .ToArray();

            // A reset event cannot also restart the same pass.
            foreach (var reset in resetEvents)
                if (restartEvents.Contains(reset))
                    throw Error(def, "reset event is also a restart event for the same target",
                        resetEvent: reset, targetStep: p.Name);

            // A completion event must not be referenced by an ends condition outside the target.
            var outsideEndEvents = def.AllSteps
                .Where(s => !subtreeNames.Contains(s.Name) && s.Ends != null)
                .SelectMany(s => s.Ends.GetAllEventIds().Select(id => (Id: id, Step: s.Name)))
                .ToArray();
            foreach (var shared in completionEvents.Intersect(outsideEndEvents.Select(o => o.Id)))
                throw Error(def,
                    $"completion event is also referenced by the ends of step " +
                    $"'{outsideEndEvents.First(o => o.Id == shared).Step}' outside the reset target",
                    resetEvent: shared, targetStep: p.Name);

            // Any event that explicitly suppresses a restart event must itself be a declared reset.
            foreach (var e in def.Events.Where(e => e.Suppresses != null))
                if (e.Suppresses!.Any(restartEvents.Contains) && !resetEvents.Contains(e.Name))
                    throw Error(def,
                        "event explicitly suppresses a restart event of a reset target but is not a declared reset event",
                        resetEvent: e.Name, targetStep: p.Name);

            foreach (var reset in resetEvents)
                Accumulate(generated, reset, restartEvents.Concat(nestedResets.Where(n => n != reset)));
            foreach (var restart in restartEvents)
                Accumulate(generated, restart, completionEvents.Where(c => c != restart).Concat(nestedResets));
        }

        // Union with authored suppresses, drop self-edges, dedupe, order deterministically.
        foreach (var (name, targets) in generated)
        {
            var entry = def.Events.FirstOrDefault(e => e.Name == name);
            if (entry == null)
                def.Events.Add(entry = new EventDefinition { Name = name });
            entry.Suppresses = (entry.Suppresses ?? [])
                .Concat(targets)
                .Where(t => t != name)
                .Distinct()
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <summary>
    /// Verifies that the target hierarchy can be reset entirely through event suppression.
    /// This checks the step structure; completion-condition shapes are validated separately
    /// by <see cref="PositiveEvents"/>.
    /// </summary>
    private static void ValidateTarget(
        WorkflowDefinition def, Step p, Step[] declarings, string resetEvent, Step[] subtree, Step[] leaves)
    {
        var declaringNames = string.Join(", ", declarings.Select(d => d.Name));
        if (p.HierarchyMode != StepHierarchyMode.Sequential)
            throw Error(def, "target parent step is not sequential", resetEvent, declaringNames, p.Name);
        if (p.Children.Length < 2)
            throw Error(def, "target parent step has fewer than two children", resetEvent, declaringNames, p.Name);
        foreach (var d in declarings)
            if (p.Children[0].Name == d.Name)
                throw Error(def, "reset event is declared on the target's first child", resetEvent, d.Name, p.Name);
        if (p.Ends != null)
            throw Error(def, "target parent step has its own ends condition", resetEvent, declaringNames, p.Name);
        if (p.Children[0].Children.Any())
            throw Error(def, $"first child '{p.Children[0].Name}' of the target is not a leaf step",
                resetEvent, declaringNames, p.Name);

        foreach (var s in subtree.Where(s => s.Name != p.Name))
            if (s.Children.Any() && s.Ends != null)
                throw Error(def, $"step '{s.Name}' inside the target has both children and its own ends condition",
                    resetEvent, s.Name, p.Name);

        foreach (var leaf in leaves)
            if (leaf.Ends == null)
                throw Error(def, $"leaf step '{leaf.Name}' under the target has no ends condition",
                    resetEvent, leaf.Name, p.Name);
    }

    /// <summary>
    /// Gets the events that start a new pass by completing the target's first child.
    /// The first child is stricter than other leaves: its ends must be a single event
    /// or an Or of events, never a top-level And.
    /// </summary>
    private static string[] FirstChildRestartEvents(WorkflowDefinition def, Step p, Step firstChild, string resetEvent)
    {
        if (firstChild.Ends == null)
            throw Error(def, $"first child '{firstChild.Name}' has no ends condition",
                resetEvent, firstChild.Name, p.Name);

        var events = PositiveEvents(firstChild.Ends,
            $"ends of first child '{firstChild.Name}' of target '{p.Name}' in workflow '{def.Name}'");

        // PositiveEvents already rejected Not/non-event parts, so Part fully resolves the effective condition.
        if (firstChild.Ends.Part is Logical { Operator: LogicalOperator.And })
            throw Error(def, "first child ends condition must be a single event or an Or of events, not an And",
                resetEvent, firstChild.Name, p.Name);

        return events.ToArray();
    }

    /// <summary>
    /// Fail-closed: returns the positive event ids of a condition, or throws if the condition is not
    /// a pure positive-event / And / Or tree (the monotone fragment reset can drive by suppression).
    /// Recurses into a named condition's resolved body so its own Not is honoured, unlike <c>Part</c>.
    /// </summary>
    private static List<string> PositiveEvents(Condition c, string where)
    {
        if (c.Not)
            throw new Exception($"resetParentStep: negated conditions cannot be reset by suppression ({where})");
        if (c.Name != null)
            return PositiveEvents(c.NamedCondition!, $"{where} via named condition '{c.Name}'");
        return c.Part switch
        {
            EventCondition e => [e.Id],
            Logical { Operator: LogicalOperator.And or LogicalOperator.Or } l
                => l.Children.SelectMany(ch => PositiveEvents(ch, where)).ToList(),
            _ => throw new Exception(
                $"resetParentStep: only positive event, And, and Or conditions can be reset ({where})")
        };
    }

    /// <summary>
    /// Enumerates a step and all of its descendants so validation and edge generation use
    /// the same reset scope.
    /// </summary>
    private static IEnumerable<Step> Subtree(Step s)
        => new[] { s }.Concat(s.Children.SelectMany(Subtree));

    /// <summary>
    /// Collects generated suppression targets per source event before they are merged with
    /// authored rules. The hash set removes duplicate edges from overlapping reset scopes.
    /// </summary>
    private static void Accumulate(Dictionary<string, HashSet<string>> map, string key, IEnumerable<string> values)
    {
        if (!map.TryGetValue(key, out var set))
            map[key] = set = [];
        foreach (var v in values)
            set.Add(v);
    }

    /// <summary>
    /// Creates a consistent parse error containing the available workflow and step context.
    /// </summary>
    private static Exception Error(WorkflowDefinition def, string message,
        string? resetEvent = null, string? declaringStep = null, string? targetStep = null)
    {
        var parts = new List<string> { $"workflow '{def.Name}'" };
        if (resetEvent != null) parts.Add($"event '{resetEvent}'");
        if (declaringStep != null) parts.Add($"declaring step '{declaringStep}'");
        if (targetStep != null) parts.Add($"target step '{targetStep}'");
        return new Exception($"resetParentStep [{string.Join(", ", parts)}]: {message}");
    }
}