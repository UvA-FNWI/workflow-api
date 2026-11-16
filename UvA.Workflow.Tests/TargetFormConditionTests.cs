using MongoDB.Bson;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Tools;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Tests;

public class TargetFormConditionTests
{
    [Theory]
    [InlineData(false, new[] { "Extra" }, true)]
    [InlineData(true, new[] { "Extra" }, true)]
    [InlineData(false, new[] { "Other" }, false)]
    [InlineData(true, new[] { "Other" }, false)]
    [InlineData(false, new[] { "Extra", "Other" }, false)]
    [InlineData(true, new[] { "Extra", "Other" }, false)]
    [InlineData(false, new string[0], false)]
    [InlineData(true, new string[0], false)]
    public void MultiSelectEquality_UsesTypedFormAnswers(bool targetForm, string[] flags, bool visible)
    {
        var (model, instance, form) = Create(targetForm, true);
        var answers = targetForm ? instance.Properties["Assessment"].AsBsonDocument : null;
        if (answers != null)
            answers["Flags"] = new BsonArray(flags);
        else
            instance.Properties["Flags"] = new BsonArray(flags);
        var condition = new Condition { Value = new Value { Property = "Flags", Equal = "=Extra" } };
        form.ActualForm.WorkflowDefinition.Properties.Get("ExtraQuestion").Condition = condition;
        form.ActualForm.Pages[1].Condition = condition;

        var status = model.GetQuestionStatus(instance, form, true);
        var dto = FormDto.Create(form, model.CreateContext(instance));

        Assert.Equal(visible, status["ExtraQuestion"].IsVisible);
        Assert.Equal(visible, dto.Pages[1].IsActive);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void QuestionStatus_UsesTheFormsOwnAnswers(bool targetForm, bool showExtra)
    {
        var (model, instance, form) = Create(targetForm, showExtra);

        var status = model.GetQuestionStatus(instance, form, true);

        Assert.Equal(showExtra, status["ExtraQuestion"].IsVisible);
        Assert.Equal(showExtra, status["ExtraQuestion"].ValidationError == null);
        Assert.Equal(showExtra ? ["Extra"] : Array.Empty<string>(), status["Choice"].Choices);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FormDto_UsesTheFormsOwnAnswersForPagesAndElements(bool targetForm, bool showExtra)
    {
        var (model, instance, form) = Create(targetForm, showExtra);
        var context = model.CreateContext(instance);

        var dto = FormDto.Create(form, context);

        Assert.Equal(showExtra, dto.Pages[1].IsActive);
        Assert.Equal(showExtra ? 1 : 0, dto.Pages[0].Elements.Count(e => e.Callout != null));
        Assert.Equal(showExtra ? "True" : "False", dto.Pages[0].Elements.Last().Text!.En);
        Assert.Equal(targetForm ? !showExtra : showExtra, context.Get("ShowExtraQuestion"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingTargetAnswer_DoesNotFallBackToParentAnswer(bool missingObject)
    {
        var (model, instance, form) = Create(true, false);
        if (missingObject)
            instance.Properties.Remove("Assessment");
        else
            instance.Properties["Assessment"].AsBsonDocument.Remove("ShowExtraQuestion");

        Assert.False(model.GetQuestionStatus(instance, form, true)["ExtraQuestion"].IsVisible);
        Assert.False(FormDto.Create(form, model.CreateContext(instance)).Pages[1].IsActive);
    }

    private static (ModelService, WorkflowInstance, Form) Create(bool targetForm, bool showExtra)
    {
        var model = new ModelService(new ModelParser(new DictionaryProvider(new()
        {
            ["Project/Entity.yaml"] = """
                                      name: Project
                                      properties:
                                        - name: Assessment
                                          type: Assessment
                                        - name: ShowExtraQuestion
                                          type: Check
                                        - name: Flags
                                          type: '[Choice]'
                                          values:
                                            - name: Extra
                                            - name: Other
                                      """,
            ["Project/Forms/Review.yaml"] = """
                                            name: Review
                                            property: Assessment
                                            targetForm: Edit
                                            """,
            ["Assessment/Entity.yaml"] = """
                                         name: Assessment
                                         isEmbedded: true
                                         properties:
                                           - name: ShowExtraQuestion
                                             type: Check
                                           - name: Flags
                                             type: '[Choice]'
                                             values:
                                               - name: Extra
                                               - name: Other
                                           - name: ExtraQuestion
                                             type: String
                                             condition: &showExtra
                                               value:
                                                 property: ShowExtraQuestion
                                                 equal: true
                                             validation: *showExtra
                                           - name: Choice
                                             type: Choice
                                             values:
                                               - name: Extra
                                                 condition: *showExtra
                                         """,
            ["Assessment/Forms/Edit.yaml"] = """
                                             name: Edit
                                             pages:
                                               - name: Content
                                                 elements:
                                                   - question: ShowExtraQuestion
                                                   - question: ExtraQuestion
                                                   - question: Choice
                                                   - callout:
                                                       text: Extra
                                                       condition:
                                                         value:
                                                           property: ShowExtraQuestion
                                                           equal: true
                                                   - text: '{{ ShowExtraQuestion }}'
                                               - name: Extra
                                                 condition:
                                                   value:
                                                     property: ShowExtraQuestion
                                                     equal: true
                                                 elements:
                                                   - question: ExtraQuestion
                                             """
        })));
        var answers = new BsonDocument { ["ShowExtraQuestion"] = showExtra, ["ExtraQuestion"] = "Answer" };
        var instance = new WorkflowInstance
        {
            Events = [],
            WorkflowDefinition = targetForm ? "Project" : "Assessment",
            Properties = targetForm
                ? new()
                {
                    ["Assessment"] = answers, ["ShowExtraQuestion"] = !showExtra, ["Flags"] = new BsonArray { "Extra" }
                }
                : answers.ToDictionary(e => e.Name, e => e.Value)
        };
        return (model, instance, model.GetForm(instance, targetForm ? "Review" : "Edit"));
    }
}