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
        /// Evaluates only event and logical conditions against a historical set of event IDs.
        /// </summary>
        public bool IsMet(IEnumerable<string>? eventIds)
        {
            if (condition == null)
                return false;

            var eventIdSet = eventIds?.ToHashSet() ?? [];
            var result = condition.Part.IsMet(eventIdSet);
            return condition.Not ? !result : result;
        }

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