using UvA.Workflow.Expressions;

namespace UvA.Workflow.WorkflowInstances.ServiceCalls;

/// <summary>
/// Parses service call inputs once so literals stay valid and only real lookups are checked as missing.
/// </summary>
internal class ServiceCallInputs
{
    private readonly IReadOnlyList<ServiceCallInput> _inputs;

    public ServiceCallInputs(IReadOnlyDictionary<string, string> inputs)
    {
        _inputs = inputs
            .Select(input => ServiceCallInput.Parse(input.Key, input.Value))
            .ToArray();

        Lookups = [.. _inputs.SelectMany(input => input.References).Distinct()];
    }

    public IReadOnlyCollection<Lookup> Lookups { get; }

    // Only referenced lookups can be missing; literals are already resolved by the parser.
    public IReadOnlyCollection<string> GetMissingInputs(ObjectContext context)
        => [.. _inputs.SelectMany(input => input.GetMissingReferences(context))];

    public ObjectContext CreateRequestContext(ObjectContext context)
        => new(_inputs.ToDictionary(Lookup (input) => input.Name,
            object? (input) => input.Expression.Execute(context)));

    private record ServiceCallInput(string Name, Expression Expression, Lookup[] References)
    {
        public static ServiceCallInput Parse(string name, string source)
        {
            var expression = ExpressionParser.Parse(source)
                             ?? throw new InvalidOperationException(
                                 $"Failed to parse service call input '{name}'.");

            return new ServiceCallInput(name, expression, [.. expression.Properties.Distinct()]);
        }

        public IEnumerable<string> GetMissingReferences(ObjectContext context)
            => References
                .Where(reference => context.Get(reference) == null)
                .Select(reference => $"{Name}<-{reference}");
    }
}