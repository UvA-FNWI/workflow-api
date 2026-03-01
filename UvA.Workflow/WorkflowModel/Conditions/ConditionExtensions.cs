namespace UvA.Workflow.WorkflowModel.Conditions;

public static class ConditionExtensions
{
    extension(Condition? condition)
    {
        public bool IsMet(ObjectContext context)
            => condition?.Not == true
                ? !condition.Part.IsMet(context)
                : condition?.Part.IsMet(context) != false;

        /// <summary>
        /// Recursively extracts all event IDs from a condition tree
        /// </summary>
        public IEnumerable<string> GetAllEventIds()
        {
            if (condition == null)
                return [];

            var part = condition.Part;

            return part switch
            {
                EventCondition eventCond => [eventCond.Id],
                Logical logical => logical.Children.SelectMany(c => c.GetAllEventIds()),
                _ => []
            };
        }
    }
}