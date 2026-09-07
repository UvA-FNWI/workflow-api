using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.WorkflowModel;

public class ModelService(ModelParser parser)
{
    public Dictionary<string, WorkflowDefinition> WorkflowDefinitions => parser.WorkflowDefinitions;

    public Dictionary<string, Role> Roles => parser.Roles.ToDictionary(r => r.Name, r => r);

    public List<Service> Services => parser.Services;

    public Form GetForm(WorkflowInstance instance, string formName)
    {
        var form = WorkflowDefinitions[instance.WorkflowDefinition].Forms.GetOrDefault(formName);
        return form ?? throw new ArgumentException($"Form {formName} not found");
    }

    public Form? TryGetForm(WorkflowInstance instance, string formName)
        => WorkflowDefinitions[instance.WorkflowDefinition].Forms.GetOrDefault(formName);

    public IEnumerable<Form> GetDerivedForms(WorkflowInstance instance, string formName)
        => WorkflowDefinitions[instance.WorkflowDefinition].Forms
            .Where(f => f.Name == formName || f.TargetFormName == formName);

    /// <summary>
    /// Resolves a <see cref="PropertyDefinition"/> by traversing a dotted property path on the given instance's
    /// workflow definition. Supports Reference and Object traversal (e.g. <c>Course.Title</c>), and User
    /// sub-fields (e.g. <c>Student.UserName</c>), which are returned as a synthesized <c>String</c> property.
    /// Returns <c>null</c> if any part of the path cannot be resolved.
    /// </summary>
    public PropertyDefinition? GetProperty(WorkflowInstance instance, params string?[] parts)
    {
        WorkflowDefinition? type = WorkflowDefinitions[instance.WorkflowDefinition];
        foreach (var part in parts.Take(parts.Length - 1).Where(p => p != null))
        {
            var prop = type!.Properties.GetOrDefault(part!);
            if (prop == null) return null;

            if (prop.DataType == DataType.User)
                return new PropertyDefinition
                {
                    Name = parts[^1]!,
                    Text = new BilingualString(parts[^1]!, parts[^1]!),
                    Type = "String"
                };

            type = prop.WorkflowDefinition;
            if (type == null) return null;
        }

        return parts.Length == 0 || parts[^1] == null ? null : type.Properties.GetOrDefault(parts[^1]!);
    }

    public ObjectContext CreateContext(WorkflowInstance instance)
        => ObjectContext.Create(instance, this);

    public ObjectContext CreateContext(string workflowDefinition, Dictionary<string, BsonValue> rawData)
        => ObjectContext.Create(WorkflowDefinitions[workflowDefinition], rawData);

    public Dictionary<string, QuestionStatus> GetQuestionStatus(WorkflowInstance instance, Form form,
        bool canViewHidden,
        IEnumerable<PropertyDefinition>? questions = null)
    {
        var context = CreateContext(instance);
        return (questions ?? (form.TargetForm ?? form).PropertyDefinitions)
            .ToDictionary(q => q.Name, q => new QuestionStatus(
                q.Condition.IsMet(context) && (q.Visibility != PropertyVisibility.Hidden || canViewHidden)
                                           && (q.Sources == null || q.Sources.Contains(form.PropertyName)),
                q.Validation.IsMet(context) || !instance.Properties.ContainsKey(q.Name)
                    ? null
                    : q.Validation!.Message ?? new BilingualString("Invalid value", "Ongeldige waarde"),
                q.Values?.Where(v => v.Condition.IsMet(context)).Select(v => v.Name).ToArray()
            ));
    }

    public string[] GetActiveSteps(WorkflowInstance instance)
    {
        var (step, context) = ResolveCurrentStep(instance);
        if (step == null)
            return [];
        context ??= CreateContext(instance);
        return step.Children
            .Where(s => s.Condition.IsMet(context) && !s.HasEnded(context))
            .Select(s => s.Name)
            .Append(step.Name)
            .Append(step.ParentStep?.Name)
            .Where(s => s != null)
            .ToArray()!;
    }

    /// <summary>
    /// Returns CurrentStep when it resolves; otherwise returns the first open step.
    /// </summary>
    public Step? GetCurrentStep(WorkflowInstance instance) => ResolveCurrentStep(instance).Step;

    private (Step? Step, ObjectContext? Context) ResolveCurrentStep(WorkflowInstance instance,
        ObjectContext? context = null)
    {
        var workflowDefinition = WorkflowDefinitions[instance.WorkflowDefinition];
        if (!string.IsNullOrEmpty(instance.CurrentStep) &&
            workflowDefinition.AllSteps.GetOrDefault(instance.CurrentStep) is { } currentStep)
            return (currentStep, context);

        context ??= CreateContext(instance);
        return (FindOpenStep(instance, context), context);
    }

    public Step? FindOpenStep(WorkflowInstance instance, ObjectContext? context = null)
    {
        var workflowDefinition = WorkflowDefinitions[instance.WorkflowDefinition];
        context ??= CreateContext(instance);
        return workflowDefinition.LeafSteps
            .FirstOrDefault(step => step.Condition.IsMet(context) && !step.HasEnded(context));
    }
}

public record QuestionStatus(bool IsVisible, BilingualString? ValidationError, string[]? Choices);