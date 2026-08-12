using System.Linq.Expressions;
using System.Reflection;
using YamlDotNet.Serialization.NamingConventions;

namespace UvA.Workflow.WorkflowModel;

/// <summary>Top-level keys present in the source YAML.</summary>
public interface IDeclaredKeys
{
    [YamlIgnore] HashSet<string> DeclaredKeys { get; set; }
}

public static class DeclaredKeysExtensions
{
    public static bool Declared<T, TProperty>(this T source, Expression<Func<T, TProperty>> property)
        where T : IDeclaredKeys
    {
        var member = (property.Body as MemberExpression)?.Member
                     ?? throw new ArgumentException("A property selector is required", nameof(property));
        var yamlKey = member.GetCustomAttribute<YamlMemberAttribute>()?.Alias
                      ?? CamelCaseNamingConvention.Instance.Apply(member.Name);
        return source.DeclaredKeys.Contains(yamlKey);
    }
}