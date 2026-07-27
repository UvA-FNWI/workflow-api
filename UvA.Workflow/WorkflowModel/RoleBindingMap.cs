namespace UvA.Workflow.WorkflowModel;

public enum RoleBindingSource
{
    Direct,
    Inherited
}

/// <summary>
/// Describes where an instance role is stored for a workflow definition.
/// </summary>
/// <param name="WorkflowDefinition">The workflow definition whose instances receive the role.</param>
/// <param name="Role">The role name.</param>
/// <param name="Source">Whether the role is stored directly or inherited through a reference.</param>
/// <param name="PropertyName">
/// The direct user property, or the reference property through which the role is inherited.
/// </param>
/// <param name="IsArray">Whether <paramref name="PropertyName"/> contains multiple values.</param>
/// <param name="ReferencedWorkflowDefinition">
/// For inherited roles, the workflow definition referenced by <paramref name="PropertyName"/>.
/// </param>
/// <param name="ReferencedRolePropertyName">
/// For inherited roles, the user property on the referenced workflow definition.
/// </param>
/// <param name="ReferencedRoleIsArray">
/// For inherited roles, whether the user property on the referenced workflow definition contains multiple users.
/// </param>
public sealed record RoleBinding(
    string WorkflowDefinition,
    string Role,
    RoleBindingSource Source,
    string PropertyName,
    bool IsArray,
    string? ReferencedWorkflowDefinition = null,
    string? ReferencedRolePropertyName = null,
    bool ReferencedRoleIsArray = false)
{
    /// <summary>
    /// MongoDB path of the property on an instance of <see cref="WorkflowDefinition"/>.
    /// </summary>
    public string PropertyPath => $"Properties.{PropertyName}";

    /// <summary>
    /// MongoDB path of the user ID for a direct role binding.
    /// The same dotted path works for both User and [User] properties.
    /// </summary>
    public string? UserIdPath =>
        Source == RoleBindingSource.Direct ? $"{PropertyPath}._id" : null;

    /// <summary>
    /// MongoDB path of the user ID on the referenced instance for an inherited role binding.
    /// </summary>
    public string? ReferencedUserIdPath =>
        Source == RoleBindingSource.Inherited ? $"Properties.{ReferencedRolePropertyName}._id" : null;
}

/// <summary>
/// Immutable, precompiled index of all direct and inherited instance-role bindings in a workflow model.
/// </summary>
public sealed class RoleBindingMap
{
    private readonly Dictionary<string, IReadOnlyList<RoleBinding>> bindingsByWorkflowDefinition;
    private readonly Dictionary<string, IReadOnlyList<RoleBinding>> bindingsByRole;

    private readonly Dictionary<(string WorkflowDefinition, string Role), IReadOnlyList<RoleBinding>>
        bindingsByWorkflowAndRole;

    public IReadOnlyList<RoleBinding> All { get; }

    private RoleBindingMap(IEnumerable<RoleBinding> bindings)
    {
        var all = bindings
            .Distinct()
            .OrderBy(binding => binding.WorkflowDefinition, StringComparer.OrdinalIgnoreCase)
            .ThenBy(binding => binding.Role, StringComparer.OrdinalIgnoreCase)
            .ThenBy(binding => binding.Source)
            .ThenBy(binding => binding.PropertyName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        All = Array.AsReadOnly(all);
        bindingsByWorkflowDefinition = all
            .GroupBy(binding => binding.WorkflowDefinition, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<RoleBinding>)Array.AsReadOnly(group.ToArray()),
                StringComparer.OrdinalIgnoreCase);
        bindingsByRole = all
            .GroupBy(binding => binding.Role, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<RoleBinding>)Array.AsReadOnly(group.ToArray()),
                StringComparer.OrdinalIgnoreCase);
        bindingsByWorkflowAndRole = all
            .GroupBy(binding => (binding.WorkflowDefinition, binding.Role), WorkflowRoleComparer.Instance)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<RoleBinding>)Array.AsReadOnly(group.ToArray()),
                WorkflowRoleComparer.Instance);
    }

    public IReadOnlyList<RoleBinding> GetBindings(string workflowDefinition)
        => bindingsByWorkflowDefinition.GetValueOrDefault(workflowDefinition) ?? [];

    public IReadOnlyList<RoleBinding> GetBindingsForRole(string role)
        => bindingsByRole.GetValueOrDefault(role) ?? [];

    public IReadOnlyList<RoleBinding> GetBindings(string workflowDefinition, string role)
        => bindingsByWorkflowAndRole.GetValueOrDefault((workflowDefinition, role)) ?? [];

    internal static RoleBindingMap Compile(IEnumerable<WorkflowDefinition> workflowDefinitions)
    {
        var bindings = new List<RoleBinding>();

        foreach (var definition in workflowDefinitions)
        {
            foreach (var property in definition.Properties)
            {
                if (property.DataType == DataType.User)
                {
                    bindings.Add(new RoleBinding(
                        definition.Name,
                        property.Name,
                        RoleBindingSource.Direct,
                        property.Name,
                        property.IsArray));
                }

                if (property.WorkflowDefinition == null)
                    continue;

                foreach (var inheritedRole in property.InheritedRoles)
                {
                    var referencedRoleProperty = property.WorkflowDefinition.Properties
                        .FirstOrDefault(candidate =>
                            candidate.DataType == DataType.User &&
                            string.Equals(candidate.Name, inheritedRole, StringComparison.OrdinalIgnoreCase));

                    if (referencedRoleProperty == null)
                        continue;

                    bindings.Add(new RoleBinding(
                        definition.Name,
                        inheritedRole,
                        RoleBindingSource.Inherited,
                        property.Name,
                        property.IsArray,
                        property.WorkflowDefinition.Name,
                        referencedRoleProperty.Name,
                        referencedRoleProperty.IsArray));
                }
            }
        }

        return new RoleBindingMap(bindings);
    }

    private sealed class WorkflowRoleComparer : IEqualityComparer<(string WorkflowDefinition, string Role)>
    {
        public static readonly WorkflowRoleComparer Instance = new();

        public bool Equals(
            (string WorkflowDefinition, string Role) x,
            (string WorkflowDefinition, string Role) y)
            => StringComparer.OrdinalIgnoreCase.Equals(x.WorkflowDefinition, y.WorkflowDefinition) &&
               StringComparer.OrdinalIgnoreCase.Equals(x.Role, y.Role);

        public int GetHashCode((string WorkflowDefinition, string Role) obj)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.WorkflowDefinition),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Role));
    }
}