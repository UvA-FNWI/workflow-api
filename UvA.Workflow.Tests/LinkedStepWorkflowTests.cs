using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using Moq;
using UvA.Workflow.Api.Assessments;
using UvA.Workflow.Api.Assessments.Dtos;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Assessments;
using UvA.Workflow.Events;
using UvA.Workflow.Jobs;
using UvA.Workflow.Journaling;
using UvA.Workflow.Notifications;
using UvA.Workflow.Persistence;
using UvA.Workflow.Submissions;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests;

public class LinkedStepWorkflowTests
{
    [Theory]
    [InlineData("LinkedStepsDemoShared", "AssessJoint")]
    [InlineData("LinkedStepsDemoIndividual", "AssessLocal")]
    public void DemoRubric_OffersNumericGradesForMultipleRequiredCriteria(string definition, string formName)
    {
        var model = new ModelService(UnitTestsHelpers.CreateModelParser());
        var instance = Instance(definition);
        instance.Properties["JointQuality"] = "Good";
        instance.Properties["LocalQuality"] = "Sufficient";
        instance.Properties["JointGrade"] = 8d;
        instance.Properties["LocalGrade"] = 7d;
        var properties = model.GetForm(instance, formName).Pages.SelectMany(page => page.Questions).ToArray();
        Assert.Equal(3, properties.Length);
        foreach (var property in properties)
        {
            var question = Assert.IsType<ChoiceQuestionDto>(
                QuestionDto.Create(property, 3));
            Assert.True(question.IsRequired);
            Assert.Equal(ChoiceControl.Rubric, question.Control);
            Assert.NotNull(question.Rubric);
            Assert.Equal(question.Choices.Select(choice => choice.Name),
                question.Rubric.SelectMany(entry => entry.Grades).Select(grade => grade.Name));
            Assert.All(question.Choices, choice =>
            {
                Assert.NotNull(choice.Value);
                Assert.InRange(choice.Value.Value, 1, 10);
                Assert.Equal(choice.Value.Value,
                    double.Parse(choice.Text.En, System.Globalization.CultureInfo.InvariantCulture));
            });
            Assert.Null(model.CreateContext(instance).Get(property.Name));
        }
    }

    [Theory]
    [InlineData("LinkedStepsDemoShared", "UploadDocument", new[] { "Title", "Student", "Supervisor" })]
    [InlineData("LinkedStepsDemoIndividual", "SharedUpload", new[] { "Student", "SharedThesis" })]
    public async Task Demo_CanBeConfiguredFromANewInstance(string definition, string nextStep, string[] fields)
    {
        var model = new ModelService(UnitTestsHelpers.CreateModelParser());
        var instance = Instance(definition);
        var repository = new Mock<IWorkflowInstanceRepository>();
        var users = new Mock<IUserService>();
        users.Setup(service => service.GetRolesOfCurrentUser()).ReturnsAsync(["Coordinator"]);
        var resolver = UnitTestsHelpers.CreateStateResolver(model, repository.Object);
        var rights = new RightsService(model, users.Object, repository.Object, resolver);
        var setup = model.GetForm(instance, "Setup");

        Assert.Equal(fields, setup.Pages.SelectMany(page => page.Questions).Select(question => question.Name));
        Assert.Equal("Setup", model.GetCurrentStep(instance, await resolver.Resolve(instance, default))?.Name);
        Assert.True(await rights.Can(instance, RoleAction.Submit, "Setup"));
        instance.Events["Setup"] = new InstanceEvent { Id = "Setup", Date = DateTime.UtcNow };
        Assert.Equal(nextStep, model.GetCurrentStep(instance, await resolver.Resolve(instance, default))?.Name);
    }

    [Fact]
    public void DemoAssessmentCard_UsesAnExistingAssessmentPart()
    {
        var model = new ModelService(UnitTestsHelpers.CreateModelParser());
        var shared = model.WorkflowDefinitions["LinkedStepsDemoShared"];
        var step = Assert.Single(shared.Steps, step => step.ResultsType == StepResultsType.AssessmentPartOverview);

        Assert.Contains(shared.AssessmentConfiguration!.Parts, part => part.Name == step.Name);
    }

    [Fact]
    public async Task Demo_CombinesSharedAndIndividualAssessmentsThroughCompletionRestartAndUndo()
    {
        var model = new ModelService(UnitTestsHelpers.CreateModelParser());
        var repository = new Mock<IWorkflowInstanceRepository>();
        var students = Enumerable.Range(1, 4).Select(i => new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserName = $"student-{i}",
            DisplayName = $"Student {i}"
        }).ToArray();
        var supervisor = new User { Id = ObjectId.GenerateNewId().ToString(), UserName = "supervisor" };
        var current = students[0];
        var users = new Mock<IUserService>();
        users.Setup(service => service.GetCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync(() => current);
        users.Setup(service => service.GetRolesOfCurrentUser()).ReturnsAsync(Array.Empty<string>());
        var source = Instance("LinkedStepsDemoShared");
        source.Events["Setup"] = new InstanceEvent { Id = "Setup", Date = source.CreatedOn };
        source.Properties["Student"] =
            new BsonArray(students.Select(student => InstanceUser.FromUser(student).ToBsonDocument()));
        source.Properties["Supervisor"] = InstanceUser.FromUser(supervisor).ToBsonDocument();
        // A deadline saved by the old demo must no longer restrict uploads.
        source.Properties["UploadDeadline"] = new BsonDateTime(DateTime.UtcNow.AddDays(-1));
        source.Properties["JointDocument"] = new BsonDocument
            { ["ArtifactId"] = "shared-file", ["Name"] = "joint.pdf", ["Length"] = 1 };
        var hosts = students.Select(student =>
        {
            var host = Instance("LinkedStepsDemoIndividual");
            host.Events["Setup"] = new InstanceEvent { Id = "Setup", Date = host.CreatedOn };
            host.Properties["Student"] = InstanceUser.FromUser(student).ToBsonDocument();
            host.Properties["SharedThesis"] = source.Id;
            return host;
        }).ToArray();
        repository.Setup(repo => repo.GetByIds(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([source]);
        repository.Setup(repo => repo.GetById(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) =>
                hosts.Append(source).SingleOrDefault(instance => instance.Id == id));
        repository.Setup(repo => repo.GetAllById(
                It.Is<string[]>(ids => ids.Contains(source.Id)), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            [
                new()
                {
                    ["_id"] = ObjectId.Parse(source.Id),
                    ["Supervisor"] = source.Properties["Supervisor"]
                }
            ]);
        var events = new Mock<IInstanceEventRepository>();
        var workflows = new WorkflowInstanceService(model, repository.Object, Mock.Of<IInstanceJournalService>(),
            events.Object, users.Object, Mock.Of<IUserRepository>());
        var assessments = new AssessmentService(model, workflows, repository.Object);
        var enricher = new WorkflowContextEnricher(repository.Object, model, assessments);
        var resolver = new WorkflowStateResolver(repository.Object, model, enricher);
        var rights = new RightsService(model, users.Object, repository.Object, resolver);
        var layouts = new Mock<IMailLayoutResolver>();
        layouts.Setup(service => service.Resolve(It.IsAny<string?>())).Returns(Mock.Of<IMailLayout>());
        var instances = new InstanceService(repository.Object, model, users.Object, rights,
            UnitTestsHelpers.CreateMailBuilder(layouts.Object), enricher, resolver);
        var logs = new List<InstanceEventLogEntry>();
        events.Setup(repo => repo.AddOrUpdateEvent(It.IsAny<WorkflowInstance>(), It.IsAny<InstanceEvent>(),
                It.IsAny<User>(), It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<OperationMetadata?>()))
            .Callback((WorkflowInstance instance, InstanceEvent evt, User actor, CancellationToken _,
                string? operationId, OperationMetadata? operation) =>
            {
                instance.Events[evt.Id] = evt;
                logs.Add(new InstanceEventLogEntry
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    WorkflowInstanceId = instance.Id,
                    EventId = evt.Id,
                    EventDate = evt.Date,
                    Operation = EventLogOperation.Create,
                    OperationId = operationId,
                    OperationMetadata = operation,
                    ExecutedBy = actor.Id
                });
            }).Returns(Task.CompletedTask);
        events.Setup(repo => repo.GetEventLogEntriesForInstance(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => logs.Where(log => log.WorkflowInstanceId == id).ToList());
        events.Setup(repo => repo.AddUndoEntry(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<User>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string id, string operationId, User actor, string reason, CancellationToken _) => logs.Add(
                new InstanceEventLogEntry
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    WorkflowInstanceId = id,
                    OperationId = operationId,
                    Operation = EventLogOperation.Undo,
                    Reason = reason,
                    ExecutedBy = actor.Id
                }))
            .Returns(Task.CompletedTask);
        var effects = new EffectService(instances, Mock.Of<IInstanceEventService>(), model, Mock.Of<IMailService>(),
            Mock.Of<IExternalUserService>(), Mock.Of<IArtifactService>(), Mock.Of<IMailLogRepository>(),
            Mock.Of<IConfiguration>(), events.Object, NullLogger<EffectService>.Instance);
        var jobsRepository = new Mock<IJobRepository>();
        var jobs = new JobService(effects, model, jobsRepository.Object, repository.Object, Mock.Of<IUserRepository>(),
            NullLogger<JobService>.Instance, instances, Options.Create(new WorkerOptions { WorkerGroup = "test" }));
        var submissions = new SubmissionService(model, instances, Mock.Of<IInstanceJournalService>(), jobs, effects);
        var results = new AssessmentsController(workflows, users.Object, repository.Object, assessments, instances,
            new AssessmentDtoFactory(new ArtifactTokenService(UnitTestsHelpers.TestS3Config), model, assessments),
            rights);

        var upload = model.GetForm(source, "UploadJoint");
        Assert.True(await rights.Can(source, RoleAction.Submit, upload.Name));
        var submitted = await submissions.SubmitSubmission(new SubmissionContext(source,
            FormSubmissionState.Resolve(source, upload, model.WorkflowDefinitions[source.WorkflowDefinition]),
            upload, upload.Name), current, default);
        Assert.True(submitted.Success);
        foreach (var host in hosts)
            Assert.Equal("SharedUpload", (await resolver.Resolve(host, default)).CurrentStep);
        Assert.Equal("ConfirmUpload", model.GetCurrentStep(source)?.Name);

        await Submit(source, "ConfirmJointUpload");
        foreach (var host in hosts)
        {
            var context = await resolver.Resolve(host, default);
            Assert.Equal("SharedAssessment", model.GetCurrentStep(host, context)?.Name);
            Assert.Contains("IndividualWork", model.GetActiveSteps(host, context));
            Assert.False(host.Properties.ContainsKey("JointDocument"));
            Assert.False(host.Events.ContainsKey("UploadJoint"));
        }

        Assert.Equal(2, logs.Count);
        Assert.All(logs, log => Assert.Equal(source.Id, log.WorkflowInstanceId));
        Assert.Equal("Student 1", model.WorkflowDefinitions[hosts[0].WorkflowDefinition].InstanceTitleTemplate!
            .Apply(await resolver.Resolve(hosts[0], default)));

        current = supervisor;
        var confirmationOperation = logs
            .Single(log => log.WorkflowInstanceId == source.Id && log.EventId == "ConfirmJointUpload")
            .OperationId!;
        await new UndoService(events.Object, jobsRepository.Object, repository.Object, instances, rights, model)
            .Undo(source, confirmationOperation, "Recheck joint upload confirmation", supervisor, default);
        var reopenedContexts = await resolver.Resolve(hosts, default);
        Assert.All(hosts,
            host => Assert.Equal("SharedUpload", model.GetCurrentStep(host, reopenedContexts[host.Id])?.Name));
        current = students[0];
        await Submit(source, "ConfirmJointUpload");
        Assert.False(await rights.Can(hosts[1], RoleAction.Submit, "UploadLocal"));

        var localGrades = new[]
        {
            new[] { "6", "7", "8" },
            new[] { "8", "9", "10" },
            new[] { "5,5", "6,5", "7,5" },
            new[] { "3", "4", "5" }
        };
        for (var i = 0; i < hosts.Length; i++)
        {
            var host = hosts[i];
            current = students[i];
            host.Properties["LocalDocument"] = new BsonDocument
                { ["ArtifactId"] = $"local-{i}", ["Name"] = "analysis.pdf", ["Length"] = 1 };
            await Submit(host, "UploadLocal");
            current = supervisor;
            for (var criterion = 0; criterion < 3; criterion++)
                host.Properties[$"LocalCriterion{criterion + 1}"] = localGrades[i][criterion];
            await Submit(host, "AssessLocal");
            Assert.Null((await ReadResults(host)).FinalGrade);
        }

        current = supervisor;
        source.Properties["JointCriterion1"] = "7";
        source.Properties["JointCriterion2"] = "8";
        source.Properties["JointCriterion3"] = "9";
        var assessment = model.GetForm(source, "AssessJoint");
        Assert.True(await rights.Can(source, RoleAction.Submit, assessment.Name));
        Assert.True((await submissions.SubmitSubmission(new SubmissionContext(source,
            FormSubmissionState.Resolve(source, assessment, model.WorkflowDefinitions[source.WorkflowDefinition]),
            assessment, assessment.Name), current, default)).Success);
        var expectedGrades = new[] { 7.4f, 8.6f, 7.1f, 5.6f };
        for (var i = 0; i < hosts.Length; i++)
        {
            current = students[i];
            var dto = await ReadResults(hosts[i]);
            Assert.Equal(expectedGrades[i], dto.FinalGrade!.Rounded);
            Assert.Equal(8m, dto.Parts[0].Combined!.WeightedAverage);
            Assert.Equal(new decimal[] { 7, 9, 6.5m, 4 }[i], dto.Parts[1].Combined!.WeightedAverage);
            Assert.Equal(new[] { "SharedAssessment", "IndividualWork" }, dto.Parts.Select(part => part.Id));
            Assert.Equal(new decimal?[] { 40, 60 }, dto.Parts.Select(part => part.Percentage));
            Assert.False(hosts[i].Properties.ContainsKey("JointCriterion1"));
            Assert.True(await rights.Can(hosts[i], RoleAction.Submit, "Finalise"));
        }

        current = students[0];
        await Submit(hosts[0], "Finalise");
        Assert.Null(model.GetCurrentStep(hosts[0], await resolver.Resolve(hosts[0], default)));

        current = supervisor;
        await effects.AddEvent(hosts[0], "RestartIndividual", supervisor, default);
        var contexts = await resolver.Resolve(hosts, default);
        Assert.Equal("IndividualWork", model.GetCurrentStep(hosts[0], contexts[hosts[0].Id])?.Name);
        Assert.All(hosts.Skip(1),
            host => Assert.Equal("Finalise", model.GetCurrentStep(host, contexts[host.Id])?.Name));
        Assert.True(FormSubmissionState.Resolve(source, assessment,
            model.WorkflowDefinitions[source.WorkflowDefinition]).IsSubmitted);
        Assert.Null((await ReadResults(hosts[0])).FinalGrade);
        Assert.Equal(8.6f, (await ReadResults(hosts[1])).FinalGrade!.Rounded);

        current = students[0];
        await Submit(hosts[0], "UploadLocal");
        current = supervisor;
        hosts[0].Properties["LocalCriterion1"] = "8";
        hosts[0].Properties["LocalCriterion2"] = "8,5";
        hosts[0].Properties["LocalCriterion3"] = "9";
        await Submit(hosts[0], "AssessLocal");
        Assert.Equal(8.3f, (await ReadResults(hosts[0])).FinalGrade!.Rounded);
        current = students[0];
        Assert.True(await rights.Can(hosts[0], RoleAction.Submit, "Finalise"));

        current = supervisor;
        var operation = logs.Single(log => log.WorkflowInstanceId == source.Id && log.EventId == "AssessJoint")
            .OperationId!;
        await new UndoService(events.Object, jobsRepository.Object, repository.Object, instances, rights, model)
            .Undo(source, operation, "Recheck joint assessment", supervisor, default);
        contexts = await resolver.Resolve(hosts, default);
        Assert.All(hosts,
            host => Assert.Equal("SharedAssessment", model.GetCurrentStep(host, contexts[host.Id])?.Name));
        Assert.True(hosts[1].Events.ContainsKey("AssessLocal"));
        foreach (var host in hosts)
            Assert.Null((await ReadResults(host)).FinalGrade);

        async Task Submit(WorkflowInstance instance, string name)
        {
            var form = model.GetForm(instance, name);
            Assert.True(await rights.Can(instance, RoleAction.Submit, name));
            var result = await submissions.SubmitSubmission(new SubmissionContext(instance,
                FormSubmissionState.Resolve(instance, form, model.WorkflowDefinitions[instance.WorkflowDefinition]),
                form, name), current, default);
            Assert.True(result.Success, string.Join(", ", result.Errors.Select(error => error.QuestionName)));
        }

        async Task<AssessmentDto> ReadResults(WorkflowInstance instance)
        {
            var response = await results.GetAssessmentResults(instance.Id, default);
            return Assert.IsType<AssessmentDto>(Assert.IsType<OkObjectResult>(response.Result).Value);
        }
    }

    private static WorkflowInstance Instance(string definition) => new()
    {
        Id = ObjectId.GenerateNewId().ToString(),
        WorkflowDefinition = definition,
        CreatedOn = DateTime.UtcNow.AddDays(-3),
        Properties = [],
        Events = []
    };
}