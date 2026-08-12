using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using UvA.Workflow.Events;
using UvA.Workflow.Journaling;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.WorkflowInstances;

public class WorkflowInstanceServiceTests
{
    private readonly Mock<IWorkflowInstanceRepository> _repository = new();
    private readonly Mock<IInstanceJournalService> _journal = new();
    private readonly WorkflowInstanceService _service;
    private readonly CancellationToken _ct = CancellationToken.None;

    public WorkflowInstanceServiceTests()
    {
        var userService = new Mock<IUserService>();
        userService.Setup(service => service.GetCurrentUser(_ct)).ReturnsAsync(new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserName = "testuser",
            DisplayName = "Test User",
            Email = "test@example.com"
        });
        _repository.Setup(repository => repository.UpdateFields(
                It.IsAny<string>(), It.IsAny<UpdateDefinition<WorkflowInstance>>(), _ct))
            .Returns(Task.CompletedTask);

        _journal.Setup(journal => journal.LogPropertyChange(
                It.IsAny<string>(), It.IsAny<PropertyChangeEntry>(), _ct))
            .ReturnsAsync(false);

        var modelService = new ModelService(UnitTestsHelpers.CreateModelParser());
        _service = new WorkflowInstanceService(modelService, _repository.Object, _journal.Object,
            Mock.Of<IInstanceEventRepository>(),
            userService.Object);
    }

    [Fact]
    public async Task GetAsOfVersion_RevertsNestedValueThatWasUnset()
    {
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project-PP", currentStep: "AssessmentSupervisor")
            .WithProperties(("AssessmentSupervisor", _ => new BsonDocument("ProblemStatement", "8")))
            .Build();
        var change = PropertyChangeEntry.Create("AssessmentSupervisor.ProblemStatement", null, new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserName = "testuser"
        });
        change.Version = 2;

        _repository.Setup(repository => repository.GetById(instance.Id, _ct)).ReturnsAsync(instance);
        _journal.Setup(journal => journal.GetInstanceJournal(instance.Id, false, _ct))
            .ReturnsAsync(new InstanceJournalEntry { InstanceId = instance.Id, PropertyChanges = [change] });

        var result = await _service.GetAsOfVersion(instance.Id, 1, _ct);

        Assert.Null(result.GetProperty("AssessmentSupervisor", "ProblemStatement"));
    }

    [Fact]
    public async Task AppendPropertyValue_AppendsAndJournals()
    {
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Upload")
            .WithProperties(("PracticalSupervisor", _ => new BsonArray
            {
                new BsonDocument("_id", ObjectId.GenerateNewId())
            }))
            .Build();
        var newUser = new BsonDocument("_id", ObjectId.GenerateNewId());

        await _service.AppendPropertyValue(instance, ["PracticalSupervisor"], newUser, _ct);

        Assert.Equal(2, instance.Properties["PracticalSupervisor"].AsBsonArray.Count);
        _repository.Verify(repository => repository.UpdateFields(
            instance.Id, It.IsAny<UpdateDefinition<WorkflowInstance>>(), _ct), Times.Once);
        _journal.Verify(journal => journal.LogPropertyChange(
            instance.Id, It.Is<PropertyChangeEntry>(entry =>
                entry.Path == "PracticalSupervisor" && entry.OldValue!.AsBsonArray.Count == 1), _ct), Times.Once);
    }

    [Fact]
    public async Task AppendPropertyValue_CreatesMissingArray()
    {
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Upload")
            .Build();
        var newUser = new BsonDocument("_id", ObjectId.GenerateNewId());

        await _service.AppendPropertyValue(instance, ["PracticalSupervisor"], newUser, _ct);

        Assert.Equal(newUser, Assert.Single(instance.Properties["PracticalSupervisor"].AsBsonArray));
    }
}