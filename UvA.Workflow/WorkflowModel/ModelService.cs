using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Entities.Domain;

public class ModelService(ModelParser parser)
{
    public Dictionary<string, WorkflowDefinition> WorkflowDefinitions => parser.WorkflowDefinitions;
    public Dictionary<string, Role> Roles => parser.Roles.ToDictionary(r => r.Name, r => r);

    public Form GetForm(WorkflowInstance instance, string formName)
    {
        var form = WorkflowDefinitions[instance.WorkflowDefinition].Forms.GetOrDefault(formName);
        return form ?? throw new ArgumentException($"Form {formName} not found");
    }

    public IEnumerable<Form> GetForms(WorkflowInstance instance, string formName)
        => WorkflowDefinitions[instance.WorkflowDefinition].Forms
            .Where(f => f.Name == formName || f.TargetFormName == formName);


    public PropertyDefinition? GetQuestion(WorkflowInstance instance, params string?[] parts)
    {
        var type = WorkflowDefinitions[instance.WorkflowDefinition];
        foreach (var part in parts.Take(parts.Length - 1).Where(p => p != null))
            type = type.Properties.Get(part!).WorkflowDefinition!;
        return type.Properties.GetOrDefault(parts[^1]!);
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
                q.Condition.IsMet(context) && (q.Visibility != PropertyVisibility.Hidden || canViewHidden),
                q.Validation.IsMet(context) || !instance.Properties.ContainsKey(q.Name)
                    ? null
                    : q.Validation!.Message ?? new BilingualString("Invalid value", "Ongeldige waarde"),
                q.Values?.Where(v => v.Value.Condition.IsMet(context)).Select(v => v.Key).ToArray()
            ));
    }

    public string[] GetActiveSteps(WorkflowInstance instance)
    {
        if (string.IsNullOrEmpty(instance.CurrentStep))
            return [];
        var step = WorkflowDefinitions[instance.WorkflowDefinition].AllSteps.Get(instance.CurrentStep);
        var context = CreateContext(instance);
        return step.Children
            .Where(s => s.Condition.IsMet(context) && !s.HasEnded(context))
            .Select(s => s.Name)
            .Append(instance.CurrentStep)
            .ToArray();
    }
}

public record QuestionStatus(bool IsVisible, BilingualString? ValidationError, string[]? Choices);