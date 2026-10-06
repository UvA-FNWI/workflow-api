using UvA.Workflow.Deadlines;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.WorkflowModel;

public partial class ModelParser
{
    private static void PreProcessDeadlines(WorkflowDefinition definition)
    {
        var forms = definition.Forms.Where(form =>
            form.Pages.Any(page => page.PageElements.Any(element => element.Deadlines))).ToArray();
        if (forms.Length == 0)
            return;

        // Keep generated question dependencies local to this workflow when properties are inherited.
        definition.Properties = definition.Properties.Select(property => property.Clone()).ToList();
        var template = definition.Properties.Get("PostponementDate");
        var deadlines = definition.Steps.SelectMany(Subtree)
            .Select(step => DeadlineHistory.GetProperty(step, definition))
            .OfType<PropertyDefinition>().DistinctBy(property => property.Name).ToArray();
        var questions = new List<PageElement>();
        var effects = new List<Effect>();
        foreach (var property in deadlines)
        {
            var name = $"New{property.Name}";
            var initialized = new Condition { Value = new() { Property = property.Name, IsEmpty = false } };
            if (!definition.Properties.Contains(name))
                definition.Properties.Add(new PropertyDefinition
                {
                    Name = name,
                    Type = "Date",
                    Text = property.DisplayName,
                    Description = template.Description == null
                        ? null
                        : new(
                            template.Description.En.Replace("$deadline", property.Name),
                            template.Description.Nl.Replace("$deadline", property.Name)),
                    Layout = template.Layout,
                    Condition = new()
                    {
                        Logical = new()
                        {
                            Operator = LogicalOperator.And,
                            Children = template.Condition == null ? [initialized] : [template.Condition, initialized]
                        }
                    },
                    Validation = new()
                    {
                        Value = new() { Property = $"withDate({property.Name}, {name})", GreaterThan = property.Name }
                    }
                });
            questions.Add(new() { Question = name });
            effects.Add(new()
            {
                Condition = initialized,
                SetProperty = new()
                {
                    Property = property.Name,
                    Value =
                        $"if(equal(PostponementType, =All), addDays({property.Name}, coalesce(PostponementDays, 0)), coalesce(withDate({property.Name}, {name}), {property.Name}))"
                }
            });
        }

        var hasDeadline = new Condition
        {
            Logical = new()
                { Operator = LogicalOperator.Or, Children = effects.Select(effect => effect.Condition!).ToArray() }
        };
        foreach (var form in forms)
        {
            foreach (var page in form.Pages)
            {
                foreach (var element in page.PageElements)
                    ValidatePageElement(element, form.Name);
                page.PageElements = page.PageElements
                    .SelectMany(element => element.Deadlines ? questions : Enumerable.Repeat(element, 1)).ToArray();
            }

            // Apply deadline changes before the configured email and other submission effects.
            form.OnSubmit = [.. effects, .. form.OnSubmit];
            foreach (var action in definition.GlobalActions.Where(action =>
                         action.Type == RoleAction.Submit && action.Form == form.Name))
                action.Condition = action.Condition == null
                    ? hasDeadline
                    : new Condition
                    {
                        Logical = new() { Operator = LogicalOperator.And, Children = [action.Condition, hasDeadline] }
                    };
        }
    }

    private static void PreProcessDeadline(Step step, WorkflowDefinition definition)
    {
        if (step.Deadline?.MaxPostponementDays is not { } maximum)
            return;

        if (maximum < 0 || DeadlineHistory.GetProperty(step, definition) == null)
            throw new Exception(
                $"maxPostponementDays on step {step.Name} must be non-negative, and its deadline must name a Date or DateTime property that is not a list");
    }
}