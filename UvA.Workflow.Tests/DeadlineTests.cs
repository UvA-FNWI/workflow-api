using System.Globalization;
using System.Text.Json;
using Moq;
using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Journaling;
using UvA.Workflow.Persistence;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Tools;
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
        Assert.True(Assert.Single(hard.Steps).Deadline!.IsPassed);
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
    [InlineData(DeadlineType.Hard, "=2000-01-01", false, true, true)]
    [InlineData(DeadlineType.Soft, "=2000-01-01", false, false, true)]
    [InlineData(DeadlineType.Hard, "=2999-01-01", false, false, false)]
    [InlineData(DeadlineType.Hard, "=2000-01-01", true, false, false)]
    public async Task Factory_ReportsDeadlineStatusForUnfinishedSteps(
        DeadlineType type, string date, bool completed, bool expectsMessage, bool expectsPassed)
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
        Assert.Equal(DateTimeOffset.Parse(date[1..], CultureInfo.InvariantCulture), stepDto.Deadline.Date);
        Assert.Null(stepDto.Deadline.Message);
        Assert.Equal(type, stepDto.Deadline.Type);
        Assert.Equal(expectsPassed, stepDto.Deadline.IsPassed);
        if (expectsPassed)
        {
            Assert.NotNull(stepDto.HeaderStatus);
            Assert.Equal(StepHeaderPillType.Error, stepDto.HeaderStatus.Type);
            Assert.Null(stepDto.HeaderStatus.Label);
        }

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

        Assert.True(step.Deadline!.IsPassed);
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
        Assert.False(future.Deadline!.IsPassed);
        Assert.Null(future.HeaderStatus);
        Assert.Null(future.Deadline!.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HardDeadline_BlocksChildDrafts_ButKeepsSubmittedAndSiblingForms(bool submitted)
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
        if (submitted)
        {
            Assert.Contains(dto.Submissions, submission => submission.FormName == "Closed");
            Assert.Contains(dto.Submissions, submission => submission.FormName == "Available");
        }
        else
        {
            Assert.DoesNotContain(dto.Submissions, submission => submission.FormName == "Closed");
            Assert.Contains(dto.Actions, action => action.Form == "Available");
        }

        modelService.WorkflowDefinitions["Hard"].AllSteps.Single(step => step.Name == "Step").Deadline!.Type =
            DeadlineType.Soft;
        var soft = await factory.Create(instance, CancellationToken.None);
        if (submitted)
            Assert.Contains(soft.Submissions, submission => submission.FormName == "Closed");
        else
            Assert.Contains(soft.Actions, action => action.Form == "Closed");
    }

    [Fact]
    public async Task HardDeadline_AllowsViewButRejectsSubmitBeforeLoadingHistory()
    {
        var modelService = CreateModelWithForms();
        var instance = Instance("Hard");
        var repository = new Mock<IWorkflowInstanceRepository>();
        repository.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        var journal = new Mock<IInstanceJournalService>(MockBehavior.Strict);
        var service = new WorkflowInstanceService(modelService, repository.Object, journal.Object,
            Mock.Of<IInstanceEventRepository>(), Mock.Of<IUserService>(), Mock.Of<IUserRepository>());

        var context = await service.GetSubmissionContext(instance.Id, "Closed", null, CancellationToken.None);
        var user = new Mock<IUserService>();
        user.Setup(u => u.GetRolesOfCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var rights = new RightsService(modelService, user.Object, repository.Object);
        await rights.EnsureAuthorizedForAction(context.Instance, RoleAction.View, context.Form.Name);
        await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
            rights.EnsureAuthorizedForAction(context.Instance, RoleAction.Submit, context.Form.Name));
        journal.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Step", DeadlineType.Hard, "=2000-01-01", false)]
    [InlineData("Child", DeadlineType.Hard, "=2000-01-01", false)]
    [InlineData("Step", DeadlineType.Soft, "=2000-01-01", true)]
    [InlineData("Child", DeadlineType.Soft, "=2000-01-01", true)]
    [InlineData("Step", DeadlineType.Hard, "=2999-01-01", true)]
    [InlineData("Child", DeadlineType.Hard, "=2999-01-01", true)]
    public async Task Factory_ExpectsSubmissionUntilOwnOrParentHardDeadlinePasses(
        string deadlineStepName, DeadlineType type, string date, bool expected)
    {
        var model = CreateModelWithForms();
        var definition = model.WorkflowDefinitions["Hard"];
        definition.AllSteps.Single(step => step.Name == "Step").Deadline = null;
        definition.AllSteps.Single(step => step.Name == deadlineStepName).Deadline =
            new Deadline { Date = date, Type = type };
        var repository = new Mock<IWorkflowInstanceRepository>();
        var factory = StepHeaderStatusTests.CreateWorkflowInstanceDtoFactory(model, repository);

        var dto = await factory.Create(Instance("Hard"), CancellationToken.None);
        var child = Assert.Single(dto.Steps.Single(step => step.Id == "Step").Children!);

        Assert.Equal(expected, child.ExpectsSubmission);
        Assert.False(child.HasSubmission);
    }

    [Theory]
    [InlineData(StepResultsType.Normal)]
    [InlineData(StepResultsType.AssessmentPartOverview)]
    [InlineData(StepResultsType.AssessmentFinalOverview)]
    public async Task HardDeadline_ResponsePreservesResultsTypeAndReadOnlySubmission(StepResultsType resultsType)
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
        var child = Assert.Single(step.Children!);
        Assert.Equal("Child", child.Id);
        Assert.True(child.HasSubmission);
        Assert.False(child.ExpectsSubmission);
        Assert.Null(step.Versions);
        Assert.False(step.HasSubmission);
        Assert.False(step.ExpectsSubmission);
        Assert.True(step.Deadline!.IsPassed);
        var submission = Assert.Single(dto.Submissions);
        Assert.Equal("Closed", submission.FormName);
        Assert.Empty(submission.Permissions);
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
            Assert.True(startDto.Deadline!.IsPassed);
            Assert.DoesNotContain(dto.Submissions, submission => submission.FormName == "Start");
            var rights = new RightsService(modelService, user.Object, repository.Object);
            Assert.True(await rights.Can(instance, RoleAction.View, "Start"));
            Assert.False(await rights.Can(instance, RoleAction.Submit, "Start"));
        }
        else
        {
            Assert.Null(startDto.Deadline!.Message);
            Assert.False(startDto.Deadline!.IsPassed);
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
        Assert.True(expired.Deadline!.IsPassed);
        Assert.False(subject.Children!.Single(step => step.Id == "Start").ExpectsSubmission);
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

    [Theory]
    [InlineData(RightsEvaluationMode.RequestContext)]
    [InlineData(RightsEvaluationMode.RealUser)]
    public async Task Rights_ExpiredStepPreservesSeparateGlobalFormGrants(RightsEvaluationMode mode)
    {
        var model = CreateModelWithForms();
        var role = model.WorkflowDefinitions.First().Value.Roles.Get("Registered");
        role.Actions =
        [
            new() { Type = RoleAction.View, Form = WorkflowModel.Action.All },
            new() { Type = RoleAction.Edit, Forms = ["Closed", "Available"] },
            new() { Type = RoleAction.Submit, Form = "Closed" }
        ];
        var user = new Mock<IUserService>();
        user.Setup(u => u.GetRolesOfCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var rights = new RightsService(model, user.Object, Mock.Of<IWorkflowInstanceRepository>());
        var instance = Instance("Hard");

        var actions =
            await rights.GetAllowedActions(instance, mode, RoleAction.View, RoleAction.Edit, RoleAction.Submit);

        Assert.Equal(role.Actions, actions);
        foreach (var action in actions)
            Assert.True(await rights.Can(instance, [action.Type], mode, "Closed"));
    }

    [Fact]
    public async Task Rights_RestrictExpiredSteps_WhileKeepingOtherActiveStepsAndUnscopedActions()
    {
        var model = CreateModelWithForms();
        var instance = Instance("Hard");
        instance.CurrentStep = "Open";
        var definition = model.WorkflowDefinitions.Single().Value;
        var active = model.GetActiveSteps(instance);
        Assert.Contains("Open", active);
        var role = definition.Roles.Get("Registered");
        role.Actions =
        [
            new() { Type = RoleAction.Execute, Name = "ClosedOnly", Steps = ["Child"] },
            new() { Type = RoleAction.Execute, Name = "Shared", Steps = ["Child", "Open"] },
            new() { Type = RoleAction.Execute, Name = "Unscoped" },
            new() { Type = RoleAction.View }
        ];
        var user = new Mock<IUserService>();
        user.Setup(u => u.GetRolesOfCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var rights = new RightsService(model, user.Object, Mock.Of<IWorkflowInstanceRepository>());

        var actions = await rights.GetAllowedActions(instance, RoleAction.Execute, RoleAction.View);

        Assert.DoesNotContain(actions, a => a.Name == "ClosedOnly");
        Assert.Same(role.Actions[1], Assert.Single(actions, a => a.Name == "Shared"));
        Assert.Contains(actions, a => a.Name == "Unscoped");
        Assert.True(await rights.Can(instance, RoleAction.View));
        Assert.Equal(active, model.GetActiveSteps(instance));
        Assert.Equal(["Child", "Open"], role.Actions[1].Steps);
    }

    [Theory]
    [InlineData(RightsEvaluationMode.RequestContext, true)]
    [InlineData(RightsEvaluationMode.RealUser, true)]
    [InlineData(RightsEvaluationMode.RequestContext, false)]
    [InlineData(RightsEvaluationMode.RealUser, false)]
    public async Task Rights_HardDeadlinePreservesOnlyView_AndStillRequiresAnActiveStep(
        RightsEvaluationMode mode, bool active)
    {
        var model = CreateModelWithForms();
        var instance = Instance("Hard");
        var step = active ? "Child" : "Open";
        RoleAction[] requested = [RoleAction.View, RoleAction.Edit, RoleAction.Submit, RoleAction.Execute];
        var role = model.WorkflowDefinitions.First().Value.Roles.Get("Registered");
        role.Actions = requested.Select(type => new WorkflowModel.Action
        {
            Type = type, Form = "Closed", Steps = [step]
        }).ToList();
        var user = new Mock<IUserService>();
        user.Setup(u => u.GetRolesOfCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var rights = new RightsService(model, user.Object, Mock.Of<IWorkflowInstanceRepository>());

        var actions = await rights.GetAllowedActions(instance, mode, requested);
        var perRole = rights.GetAllowedActionsPerTargetRole(instance, requested).SelectMany(r => r.Actions);

        if (active)
        {
            Assert.Equal(RoleAction.View, Assert.Single(actions).Type);
            Assert.Equal(RoleAction.View, Assert.Single(perRole).Type);
        }
        else
        {
            Assert.Empty(actions);
            Assert.Empty(perRole);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rights_ViewsHonorImpersonationAndConditions_ButIgnoreHardDeadlines(bool permitted)
    {
        var model = CreateModelWithForms();
        var role = model.WorkflowDefinitions.First().Value.Roles.Get("Registered");
        role.Name = "HistoricalReader";
        role.Actions =
        [
            new()
            {
                Type = RoleAction.View, Form = "Closed", Steps = ["Child"],
                Condition = new Condition { Deadline = permitted ? "=2999-01-01" : "=2000-01-01" }
            },
            new() { Type = RoleAction.Edit, Form = "Closed" }
        ];
        var user = new Mock<IUserService>();
        user.Setup(u => u.GetRolesOfCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var impersonation = new Mock<IImpersonationContextService>();
        impersonation.Setup(i => i.GetImpersonatedRole(It.IsAny<WorkflowInstance>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("HistoricalReader");
        var rights = new RightsService(model, user.Object, Mock.Of<IWorkflowInstanceRepository>(),
            impersonation.Object);
        var instance = Instance("Hard");

        var actions = await rights.GetAllowedActions(instance, RoleAction.View);

        Assert.Equal(permitted ? 1 : 0, actions.Length);
        Assert.All(actions, action => Assert.Equal(RoleAction.View, action.Type));
        Assert.Equal(permitted, await rights.Can(instance, RoleAction.View, "Closed"));
    }

    [Theory]
    [InlineData(-1, 14)]
    [InlineData(1, -12)]
    [InlineData(-1, -12)]
    [InlineData(1, 14)]
    public void Deadline_ComparesInstantsAcrossOffsets(int hoursFromNow, int offsetHours)
    {
        var date = DateTimeOffset.UtcNow.AddHours(hoursFromNow).ToOffset(TimeSpan.FromHours(offsetHours));
        var expression = "=" + date.ToString("O", CultureInfo.InvariantCulture);
        var context = new ObjectContext(new());

        Assert.Equal(hoursFromNow < 0, new Deadline { Date = expression }.HasPassed(context));
        Assert.Equal(hoursFromNow > 0, new DeadlineCondition { ExpressionText = expression }.IsMet(context));
    }

    [Fact]
    public void Deadline_EvaluatesCalculatedUtcDate_AndLeavesMissingDatesUnset()
    {
        var submittedAt = new DateTime(2026, 3, 16, 12, 0, 0, DateTimeKind.Utc);
        var context = new ObjectContext(new() { ["StartEvent"] = submittedAt });
        var calculated = new Deadline { Date = "addWeeks(StartEvent, 2)" }.Evaluate(context);

        Assert.Equal(new DateTimeOffset(submittedAt.AddDays(14)), calculated);
        Assert.Equal(TimeSpan.Zero, calculated!.Value.Offset);
        var missing = new Deadline { Date = "addWeeks(MissingEvent, 2)" };
        Assert.Null(missing.Evaluate(context));
        Assert.False(missing.HasPassed(context));
    }

    [Theory]
    [InlineData(DeadlineType.Soft, "2000-03-16T12:34:56+05:45", true)]
    [InlineData(DeadlineType.Hard, "2000-03-16T12:34:56+05:45", true)]
    [InlineData(DeadlineType.Soft, "2999-03-16T12:34:56-04:00", false)]
    [InlineData(DeadlineType.Hard, "2999-03-16T12:34:56-04:00", false)]
    public async Task Deadline_PreservesOffsetInResponse_AndAppliesSoftHardPermissions(
        DeadlineType type, string date, bool passed)
    {
        var model = CreateModelWithForms();
        var instance = Instance("Hard");
        model.WorkflowDefinitions["Hard"].AllSteps.Single(s => s.Name == "Step").Deadline =
            new Deadline { Date = "=" + date, Type = type };
        var repository = new Mock<IWorkflowInstanceRepository>();
        var factory = StepHeaderStatusTests.CreateWorkflowInstanceDtoFactory(model, repository);
        var user = new Mock<IUserService>();
        user.Setup(u => u.GetRolesOfCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var rights = new RightsService(model, user.Object, repository.Object);

        var dto = await factory.Create(instance, CancellationToken.None);
        var deadline = dto.Steps.Single(s => s.Id == "Step").Deadline!;
        var expected = DateTimeOffset.Parse(date, CultureInfo.InvariantCulture);
        Assert.Equal(expected, deadline.Date);
        Assert.Equal(expected.Offset, deadline.Date!.Value.Offset);
        Assert.Equal(type, deadline.Type);
        Assert.Equal(passed, deadline.IsPassed);
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(deadline, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var serializedDate = json.RootElement.GetProperty("date").GetDateTimeOffset();
        Assert.Equal(expected, serializedDate);
        Assert.Equal(expected.Offset, serializedDate.Offset);
        Assert.True(await rights.Can(instance, RoleAction.View, "Closed"));
        Assert.Equal(type == DeadlineType.Soft || !passed, await rights.Can(instance, RoleAction.Submit, "Closed"));
    }

    private static ModelService CreateModelWithForms() => new(new ModelParser(new DictionaryProvider(
        new Dictionary<string, string>
        {
            ["Common/Roles/Registered.yaml"] = """
                                               name: Registered
                                               actions:
                                                 - type: View
                                                   form: Available
                                                 - type: Edit
                                                   form: Available
                                                 - type: Submit
                                                   form: Available
                                               """,
            ["Hard/Entity.yaml"] = "name: Hard\ntitlePlural: Hard steps\nsteps: [Step, Open]",
            ["Hard/Steps/Step.yaml"] = """
                                       name: Step
                                       deadline:
                                         date: "=2000-01-01"
                                         type: Hard
                                       children: [Child]
                                       """,
            ["Hard/Steps/Child.yaml"] = """
                                        name: Child
                                        actions:
                                          - type: View
                                            form: Closed
                                            roles: [Registered]
                                          - type: Edit
                                            form: Closed
                                            roles: [Registered]
                                          - type: Submit
                                            form: Closed
                                            roles: [Registered]
                                        """,
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