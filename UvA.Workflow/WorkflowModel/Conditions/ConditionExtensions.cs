namespace UvA.Workflow.Entities.Domain.Conditions;

public static class ConditionExtensions
{
    public static bool IsMet(this Condition? condition, ObjectContext context)
        => condition?.Not == true
            ? !condition.Part.IsMet(context)
            : condition?.Part?.IsMet(context) != false;
}