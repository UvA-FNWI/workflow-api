using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using UvA.Workflow.Journaling;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.WorkflowInstances;

public class WorkflowInstanceServiceTests
{
    private readonly Mock<IWorkflowInstanceRepository> _repositoryMock = new();
    private readonly Mock<IInstanceJournalService> _journalServiceMock = new();
    private readonly Mock<IUserService> _userServiceMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly AnswerConversionService _answerConversionService;
    private readonly WorkflowInstanceService _workflowInstanceService;
    private readonly CancellationToken _ct = CancellationToken.None;

    public WorkflowInstanceServiceTests()
    {
        var modelService = new ModelService(UnitTestsHelpers.CreateModelParser());
        _answerConversionService = new AnswerConversionService(_userServiceMock.Object, _userRepoMock.Object);
        _workflowInstanceService =
            new WorkflowInstanceService(modelService, _repositoryMock.Object, _journalServiceMock.Object);

        _journalServiceMock
            .Setup(j => j.IncrementVersion(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _repositoryMock
            .Setup(r => r.UpdateFields(It.IsAny<string>(),
                It.IsAny<UpdateDefinition<WorkflowInstance>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private WorkflowInstance CreateProjectInstance() =>
        new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Upload")
            .Build();

    [Fact]
    public async Task UpdateProperty_ThrowsArgumentException_WhenPropertyDoesNotExistOnModel()
    {
        var instance = CreateProjectInstance();
        _repositoryMock.Setup(r => r.GetById(instance.Id, _ct)).ReturnsAsync(instance);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _workflowInstanceService.UpdateProperty(instance.Id, "NonExistentProperty", null, _answerConversionService,
                _ct));
    }

    [Fact]
    public async Task UpdateProperty_SetsNonArrayProperty_AndPersistsChanges()
    {
        var instance = CreateProjectInstance();
        _repositoryMock.Setup(r => r.GetById(instance.Id, _ct)).ReturnsAsync(instance);
        var value = JsonSerializer.SerializeToElement("Working Title");

        await _workflowInstanceService.UpdateProperty(instance.Id, "Title", value, _answerConversionService, _ct);

        Assert.Equal("Working Title", instance.Properties["Title"].AsString);
        _repositoryMock.Verify(r => r.UpdateFields(instance.Id,
            It.IsAny<UpdateDefinition<WorkflowInstance>>(), _ct), Times.Once);
        _journalServiceMock.Verify(j => j.IncrementVersion(instance.Id, _ct), Times.Once);
    }

    [Fact]
    public async Task UpdateProperty_AppendsToExistingArrayProperty()
    {
        var existingUser = new User
        {
            Id = ObjectId.GenerateNewId().ToString(), UserName = "user1", DisplayName = "User 1", Email = "u1@t.com"
        };
        _userServiceMock.Setup(s => s.GetUser("user1", _ct)).ReturnsAsync(existingUser);

        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Upload")
            .WithProperties(("PracticalSupervisor",
                _ => new BsonArray { InstanceUser.FromUser(existingUser).ToBsonDocument() }))
            .Build();
        _repositoryMock.Setup(r => r.GetById(instance.Id, _ct)).ReturnsAsync(instance);

        var newUser = new User
        {
            Id = ObjectId.GenerateNewId().ToString(), UserName = "user2", DisplayName = "User 2", Email = "u2@t.com"
        };
        _userServiceMock.Setup(s => s.GetUser("user2", _ct)).ReturnsAsync(newUser);
        var value = JsonSerializer.SerializeToElement(
            new { userName = "user2", displayName = "User 2", email = "u2@t.com" });

        await _workflowInstanceService.UpdateProperty(instance.Id, "PracticalSupervisor", value,
            _answerConversionService, _ct);

        Assert.Equal(2, instance.Properties["PracticalSupervisor"].AsBsonArray.Count);
        _repositoryMock.Verify(r => r.UpdateFields(instance.Id,
            It.IsAny<UpdateDefinition<WorkflowInstance>>(), _ct), Times.Once);
        _journalServiceMock.Verify(j => j.IncrementVersion(instance.Id, _ct), Times.Once);
    }

    [Fact]
    public async Task UpdateProperty_CreatesNewArrayProperty_WhenPropertyWasAbsent()
    {
        var user = new User
        {
            Id = ObjectId.GenerateNewId().ToString(), UserName = "user1", DisplayName = "User 1", Email = "u1@t.com"
        };
        _userServiceMock.Setup(s => s.GetUser("user1", _ct)).ReturnsAsync(user);

        var instance = CreateProjectInstance();
        _repositoryMock.Setup(r => r.GetById(instance.Id, _ct)).ReturnsAsync(instance);
        var value = JsonSerializer.SerializeToElement(
            new { userName = "user1", displayName = "User 1", email = "u1@t.com" });

        await _workflowInstanceService.UpdateProperty(instance.Id, "PracticalSupervisor", value,
            _answerConversionService, _ct);

        Assert.Single(instance.Properties["PracticalSupervisor"].AsBsonArray);
        _repositoryMock.Verify(r => r.UpdateFields(instance.Id,
            It.IsAny<UpdateDefinition<WorkflowInstance>>(), _ct), Times.Once);
        _journalServiceMock.Verify(j => j.IncrementVersion(instance.Id, _ct), Times.Once);
    }

    [Fact]
    public async Task RemovePropertyItemById_ThrowsArgumentException_WhenPropertyDoesNotExistOnModel()
    {
        var instance = CreateProjectInstance();
        _repositoryMock.Setup(r => r.GetById(instance.Id, _ct)).ReturnsAsync(instance);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _workflowInstanceService.RemovePropertyItemById(instance.Id, "NonExistentProperty", "some-id", _ct));
    }

    [Fact]
    public async Task RemovePropertyItemById_RemovesMatchingItem_FromArrayProperty()
    {
        var keepId = ObjectId.GenerateNewId();
        var removeId = ObjectId.GenerateNewId();

        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Upload")
            .WithProperties(("PracticalSupervisor", _ => new BsonArray
            {
                new BsonDocument { { "_id", keepId }, { "UserName", "user1" } },
                new BsonDocument { { "_id", removeId }, { "UserName", "user2" } }
            }))
            .Build();
        _repositoryMock.Setup(r => r.GetById(instance.Id, _ct)).ReturnsAsync(instance);

        await _workflowInstanceService.RemovePropertyItemById(instance.Id, "PracticalSupervisor", removeId.ToString(),
            _ct);

        var remaining = instance.Properties["PracticalSupervisor"].AsBsonArray;
        Assert.Single(remaining);
        Assert.Equal(keepId, remaining[0].AsBsonDocument["_id"].AsObjectId);
        _repositoryMock.Verify(r => r.UpdateFields(instance.Id,
            It.IsAny<UpdateDefinition<WorkflowInstance>>(), _ct), Times.Once);
        _journalServiceMock.Verify(j => j.IncrementVersion(instance.Id, _ct), Times.Once);
    }

    [Fact]
    public async Task RemovePropertyItemById_SetsPropertyToNull_WhenLastItemIsRemoved()
    {
        var itemId = ObjectId.GenerateNewId();

        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Upload")
            .WithProperties(("PracticalSupervisor", _ => new BsonArray
            {
                new BsonDocument { { "_id", itemId }, { "UserName", "user1" } }
            }))
            .Build();
        _repositoryMock.Setup(r => r.GetById(instance.Id, _ct)).ReturnsAsync(instance);

        await _workflowInstanceService.RemovePropertyItemById(instance.Id, "PracticalSupervisor", itemId.ToString(),
            _ct);

        Assert.Equal(BsonNull.Value, instance.Properties["PracticalSupervisor"]);
        _repositoryMock.Verify(r => r.UpdateFields(instance.Id,
            It.IsAny<UpdateDefinition<WorkflowInstance>>(), _ct), Times.Once);
        _journalServiceMock.Verify(j => j.IncrementVersion(instance.Id, _ct), Times.Once);
    }

    [Fact]
    public async Task RemovePropertyItemById_SkipsUpdateFields_WhenArrayPropertyAbsentOnInstance()
    {
        var instance = CreateProjectInstance(); // PracticalSupervisor not in Properties
        _repositoryMock.Setup(r => r.GetById(instance.Id, _ct)).ReturnsAsync(instance);

        await _workflowInstanceService.RemovePropertyItemById(instance.Id, "PracticalSupervisor", "some-id", _ct);

        _repositoryMock.Verify(r => r.UpdateFields(It.IsAny<string>(),
            It.IsAny<UpdateDefinition<WorkflowInstance>>(), It.IsAny<CancellationToken>()), Times.Never);
        _journalServiceMock.Verify(j => j.IncrementVersion(instance.Id, _ct), Times.Once);
    }

    [Fact]
    public async Task RemovePropertyItemById_UnsetsNonArrayProperty_AndPersistsChanges()
    {
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Upload")
            .WithProperties(("Title", b => b.Value("My Title")))
            .Build();
        _repositoryMock.Setup(r => r.GetById(instance.Id, _ct)).ReturnsAsync(instance);

        await _workflowInstanceService.RemovePropertyItemById(instance.Id, "Title", "irrelevant", _ct);

        Assert.False(instance.Properties.ContainsKey("Title"));
        _repositoryMock.Verify(r => r.UpdateFields(instance.Id,
            It.IsAny<UpdateDefinition<WorkflowInstance>>(), _ct), Times.Once);
        _journalServiceMock.Verify(j => j.IncrementVersion(instance.Id, _ct), Times.Once);
    }
}