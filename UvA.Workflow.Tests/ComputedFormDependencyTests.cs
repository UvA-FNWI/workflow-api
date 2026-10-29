using MongoDB.Bson;
using Moq;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Submissions;
using UvA.Workflow.Tools;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests;

public class ComputedFormDependencyTests
{
    [Fact]
    public async Task QuestionStatus_ResolvesComputedConditionsValidationAndChoices()
    {
        var (model, service, instance, form) = Create();

        var status = await service.GetQuestionStatus(instance, form, true, CancellationToken.None);

        Assert.True(status["Detail"].IsVisible);
        Assert.NotNull(status["Validated"].ValidationError);
        Assert.Equal(["Included"], Assert.IsType<string[]>(status["Choice"].Choices));
    }

    [Fact]
    public async Task SubmissionDto_ResolvesComputedPageConditions()
    {
        var (model, service, instance, form) = Create();
        var factory = new SubmissionDtoFactory(null!, model, service);
        var state = FormSubmissionState.Resolve(instance, form, model.WorkflowDefinitions[instance.WorkflowDefinition]);

        var dto = await factory.CreateAsync(instance, form, state);

        Assert.True(dto.Form.Pages[1].IsActive);
    }

    [Fact]
    public async Task SubmitSubmission_ValidatesRequiredQuestionUsingComputedReferenceCondition()
    {
        var (model, instanceService, instance, form) = Create();
        var service = new SubmissionService(model, instanceService, null!, null!, null!);
        var state = FormSubmissionState.Resolve(instance, form, model.WorkflowDefinitions[instance.WorkflowDefinition]);

        var result = await service.SubmitSubmission(new SubmissionContext(instance, state, form, form.Name),
            null!, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.QuestionName == "Detail");
        Assert.Contains(result.Errors, error => error.QuestionName == "Validated");
        Assert.Empty(instance.Events);
    }

    private static (ModelService, InstanceService, WorkflowInstance, Form) Create()
    {
        var model = new ModelService(new ModelParser(new DictionaryProvider(new()
        {
            ["Assessment/Entity.yaml"] = """
                                         name: Assessment
                                         properties:
                                           - name: Target
                                             type: Target
                                           - name: RequiresDetail
                                             type: Check
                                             expression: Target.NeedsDetail
                                           - name: AcceptsValue
                                             type: Check
                                             expression: Target.AcceptsValue
                                           - name: IncludesChoice
                                             type: Check
                                             expression: Target.IncludesChoice
                                           - name: ShowsPage
                                             type: Check
                                             expression: Target.ShowsPage
                                           - name: Detail
                                             type: String!
                                             condition:
                                               value:
                                                 property: RequiresDetail
                                                 equal: true
                                           - name: Validated
                                             type: String
                                             validation:
                                               value:
                                                 property: AcceptsValue
                                                 equal: true
                                           - name: Choice
                                             type: Choice
                                             values:
                                               - name: Included
                                                 condition:
                                                   value:
                                                     property: IncludesChoice
                                                     equal: true
                                         """,
            ["Assessment/Forms/Edit.yaml"] = """
                                             name: Edit
                                             pages:
                                               - name: Content
                                                 elements:
                                                   - question: Detail
                                                   - question: Validated
                                                   - question: Choice
                                               - name: Extra
                                                 condition:
                                                   value:
                                                     property: ShowsPage
                                                     equal: true
                                             """,
            ["Target/Entity.yaml"] = """
                                     name: Target
                                     properties:
                                       - name: NeedsDetail
                                         type: Check
                                       - name: AcceptsValue
                                         type: Check
                                       - name: IncludesChoice
                                         type: Check
                                       - name: ShowsPage
                                         type: Check
                                     """
        })));
        var targetId = ObjectId.GenerateNewId().ToString();
        var repository = new Mock<IWorkflowInstanceRepository>();
        repository.Setup(r => r.GetAllById(new[] { targetId },
                It.Is<Dictionary<string, string>>(projection => projection.ContainsKey("NeedsDetail") &&
                    projection.ContainsKey("AcceptsValue") && projection.ContainsKey("IncludesChoice") &&
                    projection.ContainsKey("ShowsPage")),
                CancellationToken.None))
            .ReturnsAsync([new()
            {
                ["_id"] = targetId, ["NeedsDetail"] = true, ["AcceptsValue"] = false,
                ["IncludesChoice"] = true, ["ShowsPage"] = true
            }]);
        var service = new InstanceService(repository.Object, model, null!, null!, null!, null!);
        var answers = new BsonDocument { ["Target"] = targetId, ["Validated"] = "Answer" };
        var instance = new WorkflowInstance
        {
            Id = ObjectId.GenerateNewId().ToString(), Events = [],
            WorkflowDefinition = "Assessment",
            Properties = answers.ToDictionary(element => element.Name, element => element.Value)
        };
        return (model, service, instance, model.GetForm(instance, "Edit"));
    }
}
