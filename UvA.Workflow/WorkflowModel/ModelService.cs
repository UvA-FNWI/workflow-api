namespace UvA.Workflow.Entities.Domain;

public class ModelService(ModelParser parser)
{
    public Dictionary<string, EntityType> EntityTypes => parser.EntityTypes;
    public Dictionary<string, Role> Roles => parser.Roles;

    public Form GetForm(WorkflowInstance instance, string formName)
        => EntityTypes[instance.EntityType].Forms[formName];

    public IEnumerable<Form> GetForms(WorkflowInstance instance, string formName)
        => EntityTypes[instance.EntityType].Forms.Values.Where(f => f.Name == formName || f.TargetFormName == formName);

    public Question GetQuestion(WorkflowInstance instance, params string?[] parts)
    {
        var type = EntityTypes[instance.EntityType];
        foreach (var part in parts.Take(parts.Length - 1).Where(p => p != null))
            type = type.Properties[part!].EntityType!;
        return type.Properties[parts[^1]!];
    }

    public ObjectContext CreateContext(WorkflowInstance instance)
        => ObjectContext.Create(instance, this);

    public Dictionary<string, QuestionStatus> GetQuestionStatus(WorkflowInstance instance, Form form,
        bool canViewHidden,
        IEnumerable<Question>? questions = null)
    {
        var context = CreateContext(instance);
        return (questions ?? (form.TargetForm ?? form).Questions)
            .ToDictionary(q => q.Name, q => new QuestionStatus(
                q.Condition.IsMet(context) && (q.Kind != QuestionKind.Hidden || canViewHidden),
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
        var step = EntityTypes[instance.EntityType].AllSteps[instance.CurrentStep];
        var context = CreateContext(instance);
        return step.Children
            .Where(s => s.Condition.IsMet(context) && !s.HasEnded(context))
            .Select(s => s.Name)
            .Append(instance.CurrentStep)
            .ToArray();
    }
}

public record QuestionStatus(bool IsVisible, BilingualString? ValidationError, string[]? Choices);