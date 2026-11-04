using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using Moq;
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
    [InlineData("LinkedStepsDemoShared", "Upload", new[] { "Title", "Student", "Supervisor", "UploadDeadline" })]
    [InlineData("LinkedStepsDemoIndividual", "Start", new[] { "Title", "Student", "Supervisor", "SharedThesis" })]
    public async Task Demo_CanBeConfiguredFromANewInstance(string definition, string nextStep, string[] fields)
    {
        var model = new ModelService(UnitTestsHelpers.CreateModelParser());
        var instance = Instance(definition);
        var repository = new Mock<IWorkflowInstanceRepository>();
        var users = new Mock<IUserService>();
        users.Setup(service => service.GetRolesOfCurrentUser()).ReturnsAsync(["Coordinator"]);
        var resolver = new WorkflowStateResolver(repository.Object, model,
            new WorkflowContextEnricher(repository.Object, model, Mock.Of<IAssessmentService>()));
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
    public async Task SharedSubmissionAndAssessmentAreOwnedOnceWhileLocalResitsStayIndividual()
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
        source.Properties["UploadDeadline"] = new BsonDateTime(DateTime.UtcNow.AddDays(30));
        source.Properties["JointDocument"] = new BsonDocument
            { ["ArtifactId"] = "shared-file", ["Name"] = "joint.pdf", ["Length"] = 1 };
        var hosts = students.Select(student =>
        {
            var host = Instance("LinkedStepsDemoIndividual");
            host.Events["Setup"] = new InstanceEvent { Id = "Setup", Date = host.CreatedOn };
            host.Properties["Student"] = InstanceUser.FromUser(student).ToBsonDocument();
            host.Properties["Supervisor"] = InstanceUser.FromUser(supervisor).ToBsonDocument();
            host.Properties["SharedThesis"] = source.Id;
            host.Events["Start"] = new InstanceEvent { Id = "Start", Date = DateTime.UtcNow.AddDays(-2) };
            return host;
        }).ToArray();
        repository.Setup(repo => repo.GetByIds(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([source]);
        var enricher = new WorkflowContextEnricher(repository.Object, model, Mock.Of<IAssessmentService>());
        var resolver = new WorkflowStateResolver(repository.Object, model, enricher);
        var rights = new RightsService(model, users.Object, repository.Object, resolver);
        var layouts = new Mock<IMailLayoutResolver>();
        layouts.Setup(service => service.Resolve(It.IsAny<string?>())).Returns(Mock.Of<IMailLayout>());
        var instances = new InstanceService(repository.Object, model, users.Object, rights,
            UnitTestsHelpers.CreateMailBuilder(layouts.Object), enricher, resolver);
        var logs = new List<InstanceEventLogEntry>();
        var events = new Mock<IInstanceEventRepository>();
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

        var upload = model.GetForm(source, "UploadJoint");
        Assert.True(await rights.Can(source, RoleAction.Submit, upload.Name));
        var submitted = await submissions.SubmitSubmission(new SubmissionContext(source,
            FormSubmissionState.Resolve(source, upload, model.WorkflowDefinitions[source.WorkflowDefinition]),
            upload, upload.Name), current, default);
        Assert.True(submitted.Success);
        foreach (var host in hosts)
        {
            var context = await resolver.Resolve(host, default);
            Assert.Equal("SharedAssessment", model.GetCurrentStep(host, context)?.Name);
            Assert.Contains("IndividualWork", model.GetActiveSteps(host, context));
            Assert.False(host.Properties.ContainsKey("JointDocument"));
            Assert.False(host.Events.ContainsKey("UploadJoint"));
        }

        Assert.Single(logs);
        Assert.Equal(source.Id, logs[0].WorkflowInstanceId);

        current = supervisor;
        source.Properties["JointQuality"] = "Good";
        var assessment = model.GetForm(source, "AssessJoint");
        Assert.True(await rights.Can(source, RoleAction.Submit, assessment.Name));
        Assert.True((await submissions.SubmitSubmission(new SubmissionContext(source,
            FormSubmissionState.Resolve(source, assessment, model.WorkflowDefinitions[source.WorkflowDefinition]),
            assessment, assessment.Name), current, default)).Success);
        foreach (var host in hosts)
            host.Events["AssessLocal"] = new InstanceEvent { Id = "AssessLocal", Date = DateTime.UtcNow.AddDays(-1) };
        await effects.AddEvent(hosts[0], "RestartIndividual", supervisor, default);
        var contexts = await resolver.Resolve(hosts, default);
        Assert.Equal("IndividualWork", model.GetCurrentStep(hosts[0], contexts[hosts[0].Id])?.Name);
        Assert.All(hosts.Skip(1),
            host => Assert.Equal("Finalise", model.GetCurrentStep(host, contexts[host.Id])?.Name));
        Assert.True(FormSubmissionState.Resolve(source, assessment,
            model.WorkflowDefinitions[source.WorkflowDefinition]).IsSubmitted);

        var operation = logs.Single(log => log.WorkflowInstanceId == source.Id && log.EventId == "AssessJoint")
            .OperationId!;
        await new UndoService(events.Object, jobsRepository.Object, repository.Object, instances, rights, model)
            .Undo(source, operation, "Recheck joint assessment", supervisor, default);
        contexts = await resolver.Resolve(hosts, default);
        Assert.All(hosts,
            host => Assert.Equal("SharedAssessment", model.GetCurrentStep(host, contexts[host.Id])?.Name));
        Assert.True(hosts[1].Events.ContainsKey("AssessLocal"));
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