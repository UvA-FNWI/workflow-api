using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using UvA.Workflow.Api.Assessments;
using UvA.Workflow.Api.Assessments.Dtos;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Persistence;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Controllers;

public class HiddenAssessmentTests : ControllerTestsBase
{
    [Theory]
    [InlineData("all", false, false)]
    [InlineData("all", true, false)]
    [InlineData("all", true, true)]
    [InlineData("combined", false, false)]
    [InlineData("combined", true, false)]
    [InlineData("combined", true, true)]
    [InlineData("single", false, false)]
    [InlineData("single", true, false)]
    [InlineData("single", true, true)]
    [InlineData("page", false, false)]
    [InlineData("page", true, false)]
    [InlineData("page", true, true)]
    public async Task Results_HideUnpermittedPropertiesInAnswersAndNumericRows(
        string endpoint, bool firstReader, bool secondReader)
    {
        var (controller, instance, _) = Build(firstReader, secondReader);

        SourceResultDto[] results;
        if (endpoint == "page")
        {
            var response = await controller.GetSubmissionResults(instance.Id, "AssessmentSupervisor", "Content", _ct);
            results = [Assert.IsType<SourceResultDto>(Assert.IsType<OkObjectResult>(response.Result).Value)];
        }
        else
        {
            var response = endpoint == "all"
                ? await controller.GetAssessmentResults(instance.Id, _ct)
                : await controller.GetSubmissionResults(instance.Id,
                    endpoint == "combined" ? "AssessmentThesis" : "AssessmentSupervisor", _ct,
                    combine: endpoint == "combined");
            var dto = Assert.IsType<AssessmentDto>(Assert.IsType<OkObjectResult>(response.Result).Value);
            results = dto.Parts.SelectMany(p => p.SourceResults.Concat(p.Combined == null ? [] : [p.Combined]))
                .ToArray();
        }

        Assert.NotEmpty(results);
        foreach (var result in results)
        {
            var permitted = result.Id == "AssessmentSupervisor" ? firstReader
                : result.Id == "AssessmentSecondAssessor" ? secondReader
                : endpoint == "single" ? firstReader : firstReader && secondReader;
            var grade = Assert.Single(result.Answers, a => a.QuestionName == "ProblemStatement");
            Assert.Equal(permitted, grade.IsVisible);
            var complete = result.Id != "Combined" || endpoint != "single";
            var hasGrade = permitted && complete;
            Assert.Equal(hasGrade, grade.Value.HasValue);
            Assert.Null(Assert.Single(result.Answers, a => a.QuestionName == "TheoreticalFramework").Value);
            Assert.Equal(complete, Assert.Single(result.Answers, a => a.QuestionName == "Methods").Value.HasValue);
            var numericRows = result.PageResults.SelectMany(p => p.QuestionResults).ToArray();
            Assert.Equal(hasGrade, numericRows.Any(q => q.Name == "ProblemStatement"));
            Assert.DoesNotContain(numericRows, q => q.Name == "TheoreticalFramework");
        }

        Assert.Equal("8", instance.Properties["AssessmentSupervisor"]["ProblemStatement"].AsString);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("AssessmentSupervisor", false)]
    [InlineData("AssessmentCombined", true)]
    [InlineData("ReviewComments", true)]
    public async Task CombinedSourceAnswer_UsesTheReferencedFormsPropertyGrant(string? grantedForm, bool visible)
    {
        var (controller, instance, viewer) = Build(false, false);
        _modelService.WorkflowDefinitions["Assessment-PP"].Properties
            .Single(p => p.Name == "ProblemStatementComments").Visibility = PropertyVisibility.Hidden;
        instance.Properties["AssessmentCombined"] =
            new BsonDocument { ["ProblemStatementComments"] = "Private comment" };
        if (grantedForm == "ReviewComments")
            _modelService.GetForm(instance, "AssessmentCombined").Name = "ReviewComments";
        if (grantedForm != null)
            viewer.Actions.Add(new()
            {
                Type = RoleAction.ViewHidden, Form = grantedForm, PropertyDefinition = "ProblemStatementComments"
            });

        var response = await controller.GetSubmissionResults(instance.Id, "AssessmentThesis", _ct, combine: true);
        var dto = Assert.IsType<AssessmentDto>(Assert.IsType<OkObjectResult>(response.Result).Value);
        var answer = Assert.Single(Assert.Single(dto.Parts).Combined!.Answers,
            a => a.QuestionName == "ProblemStatementComments");

        Assert.Equal(visible, answer.IsVisible);
        Assert.Equal(visible ? "Private comment" : null, answer.Value?.GetString());
    }

    [Fact]
    public async Task CombinedSourceFile_UsesTheSourcesFileMetadata()
    {
        var (controller, instance, viewer) = Build(false, false);
        var definition = _modelService.WorkflowDefinitions["Assessment-PP"];
        var document = new FilePropertyDefinition
        {
            Name = "PrivateDocument", Type = "File", ParentType = definition,
            Visibility = PropertyVisibility.Hidden,
            Results = new() { Type = ResultType.Source, Source = "AssessmentCombined" }
        };
        definition.Properties.Add(document);
        var page = definition.Forms.Single(f => f.Name == "Assessment").Pages[0];
        page.PageElements = [.. page.PageElements, new() { Question = document.Name, QuestionDefinition = document }];
        instance.Properties["AssessmentSupervisor"][document.Name] =
            new ArtifactInfo("hidden-reader-file", "hidden.pdf").ToBsonDocument();
        instance.Properties["AssessmentCombined"] = new BsonDocument
        {
            [document.Name] = new ArtifactInfo("permitted-source-file", "source.pdf").ToBsonDocument()
        };
        viewer.Actions.Add(new()
        {
            Type = RoleAction.ViewHidden, Form = "AssessmentCombined", PropertyDefinition = document.Name
        });

        var response = await controller.GetSubmissionResults(instance.Id, "AssessmentThesis", _ct, combine: true);
        var dto = Assert.IsType<AssessmentDto>(Assert.IsType<OkObjectResult>(response.Result).Value);
        var answer = Assert.Single(Assert.Single(dto.Parts).Combined!.Answers,
            a => a.QuestionName == document.Name);

        Assert.True(answer.IsVisible);
        Assert.Equal("source.pdf", answer.Value?.GetString());
        var file = Assert.Single(answer.Files!);
        Assert.Equal("permitted-source-file", file.Id);
        Assert.Equal("source.pdf", file.Name);
        Assert.False(string.IsNullOrWhiteSpace(file.AccessToken));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task CombinedNumericRows_RequireContributorGrantsIndependentlyOfTheSourceAnswer(
        bool sourceGranted, bool readersGranted)
    {
        var (controller, instance, viewer) = Build(readersGranted, readersGranted);
        _modelService.WorkflowDefinitions["Assessment-PP"].Properties
                .Single(p => p.Name == "ProblemStatement").Results =
            new() { Type = ResultType.Source, Source = "AssessmentCombined" };
        instance.Properties["AssessmentCombined"] = new BsonDocument { ["ProblemStatement"] = "10" };
        if (sourceGranted)
            viewer.Actions.Add(new()
            {
                Type = RoleAction.ViewHidden, Form = "AssessmentCombined", PropertyDefinition = "ProblemStatement"
            });

        var response = await controller.GetSubmissionResults(instance.Id, "AssessmentThesis", _ct, combine: true);
        var dto = Assert.IsType<AssessmentDto>(Assert.IsType<OkObjectResult>(response.Result).Value);
        var combined = Assert.Single(dto.Parts).Combined!;
        var answer = Assert.Single(combined.Answers, a => a.QuestionName == "ProblemStatement");
        Assert.Equal(sourceGranted, answer.IsVisible);
        Assert.Equal(sourceGranted ? "10" : null, answer.Value?.GetString());
        var rows = combined.PageResults.SelectMany(p => p.QuestionResults)
            .Where(q => q.Name == "ProblemStatement").ToArray();
        if (readersGranted)
            Assert.Equal(7.33, Assert.Single(rows).Answer);
        else
            Assert.Empty(rows);
    }

    private (AssessmentsController, WorkflowInstance, Role) Build(bool firstReader, bool secondReader)
    {
        var definition = _modelService.WorkflowDefinitions["Project-PP"];
        var assessment = _modelService.WorkflowDefinitions["Assessment-PP"];
        assessment.Properties.Single(p => p.Name == "ProblemStatement").Visibility = PropertyVisibility.Hidden;
        assessment.Properties.Single(p => p.Name == "TheoreticalFramework").Visibility = PropertyVisibility.Hidden;
        var viewer = new Role
        {
            Name = "HiddenViewer",
            Actions = [new() { Type = RoleAction.View, Forms = ["AssessmentSupervisor", "AssessmentSecondAssessor"] }]
        };
        if (firstReader)
            viewer.Actions.Add(new()
            {
                Type = RoleAction.ViewHidden, Form = "AssessmentSupervisor", PropertyDefinition = "ProblemStatement"
            });
        if (secondReader)
            viewer.Actions.Add(new()
            {
                Type = RoleAction.ViewHidden, Form = "AssessmentSecondAssessor", PropertyDefinition = "ProblemStatement"
            });
        definition.Roles.Add(viewer);
        var instance = new WorkflowInstanceBuilder()
            .With("Project-PP", "AssessmentThesis")
            .WithProperties(
                ("AssessmentSupervisor", _ => new BsonDocument
                {
                    ["ProblemStatement"] = "8", ["TheoreticalFramework"] = "9", ["Methods"] = "7"
                }),
                ("AssessmentSecondAssessor", _ => new BsonDocument
                {
                    ["ProblemStatement"] = "6", ["TheoreticalFramework"] = "7", ["Methods"] = "5"
                }))
            .WithEvent("AssessmentSupervisor", DateTime.UtcNow)
            .WithEvent("AssessmentSecondAssessor", DateTime.UtcNow)
            .Build();
        MockCurrentUser("HiddenViewer");
        MockInstance(instance);
        MockEmptyEventLog(instance);
        MockEmptyRelatedInstanceLookups();
        var factory = new AssessmentDtoFactory(new ArtifactTokenService(UnitTestsHelpers.TestS3Config),
            _modelService, _assessmentService);
        var controller = new AssessmentsController(_workflowInstanceService, _userServiceMock.Object,
            _workflowInstanceRepoMock.Object, _assessmentService, _instanceService, factory, _rightsService);

        return (controller, instance, viewer);
    }
}