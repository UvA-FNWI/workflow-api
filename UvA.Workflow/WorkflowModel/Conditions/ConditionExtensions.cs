namespace UvA.Workflow.WorkflowModel.Conditions;

public static class ConditionExtensions
{
    extension(Condition? condition)
    {
        public bool IsMet(ObjectContext context)
        {
            if (condition == null)
                return true;

            context.EnsureInputsAvailable(condition.Properties);
            var isMet = condition.LocalPart?.IsMet(context) ?? condition.NamedCondition?.IsMet(context)
                ?? throw new InvalidOperationException("Condition has no check.");
            return isMet != condition.Not;
        }

        /// <summary>
        /// Recursively extracts all event IDs from a condition tree
        /// </summary>
        public IEnumerable<string> GetAllEventIds()
            => condition?.Part switch
            {
                EventCondition e => [e.Id],
                CompositeCondition composite => composite.SelectMany(child => child.GetAllEventIds()),
                _ => []
            };

        /// <summary>
        /// Recursively extracts all Value parts from a condition tree
        /// </summary>
        public IEnumerable<Value> GetAllValues()
            => condition?.Part switch
            {
                Value value => [value],
                CompositeCondition composite => composite.SelectMany(child => child.GetAllValues()),
                _ => []
            };
    }
}