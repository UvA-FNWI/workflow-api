using Microsoft.Extensions.Caching.Memory;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using UvA.Workflow.Organizations;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.Tests.Helpers;

namespace UvA.Workflow.Tests.Users;

public class UserServiceSyncTests
{
    private static readonly string[] AllFields = ["DisplayName", "Email"];
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static UserService CreateService(
        Mock<IWorkflowInstanceRepository> instanceRepoMock,
        Mock<IUserRepository>? userRepoMock = null)
        => new(
            Mock.Of<ICurrentUserAccessor>(),
            (userRepoMock ?? new Mock<IUserRepository>()).Object,
            Mock.Of<IOrganizationService>(),
            new MemoryCache(new MemoryCacheOptions()),
            [],
            [],
            instanceRepoMock.Object,
            new ModelService(UnitTestsHelpers.CreateModelParser()));

    private static User MakeUser(string? id = null, string displayName = "Jane Doe",
        string email = "jane@invalid.invalid")
        => new()
        {
            Id = id ?? ObjectId.GenerateNewId().ToString(),
            UserName = "jdoe",
            DisplayName = displayName,
            Email = email
        };


    [Fact]
    public async Task SyncUserInInstances_UserWithInvalidId_DoesNotQueryRepository()
    {
        var instanceRepo = new Mock<IWorkflowInstanceRepository>();
        var service = CreateService(instanceRepo);
        var user = MakeUser(id: "not-an-object-id");

        await service.SyncUserInInstances(user, AllFields, Ct);

        instanceRepo.Verify(
            r => r.GetByFilter(It.IsAny<FilterDefinition<WorkflowInstance>>(), Ct),
            Times.Never);
    }

    [Fact]
    public async Task SyncUserInInstances_NoMatchingInstances_DoesNotCallUpdate()
    {
        var instanceRepo = new Mock<IWorkflowInstanceRepository>();
        instanceRepo
            .Setup(r => r.GetByFilter(It.IsAny<FilterDefinition<WorkflowInstance>>(), Ct))
            .ReturnsAsync([]);

        var service = CreateService(instanceRepo);
        var user = MakeUser();

        await service.SyncUserInInstances(user, AllFields, Ct);

        instanceRepo.Verify(r => r.Update(It.IsAny<WorkflowInstance>(), Ct), Times.Never);
    }

    [Fact]
    public async Task SyncUserInInstances_ScalarUserPropertyMatches_UpdatesFieldsAndCallsUpdate()
    {
        var user = MakeUser();

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("open")
            .WithProperties(
                ("Student", pb => pb.Person(
                    objectId: user.Id,
                    displayName: "Old Name",
                    email: "old@uva.nl",
                    userName: "jdoe")))
            .Build();

        var instanceRepo = new Mock<IWorkflowInstanceRepository>();
        instanceRepo
            .Setup(r => r.GetByFilter(It.IsAny<FilterDefinition<WorkflowInstance>>(), Ct))
            .ReturnsAsync([instance]);

        var service = CreateService(instanceRepo);

        await service.SyncUserInInstances(user, AllFields, Ct);

        var updated = instance.Properties["Student"].AsBsonDocument;
        Assert.Equal("Jane Doe", updated["DisplayName"].AsString);
        Assert.Equal("jane@invalid.invalid", updated["Email"].AsString);
        instanceRepo.Verify(r => r.Update(instance, Ct), Times.Once);
    }

    [Fact]
    public async Task SyncUserInInstances_ScalarPropertyBelongsToDifferentUser_DoesNotUpdate()
    {
        var user = MakeUser();
        var otherUserId = ObjectId.GenerateNewId().ToString();

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("open")
            .WithProperties(
                ("Student", pb => pb.Person(objectId: otherUserId)))
            .Build();

        var instanceRepo = new Mock<IWorkflowInstanceRepository>();
        instanceRepo
            .Setup(r => r.GetByFilter(It.IsAny<FilterDefinition<WorkflowInstance>>(), Ct))
            .ReturnsAsync([instance]);

        var service = CreateService(instanceRepo);

        await service.SyncUserInInstances(user, AllFields, Ct);

        instanceRepo.Verify(r => r.Update(It.IsAny<WorkflowInstance>(), Ct), Times.Never);
    }

    [Fact]
    public async Task SyncUserInInstances_ArrayUserPropertyContainsUser_UpdatesOnlyMatchingElement()
    {
        var user = MakeUser();
        var otherUserId = ObjectId.GenerateNewId().ToString();

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("open")
            .WithProperties(
                ("PracticalSupervisor", pb => pb.Array(
                    b => b.Person(objectId: user.Id, displayName: "Old Name", email: "old@uva.nl", userName: "jdoe"),
                    b => b.Person(objectId: otherUserId, displayName: "Other Person", email: "other@uva.nl",
                        userName: "other"))))
            .Build();

        var instanceRepo = new Mock<IWorkflowInstanceRepository>();
        instanceRepo
            .Setup(r => r.GetByFilter(It.IsAny<FilterDefinition<WorkflowInstance>>(), Ct))
            .ReturnsAsync([instance]);

        var service = CreateService(instanceRepo);

        await service.SyncUserInInstances(user, AllFields, Ct);

        var array = instance.Properties["PracticalSupervisor"].AsBsonArray;
        var matchedElem = array.First(e => e.AsBsonDocument["_id"] == ObjectId.Parse(user.Id)).AsBsonDocument;
        var otherElem = array.First(e => e.AsBsonDocument["_id"] == ObjectId.Parse(otherUserId)).AsBsonDocument;

        Assert.Equal("Jane Doe", matchedElem["DisplayName"].AsString);
        Assert.Equal("jane@invalid.invalid", matchedElem["Email"].AsString);
        Assert.Equal("Other Person", otherElem["DisplayName"].AsString); // unchanged
        instanceRepo.Verify(r => r.Update(instance, Ct), Times.Once);
    }

    [Fact]
    public async Task SyncUserInInstances_ArrayPropertyDoesNotContainUser_DoesNotUpdate()
    {
        var user = MakeUser();
        var otherUserId = ObjectId.GenerateNewId().ToString();

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("open")
            .WithProperties(
                ("PracticalSupervisor", pb => pb.Array(b => b.Person(objectId: otherUserId))))
            .Build();

        var instanceRepo = new Mock<IWorkflowInstanceRepository>();
        instanceRepo
            .Setup(r => r.GetByFilter(It.IsAny<FilterDefinition<WorkflowInstance>>(), Ct))
            .ReturnsAsync([instance]);

        var service = CreateService(instanceRepo);

        await service.SyncUserInInstances(user, AllFields, Ct);

        instanceRepo.Verify(r => r.Update(It.IsAny<WorkflowInstance>(), Ct), Times.Never);
    }

    [Fact]
    public async Task SyncUserInInstances_OnlySpecifiedFieldsAreOverwritten()
    {
        var user = MakeUser();

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("open")
            .WithProperties(
                ("Student", pb => pb.Person(
                    objectId: user.Id,
                    displayName: "Old Name",
                    email: "old@uva.nl",
                    userName: "original-username")))
            .Build();

        var instanceRepo = new Mock<IWorkflowInstanceRepository>();
        instanceRepo
            .Setup(r => r.GetByFilter(It.IsAny<FilterDefinition<WorkflowInstance>>(), Ct))
            .ReturnsAsync([instance]);

        var service = CreateService(instanceRepo);

        // Only sync DisplayName, not Email
        await service.SyncUserInInstances(user, ["DisplayName"], Ct);

        var updated = instance.Properties["Student"].AsBsonDocument;
        Assert.Equal("Jane Doe", updated["DisplayName"].AsString);
        Assert.Equal("old@uva.nl", updated["Email"].AsString); // not in requested fields, unchanged
    }
}