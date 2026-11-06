using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Personal;
using UvA.Workflow.Api.Screens;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Events;
using UvA.Workflow.Jobs;
using UvA.Workflow.Journaling;
using UvA.Workflow.Notifications;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Tests.WorkflowInstances;
using UvA.Workflow.Users;
using UvA.Workflow.Versioning;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Controllers;

public class LinkedStepPresentationTests
{
    [Fact]
    public async Task Instance_ProjectsSourceMetadataWithoutTakingOwnershipOfSubmissions()
    {
        var api = new LinkedStepApiSetup(participants: ["Student"]);
        api.Core.Complete("Uploaded", 20);

        var dto = await api.Factory.Create(api.Core.Student, default);
        var imported = dto.Steps.Single(step => step.Id == "SharedUpload");

        Assert.Equal("SharedAssessment", dto.CurrentStep);
        Assert.Equal("Joint upload", imported.Title.En);
        Assert.Equal(api.Core.Source.Id, imported.ImportedFrom!.InstanceId);
        Assert.Equal("Upload", imported.ImportedFrom.StepId);
        Assert.Equal("Student A", Assert.Single(imported.ImportedFrom.Participants).DisplayName);
        Assert.Equal(api.Core.Source.Events["Uploaded"].Date, imported.DateCompleted);
        Assert.True(imported.IsAvailable);
        Assert.Null(imported.Versions);
        Assert.Null(imported.UndoCandidate);
        Assert.Empty(dto.Submissions);
        Assert.Empty(dto.Actions);
    }

    [Fact]
    public async Task Instance_CombinesConfiguredParticipantRolesAndDeduplicatesByUserId()
    {
        var api = new LinkedStepApiSetup(participants: ["Members", "Supervisor"]);
        var student = api.Core.Source.Properties["Student"].AsBsonArray[0];
        var other = InstanceUser.FromUser(new User
        {
            Id = ObjectId.GenerateNewId().ToString(), UserName = "b", DisplayName = "Student A"
        }).ToBsonDocument();
        api.Core.Source.Properties["Members"] = new BsonArray { student, other };
        api.Core.Source.Properties["Supervisor"] = student;

        var dto = await api.Factory.Create(api.Core.Student, default);

        Assert.Equal(["Student A", "Student A"],
            dto.Steps.Single(step => step.Id == "SharedUpload").ImportedFrom!.Participants
                .Select(user => user.DisplayName));
    }

    [Fact]
    public async Task Instance_OmittedParticipantsDoesNotAssumeStudent()
    {
        var api = new LinkedStepApiSetup();

        var dto = await api.Factory.Create(api.Core.Student, default);

        Assert.Empty(dto.Steps.Single(step => step.Id == "SharedUpload").ImportedFrom!.Participants);
    }

    [Fact]
    public async Task Instance_RestrictedSourceDoesNotExposeItsIdentityOrParticipants()
    {
        var api = new LinkedStepApiSetup(sourceAccess: false, participants: ["Student"]);
        api.Core.Complete("Uploaded", 20);
        api.Core.Complete("Assessed", 21);

        var dto = await api.Factory.Create(api.Core.Student, default);
        var imported = dto.Steps.Single(step => step.Id == "SharedUpload");

        Assert.Equal("After", dto.CurrentStep);
        Assert.Equal(StepImportStatus.Restricted, imported.ImportStatus);
        Assert.Null(imported.ImportedFrom);
        Assert.Null(imported.Versions);
        Assert.Null(imported.UndoCandidate);
    }

    [Fact]
    public async Task Overviews_ResolveSourceStateWithoutADetailPageVisit()
    {
        var api = new LinkedStepApiSetup();
        api.Core.Complete("Uploaded", 20);
        api.Core.Complete("Assessed", 21);
        api.Core.Repository.Setup(repo => repo.GetByWorkflowDefinition("Individual",
                It.IsAny<MongoDB.Driver.FilterDefinition<WorkflowInstance>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([api.Core.Student]);
        var screens = new ScreenDataService(api.Core.Model, api.Instances, api.Core.Repository.Object,
            new InstanceAuthorizationFilterService(api.Rights, api.Core.Model, api.Users.Object,
                api.Core.Repository.Object), api.Rights);

        var data = await screens.GetScreenData("Overview", "Individual", default);

        var group = Assert.Single(data.Groups!, group => group.Rows.Length > 0);
        Assert.Equal("after", group.Name);
        api.Core.Repository.Verify(repo => repo.GetByIds(
            It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Before", api.Core.Student.CurrentStep);
    }

    [Fact]
    public async Task CompletedProgress_DoesNotUseAnEarlierLocalEventDate()
    {
        var api = new LinkedStepApiSetup(finalImport: true);
        api.Core.Complete("Uploaded", 20);
        api.Core.Complete("Assessed", 21);
        var context = await api.Core.Resolver.Resolve(api.Core.Student, default);

        var progress = StepProgressResolver.ResolveProgress(api.Core.Model.WorkflowDefinitions["Individual"],
            null, context, api.Core.Model.GetCompletionDate(api.Core.Student, context));

        Assert.Contains("21/06", progress.Text.En);
        Assert.DoesNotContain("01/06", progress.Text.En);
    }

    [Fact]
    public async Task PersonalOverview_UsesTheSharedStateOnItsFirstRead()
    {
        var api = new LinkedStepApiSetup();
        api.Core.Complete("Uploaded", 20);
        api.Core.Complete("Assessed", 21);
        api.Core.Repository.Setup(repo => repo.GetByFilter(
                It.IsAny<MongoDB.Driver.FilterDefinition<WorkflowInstance>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([api.Core.Student]);
        var user = await api.Users.Object.GetCurrentUser(default);

        var dto = await new PersonalInstanceService(api.Core.Model, api.Instances, api.Core.Repository.Object,
                api.Rights)
            .GetInstances(user!, default);

        Assert.Equal("After", Assert.Single(dto.Instances).CurrentStep);
        Assert.Equal("Before", api.Core.Student.CurrentStep);
    }

    [Theory]
    [InlineData(true, "Joint assessment")]
    [InlineData(false, "Shared step")]
    public async Task CurrentStepField_OnlyShowsTheSourceTitleWhenTheViewerCanAccessIt(bool canView, string title)
    {
        var api = new LinkedStepApiSetup(sourceAccess: canView);
        api.Core.Model.WorkflowDefinitions["Individual"].Fields = [new Field { CurrentStep = true }];
        api.Core.Complete("Uploaded", 20);

        var dto = await api.Factory.Create(api.Core.Student, default);

        Assert.Equal(title, Assert.IsType<BilingualString>(Assert.Single(dto.Fields).Value).En);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverviewProgress_OnlyRendersSourceValuesWhenTheViewerCanAccessTheSource(bool canView)
    {
        var api = new LinkedStepApiSetup(sourceAccess: canView);
        var sourceDefinition = api.Core.Model.WorkflowDefinitions["Shared"];
        sourceDefinition.Properties.Add(new StringPropertyDefinition { Name = "SourceSecret", Type = "String" });
        sourceDefinition.Steps[0].Progress = [new ProgressInformation { Text = "Source says {{ SourceSecret }}" }];
        api.Core.Source.Properties["SourceSecret"] = "protected-value";
        api.Core.Repository.Setup(repo => repo.GetByWorkflowDefinition("Individual",
                It.IsAny<MongoDB.Driver.FilterDefinition<WorkflowInstance>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([api.Core.Student]);
        api.Core.Repository.Setup(repo => repo.GetByFilter(
                It.IsAny<MongoDB.Driver.FilterDefinition<WorkflowInstance>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([api.Core.Student]);
        var screens = new ScreenDataService(api.Core.Model, api.Instances, api.Core.Repository.Object,
            new InstanceAuthorizationFilterService(api.Rights, api.Core.Model, api.Users.Object,
                api.Core.Repository.Object), api.Rights);

        var screen = await screens.GetScreenData("Overview", "Individual", default);
        var row = Assert.Single(Assert.Single(screen.Groups!, group => group.Rows.Length > 0).Rows);
        var screenProgress = Assert.IsType<ProgressInformationDto>(Assert.Single(row.Values).Value);
        var user = await api.Users.Object.GetCurrentUser(default);
        var personal =
            await new PersonalInstanceService(api.Core.Model, api.Instances, api.Core.Repository.Object, api.Rights)
                .GetInstances(user!, default);

        Assert.Equal(canView, screenProgress.Text.En.Contains("protected-value", StringComparison.Ordinal));
        Assert.Equal(canView,
            Assert.Single(personal.Instances).Progress.Text.En.Contains("protected-value", StringComparison.Ordinal));
        Assert.Equal("SharedUpload", Assert.Single(personal.Instances).CurrentStep);
    }
}

internal sealed class LinkedStepApiSetup
{
    public LinkedStepTestSetup Core { get; }
    public Mock<IUserService> Users { get; } = new();
    public RightsService Rights { get; }
    public InstanceService Instances { get; }
    public WorkflowInstanceDtoFactory Factory { get; }

    public LinkedStepApiSetup(bool sourceAccess = true, bool finalImport = false, string[]? participants = null)
    {
        var files = LinkedStepConfigurationTests.Files();
        if (participants != null)
        {
            files["Shared/Entity.yaml"] +=
                "\n  - name: Members\n    type: '[User]'\n  - name: Supervisor\n    type: User";
            files["Individual/Steps/SharedUpload.yaml"] +=
                "\n  participants: [" + string.Join(", ", participants) + "]";
        }

        files["Common/Roles/Registered.yaml"] = "name: Registered";
        files["Common/Roles/Student.yaml"] = "name: Student";
        files["Individual/Actions.yaml"] = "globalActions:\n  - type: View\n    roles: [Registered, Student]";
        if (sourceAccess) files["Shared/Actions.yaml"] = files["Individual/Actions.yaml"];
        files["Individual/Screens/Overview.yaml"] = """
                                                    name: Overview
                                                    columns:
                                                      - currentStep: true
                                                    grouping:
                                                      groups:
                                                        - name: shared
                                                          title: Shared
                                                          steps: [SharedUpload, SharedAssessment]
                                                        - name: after
                                                          title: After
                                                          steps: [After]
                                                    """;
        if (finalImport)
            files["Individual/Entity.yaml"] = files["Individual/Entity.yaml"].Replace(
                "[Before, SharedUpload, SharedAssessment, After]", "[Before, SharedUpload, SharedAssessment]");
        Core = new LinkedStepTestSetup(files);
        Core.Repository.Setup(repo => repo.GetAllByType(It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<BsonDocument?>(),
            It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var student = new User { Id = ObjectId.GenerateNewId().ToString(), UserName = "a", DisplayName = "Student A" };
        Core.Source.Properties["Student"] = new BsonArray { InstanceUser.FromUser(student).ToBsonDocument() };
        Core.Student.Properties["Student"] = InstanceUser.FromUser(student).ToBsonDocument();
        Users.Setup(service => service.GetRolesOfCurrentUser()).ReturnsAsync(Array.Empty<string>());
        Users.Setup(service => service.GetCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync(student);
        Rights = new RightsService(Core.Model, Users.Object, Core.Repository.Object, Core.Resolver);
        var layouts = new Mock<IMailLayoutResolver>();
        layouts.Setup(service => service.Resolve(It.IsAny<string?>())).Returns(Mock.Of<IMailLayout>());
        Instances = new InstanceService(Core.Repository.Object, Core.Model, Users.Object, Rights,
            UnitTestsHelpers.CreateMailBuilder(layouts.Object), Core.Enricher, Core.Resolver);
        var events = new Mock<IInstanceEventRepository>();
        events.Setup(service =>
                service.GetEventLogEntriesForInstance(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var workflows = new WorkflowInstanceService(Core.Model, Core.Repository.Object,
            Mock.Of<IInstanceJournalService>(), events.Object, Users.Object, Mock.Of<IUserRepository>());
        var submissions = new SubmissionDtoFactory(new ArtifactTokenService(UnitTestsHelpers.TestS3Config),
            Core.Model, Instances);
        var undo = new UndoService(events.Object, Mock.Of<IJobRepository>(), Core.Repository.Object,
            Instances, Rights, Core.Model);
        Factory = new WorkflowInstanceDtoFactory(Instances, Core.Model, submissions, Rights,
            new StepVersionService(Core.Model), new StepProgressResolver(Core.Model), workflows,
            NullLogger<WorkflowInstanceDtoFactory>.Instance, undo);
    }
}