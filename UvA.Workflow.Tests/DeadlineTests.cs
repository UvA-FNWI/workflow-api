using Moq;
using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Journaling;
using UvA.Workflow.Persistence;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Tests;

public class DeadlineTests
{
    private static readonly DictionaryProvider Content = new(new Dictionary<string, string>
    {
        ["Common/Roles/Registered.yaml"] = "name: Registered",
        ["Soft/Entity.yaml"] = "name: Soft\ntitlePlural: Soft steps\nsteps: [Step]",
        ["Soft/Steps/Step.yaml"] = """
                                   name: Step
                                   deadline:
                                     date: "=2000-01-01"
                                   actions:
                                     - name: Continue
                                       type: Execute
                                       roles: [Registered]
                                     - type: View
                                       roles: [Registered]
                                     - type: Edit
                                       roles: [Registered]
                                   """,
        ["Hard/Entity.yaml"] = "name: Hard\ntitlePlural: Hard steps\nsteps: [Step]",
        ["Hard/Steps/Step.yaml"] = """
                                   name: Step
                                   deadline:
                                     date: "=2000-01-01"
                                     type: Hard
                                   actions:
                                     - name: Continue
                                       type: Execute
                                       roles: [Registered]
                                     - type: View
                                       roles: [Registered]
                                     - type: Edit
                                       roles: [Registered]
                                   """
    });

    [Fact]
    public void Deadline_IsStepMetadata_AndDefaultsToSoft()
    {
        var modelService = new ModelService(new ModelParser(Content));
        var step = modelService.WorkflowDefinitions["Soft"].AllSteps.Single();
        var instance = Instance("Soft");

        Assert.Null(step.Condition);
        Assert.Equal(DeadlineType.Soft, step.Deadline!.Type);
        Assert.True(step.Deadline.HasPassed(modelService.CreateContext(instance)));
        Assert.Equal(["Step"], modelService.GetActiveSteps(instance));
    }

    [Fact]
    public async Task PassedHardDeadline_RemovesCurrentStepActions_ButKeepsStepActive()
    {
        var modelService = new ModelService(new ModelParser(Content));
        var repository = new Mock<IWorkflowInstanceRepository>();
        var factory = StepHeaderStatusTests.CreateWorkflowInstanceDtoFactory(modelService, repository);
        var soft = await factory.Create(Instance("Soft"), CancellationToken.None);
        var hard = await factory.Create(Instance("Hard"), CancellationToken.None);

        Assert.Single(soft.Actions);
        Assert.Empty(hard.Actions);
        Assert.Equal(["Step"], modelService.GetActiveSteps(Instance("Hard")));
        Assert.True(Assert.Single(hard.Steps).Deadline!.IsClosed);
    }

    [Theory]
    [InlineData("\"=2999-01-01\"", true)]
    [InlineData("\"=2000-01-01\"", false)]
    [InlineData("{ expressionText: '=2999-01-01' }", true)]
    [InlineData("{ expressionText: '=2000-01-01' }", false)]
    public async Task DeadlineCondition_ControlsActions_IndependentlyOfStepDeadline(string deadline, bool expected)
    {
        var content = new DictionaryProvider(new Dictionary<string, string>
        {
            ["Common/Roles/Registered.yaml"] = "name: Registered",
            ["Soft/Entity.yaml"] = "name: Soft\ntitlePlural: Soft steps\nsteps: [Step]",
            ["Soft/Steps/Step.yaml"] = $$"""
                                         name: Step
                                         deadline:
                                           date: "=2000-01-01"
                                         actions:
                                           - name: Continue
                                             type: Execute
                                             roles: [Registered]
                                             condition:
                                               deadline: {{deadline}}
                                         """
        });
        var modelService = new ModelService(new ModelParser(content));
        var instance = Instance("Soft");
        var step = modelService.WorkflowDefinitions["Soft"].AllSteps.Single();
        var context = modelService.CreateContext(instance);
        var userService = new Mock<IUserService>();
        userService.Setup(service => service.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var rightsService = new RightsService(modelService, userService.Object, Mock.Of<IWorkflowInstanceRepository>());

        var actions = await rightsService.GetAllowedActions(instance, [RoleAction.Execute]);

        Assert.Equal(expected ? 1 : 0, actions.Length);
        Assert.True(step.Deadline!.HasPassed(context));
        Assert.False(step.HasPassedHardDeadline(context));
        Assert.Equal(["Step"], modelService.GetActiveSteps(instance));
    }

    [Fact]
    public void DeadlineCondition_TracksProperties_AndSupportsNegationForMissingDates()
    {
        var modelService = new ModelService(new ModelParser(Content));
        var context = modelService.CreateContext(Instance("Soft"));
        var condition = new Condition { Deadline = "addDays(StartEvent, 1)" };

        Assert.Contains(condition.Properties, lookup => lookup.ToString() == "StartEvent");
        Assert.Null(condition.Deadline.Evaluate(context));
        Assert.False(condition.IsMet(context));
        condition.Not = true;
        Assert.True(condition.IsMet(context));
    }

    [Theory]
    [InlineData(DeadlineType.Hard, "=2000-01-01", false, true)]
    [InlineData(DeadlineType.Soft, "=2000-01-01", false, false)]
    [InlineData(DeadlineType.Hard, "=2999-01-01", false, false)]
    [InlineData(DeadlineType.Hard, "=2000-01-01", true, false)]
    public async Task Factory_ReplacesOnlyUnfinishedExpiredHardDeadlineContent(
        DeadlineType type, string date, bool completed, bool expectsMessage)
    {
        var modelService = new ModelService(new ModelParser(Content));
        var instance = Instance("Hard");
        var step = modelService.WorkflowDefinitions["Hard"].AllSteps.Single();
        step.Deadline!.Type = type;
        step.Deadline.Date = date;
        if (completed)
            step.Ends = new Condition { Deadline = "=2999-01-01" };
        var repository = new Mock<IWorkflowInstanceRepository>();
        repository.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        repository.Setup(r => r.GetAllById(It.IsAny<string[]>(), It.IsAny<Dictionary<string, string>>(),
            It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var factory = StepHeaderStatusTests.CreateWorkflowInstanceDtoFactory(modelService, repository);

        var dto = await factory.Create(instance, CancellationToken.None);
        var stepDto = Assert.Single(dto.Steps);

        Assert.NotNull(stepDto.Deadline);
        Assert.Equal(DateTime.Parse(date[1..]), stepDto.Deadline.Date);
        Assert.Null(stepDto.Deadline.Message);
        Assert.Equal(expectsMessage, stepDto.Deadline!.IsClosed);
        Assert.Equal(step.ResultsType, stepDto.ResultsType);
        if (expectsMessage)
        {
            Assert.Equal(StepHeaderPillType.Error, stepDto.HeaderStatus?.Type);
            Assert.False(stepDto.ExpectsSubmission);
            Assert.Empty(dto.Actions);
        }
    }

    [Fact]
    public void DeadlineText_ParsesBilingualTemplates_AndTracksTheirProperties()
    {
        var content = new DictionaryProvider(new Dictionary<string, string>
        {
            ["Hard/Entity.yaml"] = "name: Hard\ntitlePlural: Hard steps\nsteps: [Step]",
            ["Hard/Steps/Step.yaml"] = """
                                       name: Step
                                       deadline:
                                         date: "=2000-01-01"
                                         type: Hard
                                         text:
                                           en: "Closed on {{ formatDate(StartEvent, =dd-MM-yyyy) }}."
                                           nl: "Gesloten op {{ formatDate(StartEvent, =dd-MM-yyyy) }}."
                                       events:
                                         - name: Start
                                       """
        });
        var modelService = new ModelService(new ModelParser(content));
        var instance = Instance("Hard");
        instance.Events.Add("Start", new() { Id = "Start", Date = new DateTime(2000, 1, 1) });
        var step = modelService.WorkflowDefinitions["Hard"].AllSteps.Single();

        var message = step.Deadline!.TextTemplate!.Apply(modelService.CreateContext(instance));

        Assert.Equal("Closed on 01-01-2000.", message.En);
        Assert.Equal("Gesloten op 01-01-2000.", message.Nl);
        Assert.Contains(step.Lookups, lookup => lookup.ToString() == "StartEvent");
    }

    [Fact]
    public async Task Factory_ReturnsOnlyExplicitlyConfiguredText()
    {
        var modelService = new ModelService(new ModelParser(Content));
        modelService.WorkflowDefinitions["Hard"].AllSteps.Single().Deadline!.Text =
            new BilingualString("Contact staff about {{ Id }}", "Neem contact op over {{ Id }}");
        var factory = StepHeaderStatusTests.CreateWorkflowInstanceDtoFactory(modelService,
            new Mock<IWorkflowInstanceRepository>());

        var step = Assert.Single((await factory.Create(Instance("Hard"), CancellationToken.None)).Steps);

        Assert.True(step.Deadline!.IsClosed);
        Assert.Equal("Contact staff about Hard-instance", step.Deadline!.Message!.En);
        Assert.Equal("Neem contact op over Hard-instance", step.Deadline!.Message.Nl);
    }

    [Fact]
    public async Task Factory_DoesNotShowDeadlineMessageForInactiveFutureStep()
    {
        var modelService = CreateModelWithForms();
        modelService.WorkflowDefinitions["Hard"].AllSteps.Single(step => step.Name == "Open").Deadline =
            new Deadline { Date = "=2000-01-01", Type = DeadlineType.Hard };
        var factory = StepHeaderStatusTests.CreateWorkflowInstanceDtoFactory(modelService,
            new Mock<IWorkflowInstanceRepository>());

        var dto = await factory.Create(Instance("Hard"), CancellationToken.None);
        var future = dto.Steps.Single(step => step.Id == "Open");

        Assert.Equal(StepResultsType.Normal, future.ResultsType);
        Assert.False(future.Deadline!.IsClosed);
        Assert.Null(future.Deadline!.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HardDeadline_HidesChildForms_WithoutRemovingAvailableSiblingForms(bool submitted)
    {
        var modelService = CreateModelWithForms();
        var instance = Instance("Hard");
        if (submitted)
            foreach (var name in new[] { "Closed", "Available" })
                instance.Events.Add(name, new() { Id = name, Date = new DateTime(1999, 1, 1) });
        var repository = new Mock<IWorkflowInstanceRepository>();
        var factory = StepHeaderStatusTests.CreateWorkflowInstanceDtoFactory(modelService, repository);

        var dto = await factory.Create(instance, CancellationToken.None);

        Assert.Null(dto.Steps.Single(step => step.Id == "Open").Deadline);
        Assert.DoesNotContain(dto.Actions, action => action.Form == "Closed");
        Assert.DoesNotContain(dto.Submissions, submission => submission.FormName == "Closed");
        if (submitted)
            Assert.Contains(dto.Submissions, submission => submission.FormName == "Available");
        else
            Assert.Contains(dto.Actions, action => action.Form == "Available");

        modelService.WorkflowDefinitions["Hard"].AllSteps.Single(step => step.Name == "Step").Deadline!.Type =
            DeadlineType.Soft;
        var soft = await factory.Create(instance, CancellationToken.None);
        if (submitted)
            Assert.Contains(soft.Submissions, submission => submission.FormName == "Closed");
        else
            Assert.Contains(soft.Actions, action => action.Form == "Closed");
    }

    [Fact]
    public async Task HardDeadline_RejectsLiveFormAccessBeforeLoadingHistory()
    {
        var modelService = CreateModelWithForms();
        var instance = Instance("Hard");
        var repository = new Mock<IWorkflowInstanceRepository>();
        repository.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        var journal = new Mock<IInstanceJournalService>(MockBehavior.Strict);
        var service = new WorkflowInstanceService(modelService, repository.Object, journal.Object,
            Mock.Of<IInstanceEventRepository>(), Mock.Of<IUserService>(), Mock.Of<IUserRepository>());

        await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
            service.GetSubmissionContext(instance.Id, "Closed", null, CancellationToken.None));
        journal.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(StepResultsType.Normal)]
    [InlineData(StepResultsType.AssessmentPartOverview)]
    [InlineData(StepResultsType.AssessmentFinalOverview)]
    public async Task HardDeadline_ResponsePreservesResultsTypeWithoutCurrentForms(StepResultsType resultsType)
    {
        var modelService = CreateModelWithForms();
        var instance = Instance("Hard");
        instance.Events.Add("Closed", new() { Id = "Closed", Date = new DateTime(1999, 1, 1) });
        modelService.WorkflowDefinitions["Hard"].AllSteps.Single(step => step.Name == "Step").ResultsType =
            resultsType;
        var repository = new Mock<IWorkflowInstanceRepository>();
        repository.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        repository.Setup(r => r.GetAllById(It.IsAny<string[]>(), It.IsAny<Dictionary<string, string>>(),
            It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var factory = StepHeaderStatusTests.CreateWorkflowInstanceDtoFactory(modelService, repository);

        var dto = await factory.Create(instance, CancellationToken.None);
        var step = dto.Steps.Single(s => s.Id == "Step");

        Assert.Null(step.Deadline!.Message);
        Assert.Equal(resultsType, step.ResultsType);
        Assert.Null(step.Children);
        Assert.Null(step.Versions);
        Assert.False(step.HasSubmission);
        Assert.False(step.ExpectsSubmission);
        Assert.True(step.Deadline!.IsClosed);
        Assert.Empty(dto.Submissions);
        Assert.DoesNotContain(dto.Actions, action => action.Form == "Closed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubmittedStart_RemainsVisibleAfterDeadline_UnlessReopenedByRejection(bool rejected)
    {
        var modelService = new ModelService(new ModelParser(new FileSystemProvider(UnitTestsHelpers.FixturesPath)));
        var definition = modelService.WorkflowDefinitions["Project"];
        var start = definition.AllSteps.Single(step => step.Name == "Start");
        start.Deadline = new Deadline { Date = "=2000-01-01", Type = DeadlineType.Hard };
        var submittedAt = new DateTime(1999, 12, 31);
        var instance = Instance("Project");
        instance.CurrentStep = rejected ? "Start" : "SubjectFeedback";
        instance.Properties["Title"] = "Submitted project";
        instance.Properties["Subject"] = "Proposal to review";
        instance.Events.Add("Start", new() { Id = "Start", Date = submittedAt });
        if (rejected)
            instance.Events.Add("RejectSubject", new() { Id = "RejectSubject", Date = new DateTime(2000, 1, 2) });

        var repository = new Mock<IWorkflowInstanceRepository>();
        repository.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        repository.Setup(r => r.GetAllById(It.IsAny<string[]>(), It.IsAny<Dictionary<string, string>>(),
            It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var user = new Mock<IUserService>();
        user.Setup(service => service.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["Coordinator"]);
        var service = new WorkflowInstanceService(modelService, repository.Object,
            Mock.Of<IInstanceJournalService>(), Mock.Of<IInstanceEventRepository>(), user.Object,
            Mock.Of<IUserRepository>());
        var factory = StepHeaderStatusTests.CreateWorkflowInstanceDtoFactory(modelService, repository, ["Coordinator"]);

        var dto = await factory.Create(instance, CancellationToken.None);
        var subjectDto = dto.Steps.Single(step => step.Id == "Subject");
        var startDto = subjectDto.Children!.Single(step => step.Id == "Start");
        if (rejected)
        {
            Assert.Null(startDto.Deadline!.Message);
            Assert.True(startDto.Deadline!.IsClosed);
            Assert.DoesNotContain(dto.Submissions, submission => submission.FormName == "Start");
            await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
                service.GetSubmissionContext(instance.Id, "Start", null, CancellationToken.None));
        }
        else
        {
            Assert.Null(startDto.Deadline!.Message);
            Assert.False(startDto.Deadline!.IsClosed);
            Assert.NotEqual(StepHeaderPillType.Error, subjectDto.HeaderStatus?.Type);
            Assert.True(startDto.HasSubmission);
            Assert.Equal(submittedAt, startDto.DateCompleted);
            var submission = Assert.Single(dto.Submissions, submission => submission.FormName == "Start");
            Assert.Equal("Proposal to review",
                submission.Answers.Single(answer => answer.QuestionName == "Subject").Value!.Value.GetString());
            Assert.Contains(dto.Actions, action => action.Form == "ApproveSubject");
            Assert.Contains(dto.Actions, action => action.Form == "RejectSubject");
            var context = await service.GetSubmissionContext(instance.Id, "Start", null, CancellationToken.None);
            Assert.True(context.SubmissionState.IsSubmitted);
        }
    }

    [Theory]
    [InlineData("Start", true)]
    [InlineData("Subject", true)]
    [InlineData("Start", false)]
    [InlineData("Subject", false)]
    public async Task ExpiredStep_KeepsAuthorizedReadOnlyVersions(string deadlineStepName, bool canView)
    {
        var modelService = new ModelService(new ModelParser(new FileSystemProvider(UnitTestsHelpers.FixturesPath)));
        var definition = modelService.WorkflowDefinitions["Project"];
        definition.AllSteps.Single(step => step.Name == deadlineStepName).Deadline =
            new Deadline { Date = "=2000-01-01", Type = DeadlineType.Hard };
        var instance = Instance("Project");
        instance.Properties["Subject"] = "Current draft";
        instance.Id = "507f1f77bcf86cd799439011";
        var submittedAt = new DateTime(1999, 12, 30);
        var rejectedAt = new DateTime(1999, 12, 31);
        instance.Events.Add("Start", new() { Id = "Start", Date = submittedAt });
        instance.Events.Add("RejectSubject", new() { Id = "RejectSubject", Date = rejectedAt });
        var history = new WorkflowInstanceHistory(new InstanceJournalEntry
            {
                InstanceId = instance.Id,
                PropertyChanges =
                    [PropertyChangeEntry.Create("Subject", "Original proposal", new User { UserName = "coordinator" })]
            },
            [
                new()
                {
                    WorkflowInstanceId = instance.Id, EventId = "Start", Timestamp = submittedAt,
                    EventDate = submittedAt, Operation = EventLogOperation.Create
                },
                new()
                {
                    WorkflowInstanceId = instance.Id, EventId = "RejectSubject", Timestamp = rejectedAt,
                    EventDate = rejectedAt, Operation = EventLogOperation.Create
                }
            ]);
        var repository = new Mock<IWorkflowInstanceRepository>();
        repository.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        repository.Setup(r => r.GetAllById(It.IsAny<string[]>(), It.IsAny<Dictionary<string, string>>(),
            It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var factory = StepHeaderStatusTests.CreateWorkflowInstanceDtoFactory(modelService, repository,
            canView ? ["Coordinator"] : [], history);

        var dto = await factory.Create(instance, CancellationToken.None);
        var subject = dto.Steps.Single(step => step.Id == "Subject");
        var expired = deadlineStepName == "Subject" ? subject : subject.Children!.Single(step => step.Id == "Start");
        Assert.Null(expired.Deadline!.Message);
        Assert.True(expired.Deadline!.IsClosed);
        Assert.False(expired.ExpectsSubmission);
        Assert.DoesNotContain(dto.Actions, action => action.Form == "Start");
        Assert.DoesNotContain(dto.Submissions, submission => submission.FormName == "Start");
        var version = Assert.Single(expired.Versions!);
        if (canView)
        {
            var submission = Assert.Single(version.Submissions, submission => submission.FormName == "Start");
            Assert.Empty(submission.Permissions);
            Assert.Equal("Original proposal",
                submission.Answers.Single(answer => answer.QuestionName == "Subject").Value!.Value.GetString());
        }
        else
        {
            Assert.Empty(version.Submissions);
        }
    }

    private static ModelService CreateModelWithForms() => new(new ModelParser(new DictionaryProvider(
        new Dictionary<string, string>
        {
            ["Common/Roles/Registered.yaml"] = """
                                               name: Registered
                                               actions:
                                                 - type: View
                                                   form: <All>
                                                 - type: Edit
                                                   form: <All>
                                                 - type: Submit
                                                   forms: [Closed, Available]
                                               """,
            ["Hard/Entity.yaml"] = "name: Hard\ntitlePlural: Hard steps\nsteps: [Step, Open]",
            ["Hard/Steps/Step.yaml"] = """
                                       name: Step
                                       deadline:
                                         date: "=2000-01-01"
                                         type: Hard
                                       children: [Child]
                                       """,
            ["Hard/Steps/Child.yaml"] = "name: Child",
            ["Hard/Steps/Open.yaml"] = "name: Open",
            ["Hard/Forms/Closed.yaml"] = "name: Closed\nstep: Child\npages: []",
            ["Hard/Forms/Available.yaml"] = "name: Available\nstep: Open\npages: []"
        })));

    private static WorkflowInstance Instance(string definition) => new()
    {
        Id = $"{definition}-instance",
        WorkflowDefinition = definition,
        CurrentStep = "Step",
        Properties = new(),
        Events = new()
    };
}