using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Api.Submissions.Dtos;

public record PageDto(
    int Index,
    string Name,
    BilingualString Title,
    PageLayout Layout,
    PageElementDto[] Elements,
    bool HasResults,
    bool IsInCurrentForm,
    bool IsActive
)
{
    public static PageDto Create(int index, Page page, IEnumerable<PageElementDto> elements, ObjectContext context,
        bool isInCurrentForm, bool isActive)
        => new(
            index,
            page.Name,
            page.DisplayTitle,
            page.Layout,
            elements.ToArray(),
            page.HasResults,
            isInCurrentForm,
            isActive
        );
}

public enum PageElementKind
{
    Question,
    Callout,
    Text
}

public record PageElementDto(
    PageElementKind Kind,
    QuestionDto? Question,
    CalloutDto? Callout,
    BilingualString? Text)
{
    public static PageElementDto? Create(
        PageElement element,
        Dictionary<PropertyDefinition, QuestionDto> questions,
        ObjectContext context)
    {
        if (element.Question != null)
        {
            var definition = element.QuestionDefinition
                             ?? throw new InvalidOperationException(
                                 $"PageElement question '{element.Question}' was not resolved by ModelParser");
            return new PageElementDto(PageElementKind.Question, questions[definition], null, null);
        }

        if (element.Callout != null)
            return element.Callout.Condition.IsMet(context)
                ? new PageElementDto(PageElementKind.Callout, null, CalloutDto.Create(element.Callout, context), null)
                : null;

        return new PageElementDto(PageElementKind.Text, null, null, element.TextTemplate?.Apply(context));
    }
}

public record CalloutDto(CalloutVariant Variant, BilingualString? Title, BilingualString? Text)
{
    public static CalloutDto Create(Callout callout, ObjectContext context)
        => new(callout.Variant, callout.TitleTemplate?.Apply(context), callout.TextTemplate?.Apply(context));
}