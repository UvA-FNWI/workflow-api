using UvA.Workflow.Assessments;
using UvA.Workflow.Messages;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;
using WorkflowAction = UvA.Workflow.WorkflowModel.Action;

namespace UvA.Workflow.Api.Submissions.Dtos;

public record FormDto(
    string Name,
    BilingualString Title,
    PageDto[] Pages,
    FormLayout Layout,
    string? Step,
    FormConfirmationDto? Confirmation = null)
{
    public static FormDto Create(Form form, ObjectContext context, string? instanceId = null,
        WorkflowAction[]? messageActions = null)
    {
        context = form.GetContext(context);
        var allPages = form.ActualForm.Pages.ToArray();
        var currentFormPages = form.GetCurrentPages().ToArray();
        var activePages = currentFormPages.Where(p => p.Condition.IsMet(context)).ToArray();
        var pageWeights =
            AssessmentHelpers.GetPageWeights(allPages.Where(page => page.Condition.IsMet(context)), context);
        var totalWeight = pageWeights.Values.Sum() ?? 0;

        var questions = activePages
            .SelectMany(p => p.Questions)
            .Distinct()
            .ToDictionary(property => property, property =>
            {
                var question = QuestionDto.Create(property, totalWeight);
                if (instanceId == null || messageActions == null)
                    return question;

                var path = form.PropertyName == null ? property.Name : $"{form.PropertyName}.{property.Name}";
                return WithMessages(question, form.WorkflowDefinition, instanceId, path, messageActions);
            });
        // Prefer the overriding form's own title; fall back to the target form's title, then its name.
        var title = form.Title ?? form.ActualForm.Title ?? form.ActualForm.Name;
        var originalForm = form;
        form = form.ActualForm;
        return new FormDto(
            form.Name,
            title,
            allPages.Select((p, i) =>
            {
                var isInCurrentForm = currentFormPages.Any(page => page.Name == p.Name);
                var isActive = activePages.Any(page => page.Name == p.Name);
                var pageElements = isActive
                    ? p.PageElements.Select(e => PageElementDto.Create(e, questions, context))
                        .OfType<PageElementDto>()
                    : [];
                decimal? percentage = isActive && totalWeight != 0 && pageWeights.GetValueOrDefault(p) is { } weight
                    ? Math.Round(weight / totalWeight * 100, 2, MidpointRounding.AwayFromZero)
                    : null;
                return PageDto.Create(i, p, pageElements, isInCurrentForm, isActive, percentage);
            }).ToArray(),
            form.Layout,
            originalForm.Step,
            FormConfirmationDto.Create(form.Confirmation, context)
        );
    }

    private static QuestionDto WithMessages(QuestionDto question, WorkflowDefinition definition, string instanceId,
        string path, WorkflowAction[] actions)
    {
        if (!definition.AllowsMessages(path))
            return question;

        question = question with
        {
            Messages = new QuestionMessagesDto(instanceId, path,
                MessageService.HasTargetPermission(definition, actions, RoleAction.ViewMessages, path),
                MessageService.HasTargetPermission(definition, actions, RoleAction.AddMessages, path))
        };
        return question is ObjectQuestionDto { SubProperties: { } children } embedded
            ? embedded with
            {
                SubProperties = children.Select(child =>
                    WithMessages(child, definition, instanceId, $"{path}.{child.Name}", actions)).ToArray()
            }
            : question;
    }
}

public record FormConfirmationDto(
    BilingualString Title,
    BilingualString Text)
{
    public static FormConfirmationDto? Create(FormConfirmation? confirmation, ObjectContext context)
        => confirmation == null
            ? null
            : new FormConfirmationDto(
                confirmation.TitleTemplate.Apply(context),
                confirmation.TextTemplate.Apply(context));
}