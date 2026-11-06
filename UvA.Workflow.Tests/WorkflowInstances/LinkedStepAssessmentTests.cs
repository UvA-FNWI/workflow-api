using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using Moq;
using UvA.Workflow.Api.Assessments;
using UvA.Workflow.Api.Assessments.Dtos;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Assessments;
using UvA.Workflow.Events;
using UvA.Workflow.Journaling;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Jobs;
using UvA.Workflow.Notifications;
using UvA.Workflow.Persistence;
using UvA.Workflow.Submissions;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Tools;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowInstances.ServiceCalls;

namespace UvA.Workflow.Tests.WorkflowInstances;

public class LinkedStepAssessmentTests
{
    [Fact]
    public async Task FinalGrade_CombinesSharedAndLocalAnswersAndRoundsOnlyOnce()
    {
        var setup = new LinkedStepAssessmentSetup();

        var context = await setup.Resolver.Resolve(setup.Core.Student, default);
        var result = setup.Calculate(context);

        Assert.Equal(7.46m, result.PartResults[0].Combined.WeightedAverage);
        Assert.Equal(8m, result.PartResults[1].Combined.WeightedAverage);
        Assert.Equal(7.73m, result.FinalGradeUnrounded);
        Assert.Equal(7.7f, result.FinalGradeRounded);
        Assert.Equal(7.7f, context.Get("Assessment.FinalGrade"));
        Assert.Equal("SharedAssessment", result.PartResults[0].Name);

        var other = setup.Core.CreateStudent();
        other.Properties["Grade"] = 9d;
        other.Events["AssessForm"] = setup.Core.Student.Events["AssessForm"];
        var otherContext = await setup.Resolver.Resolve(other, default);
        Assert.Equal(8.23m, setup.Calculate(otherContext).FinalGradeUnrounded);
        Assert.Equal(7.73m, setup.Calculate(context).FinalGradeUnrounded);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("draft")]
    [InlineData("undone")]
    [InlineData("suppressed")]
    [InlineData("ungraded")]
    [InlineData("local-draft")]
    public async Task FinalGrade_IsUnavailableUntilAllRequiredSourcesAreSubmitted(string state)
    {
        var setup = new LinkedStepAssessmentSetup();
        if (state == "missing") setup.Core.Sources.Clear();
        if (state is "draft" or "undone") setup.Core.Source.Events.Remove("AssessForm");
        if (state == "undone") setup.Core.Source.Properties["Grade"] = 9d;
        if (state == "local-draft") setup.Core.Student.Events.Remove("AssessForm");
        if (state == "ungraded") setup.Core.Source.Properties.Remove("Grade");
        if (state == "suppressed")
        {
            setup.Core.Model.WorkflowDefinitions["Shared"].Events.Add(
                new EventDefinition { Name = "Reassess", Suppresses = ["AssessForm"] });
            setup.Core.Complete("Reassess", 22);
        }

        var context = await setup.Resolver.Resolve(setup.Core.Student, default);

        Assert.Null(setup.Calculate(context).FinalGradeRounded);
        Assert.Null(context.Get("Assessment.FinalGrade"));
        var inputs = new ServiceCallInputs(new Dictionary<string, string> { ["Grade"] = "Assessment.FinalGrade" });
        Assert.Equal(["Grade<-Assessment.FinalGrade"], inputs.GetMissingInputs(context));

        var response = await setup.Controller.GetAssessmentResults(setup.Core.Student.Id, default);
        Assert.Null(Assert.IsType<AssessmentDto>(Assert.IsType<OkObjectResult>(response.Result).Value).FinalGrade);
    }

    [Fact]
    public async Task FinalGrade_UsesAllSourceAssessorsAndTheLocalPartWeights()
    {
        var files = LinkedStepAssessmentSetup.Files();
        files["Shared/Entity.yaml"] = files["Shared/Entity.yaml"].Replace("assessments:", """
              - name: OtherGrade
                type: Double
                calculation:
                  type: Average
                  weight: 1
            assessments:
            """).Replace("sources: [{name: AssessForm}]",
            "sources: [{name: AssessForm, weight: 2}, {name: AssessSecond}]");
        files["Shared/Forms/AssessSecond.yaml"] = files["Shared/Forms/AssessForm.yaml"]
            .Replace("name: AssessForm", "name: AssessSecond").Replace("question: Grade", "question: OtherGrade");
        files["Shared/Actions.yaml"] =
            files["Shared/Actions.yaml"].Replace("[AssessForm]", "[AssessForm, AssessSecond]");
        var setup = new LinkedStepAssessmentSetup(files);
        var config = setup.Core.Model.WorkflowDefinitions["Individual"].AssessmentConfiguration!;
        config.Parts[0].Weight = 40;
        config.Parts[1].Weight = 60;
        setup.Core.Source.Properties["OtherGrade"] = 5.96d;
        Assert.Null(setup.Calculate(await setup.Resolver.Resolve(setup.Core.Student, default)).FinalGradeRounded);

        setup.Core.Complete("AssessSecond", 22);
        var result = setup.Calculate(await setup.Resolver.Resolve(setup.Core.Student, default));

        Assert.Equal(6.96m, result.PartResults[0].Combined.WeightedAverage);
        Assert.Equal(7.584m, result.FinalGradeUnrounded);
        Assert.Equal(7.6f, result.FinalGradeRounded);
    }

    [Fact]
    public async Task FinalGrade_UsesTheSameCourseRoundingInExpressionsAndResults()
    {
        var files = LinkedStepAssessmentSetup.Files();
        files["Course/Entity.yaml"] =
            "name: Course\nproperties:\n  - name: GradingBasis\n    type: String\n  - name: GradeGap\n    type: Check";
        files["Individual/Entity.yaml"] = files["Individual/Entity.yaml"]
            .Replace("assessments:", "  - name: Course\n    type: Course!\nassessments:")
            .Replace("  gradingBasis: Decimal\n", "");
        var setup = new LinkedStepAssessmentSetup(files);
        var course = new WorkflowInstance
        {
            Id = ObjectId.GenerateNewId().ToString(), WorkflowDefinition = "Course",
            Properties = new() { ["GradingBasis"] = "Half", ["GradeGap"] = false }
        };
        setup.Core.Student.Properties["Course"] = course.Id;
        setup.Core.Repository.Setup(repo => repo.GetById(course.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(course);
        setup.Core.Repository.Setup(repo => repo.GetAllById(It.IsAny<string[]>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new() { ["_id"] = ObjectId.Parse(course.Id), ["GradingBasis"] = "Half", ["GradeGap"] = false }
            ]);

        var context = await setup.Resolver.Resolve(setup.Core.Student, default);
        var response = await setup.Controller.GetAssessmentResults(setup.Core.Student.Id, default);
        var dto = Assert.IsType<AssessmentDto>(Assert.IsType<OkObjectResult>(response.Result).Value);

        Assert.Equal(7.5f, context.Get("Assessment.FinalGrade"));
        Assert.Equal(7.5f, dto.FinalGrade!.Rounded);
    }

    [Fact]
    public async Task Job_ResolvesTheFinalGradeWithoutAnAssessmentLookupOnAStep()
    {
        var files = LinkedStepAssessmentSetup.Files();
        files["Individual/Steps/After.yaml"] = "name: After\nends:\n  event: Finished";
        var setup = new LinkedStepAssessmentSetup(files);
        var effects = new EffectService(setup.Instances, Mock.Of<IInstanceEventService>(), setup.Core.Model,
            Mock.Of<IMailService>(), Mock.Of<IExternalUserService>(), Mock.Of<IArtifactService>(),
            Mock.Of<IMailLogRepository>(), new ConfigurationBuilder().Build(), Mock.Of<IInstanceEventRepository>(),
            NullLogger<EffectService>.Instance);
        var jobs = new JobService(effects, setup.Core.Model, Mock.Of<IJobRepository>(), setup.Core.Repository.Object,
            Mock.Of<IUserRepository>(), NullLogger<JobService>.Instance, setup.Instances,
            Options.Create(new WorkerOptions()));

        var result = await jobs.CreateAndRunJob(setup.Core.Student, JobSource.Action, "Finalize",
            [new Effect { SetProperty = new SetProperty { Property = "FinalGrade", Value = "Assessment.FinalGrade" } }],
            new User(), null, default);

        Assert.Null(result.Error);
        Assert.Equal(7.7f, (float)setup.Core.Student.GetProperty("FinalGrade")!.AsDouble);
    }

    [Fact]
    public async Task Submission_ResolvesTheFinalGradeForValidation()
    {
        var files = LinkedStepAssessmentSetup.Files();
        files["Individual/Steps/After.yaml"] = "name: After\nends:\n  event: Finished";
        files["Individual/Entity.yaml"] = files["Individual/Entity.yaml"].Replace("assessments:", """
              - name: Approval
                type: Check
                validation:
                  not: true
                  value:
                    property: Assessment.FinalGrade
                    lessThan: 8
            assessments:
            """);
        files["Individual/Forms/Finalize.yaml"] =
            "name: Finalize\nstep: After\npages:\n  - name: Approval\n    elements:\n      - question: Approval";
        var setup = new LinkedStepAssessmentSetup(files);
        setup.Core.Student.Properties["Approval"] = true;
        var form = setup.Core.Model.GetForm(setup.Core.Student, "Finalize");
        var context = new SubmissionContext(setup.Core.Student,
            FormSubmissionState.Resolve(setup.Core.Student, form, setup.Core.Model.WorkflowDefinitions["Individual"]),
            form, form.Name);
        var submissions = new SubmissionService(setup.Core.Model, setup.Instances,
            Mock.Of<IInstanceJournalService>(), null!, null!);

        var result = await submissions.SubmitSubmission(context, new User(), default);

        Assert.False(result.Success);
        Assert.Equal("Approval", Assert.Single(result.Errors).QuestionName);
    }

    [Fact]
    public async Task Results_ReadSharedAnswersFromTheirOwnerEvenWhenFormNamesMatch()
    {
        var setup = new LinkedStepAssessmentSetup();

        var response = await setup.Controller.GetAssessmentResults(setup.Core.Student.Id, default);
        var dto = Assert.IsType<AssessmentDto>(Assert.IsType<OkObjectResult>(response.Result).Value);

        Assert.Equal(7.7f, dto.FinalGrade!.Rounded);
        Assert.Equal(7.46m, Assert.Single(dto.Parts[0].SourceResults).WeightedAverage);
        Assert.Equal(7.46d, Assert.Single(dto.Parts[0].SourceResults[0].Answers).Value!.Value.GetDouble());
        Assert.Equal(8m, Assert.Single(dto.Parts[1].SourceResults).WeightedAverage);
    }

    [Fact]
    public async Task Results_RequirePermissionToViewTheSharedResults()
    {
        var setup = new LinkedStepAssessmentSetup(sourceAccess: false);

        await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
            setup.Controller.GetAssessmentResults(setup.Core.Student.Id, default));
    }

    [Fact]
    public async Task Results_ViewResultsDoesNotGrantAccessToSharedAssessorAnswers()
    {
        var files = LinkedStepAssessmentSetup.Files();
        files["Shared/Actions.yaml"] = files["Shared/Actions.yaml"].Replace("type: View", "type: ViewResults");
        var setup = new LinkedStepAssessmentSetup(files);

        var response = await setup.Controller.GetAssessmentResults(setup.Core.Student.Id, default);
        var dto = Assert.IsType<AssessmentDto>(Assert.IsType<OkObjectResult>(response.Result).Value);

        Assert.Empty(dto.Parts[0].SourceResults);
        Assert.Equal(7.46m, dto.Parts[0].Combined!.WeightedAverage);
        Assert.Single(dto.Parts[1].SourceResults);
        Assert.Equal(7.7f, dto.FinalGrade!.Rounded);
    }

    [Theory]
    [InlineData("missing-part")]
    [InlineData("local-sources")]
    public void Configuration_RejectsAmbiguousOrMissingImportedAssessmentParts(string failure)
    {
        var files = LinkedStepAssessmentSetup.Files();
        if (failure == "missing-part")
            files["Shared/Entity.yaml"] = files["Shared/Entity.yaml"].Replace("name: Assess\n", "name: Other\n");
        else
            files["Individual/Entity.yaml"] = files["Individual/Entity.yaml"].Replace(
                "name: SharedAssessment\n      weight: 1",
                "name: SharedAssessment\n      sources: [{name: AssessForm}]\n      weight: 1");

        Assert.Contains("assessment", Assert.ThrowsAny<Exception>(() =>
            new ModelParser(new DictionaryProvider(files))).Message, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class LinkedStepAssessmentSetup
{
    public LinkedStepTestSetup Core { get; }
    public WorkflowStateResolver Resolver { get; }
    public AssessmentService Assessments { get; }
    public AssessmentsController Controller { get; }
    public InstanceService Instances { get; }

    public LinkedStepAssessmentSetup(Dictionary<string, string>? files = null, bool sourceAccess = true)
    {
        files ??= Files();
        if (!sourceAccess) files.Remove("Shared/Actions.yaml");
        Core = new LinkedStepTestSetup(files);
        var users = new Mock<IUserService>();
        users.Setup(service => service.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = ObjectId.GenerateNewId().ToString(), UserName = "assessor" });
        users.Setup(service => service.GetRolesOfCurrentUser()).ReturnsAsync(Array.Empty<string>());
        Core.Repository.Setup(repo => repo.GetById(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) =>
                Core.Sources.Append(Core.Student).FirstOrDefault(instance => instance.Id == id));
        var workflows = new WorkflowInstanceService(Core.Model, Core.Repository.Object,
            Mock.Of<IInstanceJournalService>(), Mock.Of<IInstanceEventRepository>(), users.Object,
            Mock.Of<IUserRepository>());
        Assessments = new AssessmentService(Core.Model, workflows, Core.Repository.Object);
        var enricher = new WorkflowContextEnricher(Core.Repository.Object, Core.Model, Assessments);
        Resolver = new WorkflowStateResolver(Core.Repository.Object, Core.Model, enricher);
        var rights = new RightsService(Core.Model, users.Object, Core.Repository.Object, Resolver);
        Instances = new InstanceService(Core.Repository.Object, Core.Model, users.Object, rights,
            null!, enricher, Resolver);
        Controller = new AssessmentsController(workflows, users.Object, Core.Repository.Object, Assessments,
            Instances, new AssessmentDtoFactory(new ArtifactTokenService(UnitTestsHelpers.TestS3Config),
                Core.Model, Assessments), rights);
        Core.Source.Properties["Grade"] = 7.46d;
        Core.Student.Properties["Grade"] = 8d;
        Core.Complete("AssessForm", 21);
        Core.Student.Events["AssessForm"] = Core.Source.Events["AssessForm"];
    }

    public AssessmentResult Calculate(ObjectContext context)
    {
        var definition = Core.Model.WorkflowDefinitions["Individual"];
        return Assessments.GetAssessmentResult(definition, context, definition.AssessmentConfiguration);
    }

    internal static Dictionary<string, string> Files()
    {
        var files = LinkedStepConfigurationTests.Files();
        foreach (var name in new[] { "Shared", "Individual" })
        {
            files[$"{name}/Entity.yaml"] += """

                                              - name: Grade
                                                type: Double
                                                calculation:
                                                  type: Average
                                                  weight: 1
                                            assessments:
                                              gradingBasis: Decimal
                                              parts:
                                            """;
            files[$"{name}/Forms/AssessForm.yaml"] = $"""
                                                      name: AssessForm
                                                      step: {(name == "Shared" ? "Assess" : "After")}
                                                      pages:
                                                        - name: Grade
                                                          elements:
                                                            - question: Grade
                                                      """;
            files[$"{name}/Actions.yaml"] =
                "globalActions:\n  - type: View\n    roles: [Registered]\n    forms: [AssessForm]";
        }

        files["Shared/Entity.yaml"] = files["Shared/Entity.yaml"].Replace("gradingBasis: Decimal", "gradingBasis: Half")
                                      + "\n    - name: Assess\n      weight: 1\n      sources: [{name: AssessForm}]";
        files["Individual/Entity.yaml"] += """

                                               - name: SharedAssessment
                                                 weight: 1
                                               - name: IndividualAssessment
                                                 weight: 1
                                                 sources: [{name: AssessForm}]
                                           """;
        files["Individual/Steps/After.yaml"] += "\nprogress:\n  - text: 'Grade {{ Assessment.FinalGrade }}'";
        files["Common/Roles/Registered.yaml"] = "name: Registered";
        return files;
    }
}