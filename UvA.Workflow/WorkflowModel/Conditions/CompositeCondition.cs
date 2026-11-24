namespace UvA.Workflow.WorkflowModel.Conditions;

public abstract class CompositeCondition : List<Condition>, IConditionPart
{
    public Lookup[] Dependants => this.SelectMany(child => child.Dependants).ToArray();
    public IEnumerable<Lookup> Properties => this.SelectMany(child => child.Properties);

    public abstract bool IsMet(ObjectContext context);
}

public sealed class And : CompositeCondition
{
    public override bool IsMet(ObjectContext context) => this.All(child => child.IsMet(context));
}

public sealed class Or : CompositeCondition
{
    public override bool IsMet(ObjectContext context) => this.Any(child => child.IsMet(context));
}