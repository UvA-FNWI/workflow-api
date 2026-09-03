using System.Linq.Expressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using UvA.Workflow.Organizations;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.Tests.Helpers;

namespace UvA.Workflow.Tests.Users;

public class UserServiceSyncTests
{
    private static readonly Expression<Func<InstanceUser, object>>[] AllFields = [u => u.DisplayName, u => u.Email];
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
            new ModelService(UnitTestsHelpers.CreateModelParser()),
            Mock.Of<ILogger<UserService>>());

    private static User MakeUser(string? id = null, string displayName = "Jane Doe",
        string email = "jane@invalid.invalid")
        => new()
        {
            Id = id ?? ObjectId.GenerateNewId().ToString(),
            UserName = "jdoe",
            DisplayName = displayName,
            Email = email
        };

    private static BsonDocument RenderUpdate(UpdateDefinition<WorkflowInstance> update)
    {
        var registry = BsonSerializer.SerializerRegistry;
        var serializer = registry.GetSerializer<WorkflowInstance>();
        return update.Render(new RenderArgs<WorkflowInstance>(serializer, registry)).AsBsonDocument;
    }

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

        instanceRepo.Verify(r => r.UpdateFields(It.IsAny<string>(), It.IsAny<UpdateDefinition<WorkflowInstance>>(), Ct),
            Times.Never);
        instanceRepo.Verify(
            r => r.UpdateArrayFields(It.IsAny<string>(), It.IsAny<UpdateDefinition<WorkflowInstance>>(),
                It.IsAny<IEnumerable<ArrayFilterDefinition>>(), Ct), Times.Never);
    }

    [Fact]
    public async Task SyncUserInInstances_ScalarUserPropertyMatches_CallsUpdateFieldsWithCorrectValues()
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

        UpdateDefinition<WorkflowInstance>? capturedUpdate = null;
        var instanceRepo = new Mock<IWorkflowInstanceRepository>();
        instanceRepo
            .Setup(r => r.GetByFilter(It.IsAny<FilterDefinition<WorkflowInstance>>(), Ct))
            .ReturnsAsync([instance]);
        instanceRepo
            .Setup(r => r.UpdateFields(instance.Id, It.IsAny<UpdateDefinition<WorkflowInstance>>(), Ct))
            .Callback<string, UpdateDefinition<WorkflowInstance>, CancellationToken>((_, update, _) =>
                capturedUpdate = update)
            .Returns(Task.CompletedTask);

        var service = CreateService(instanceRepo);
        await service.SyncUserInInstances(user, AllFields, Ct);

        instanceRepo.Verify(r => r.UpdateFields(instance.Id, It.IsAny<UpdateDefinition<WorkflowInstance>>(), Ct),
            Times.Once);

        var rendered = RenderUpdate(capturedUpdate!);
        Assert.Equal("Jane Doe", rendered["$set"]["Properties.Student.DisplayName"].AsString);
        Assert.Equal("jane@invalid.invalid", rendered["$set"]["Properties.Student.Email"].AsString);
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

        instanceRepo.Verify(r => r.UpdateFields(It.IsAny<string>(), It.IsAny<UpdateDefinition<WorkflowInstance>>(), Ct),
            Times.Never);
    }

    [Fact]
    public async Task SyncUserInInstances_ArrayUserPropertyContainsUser_CallsUpdateArrayFieldsForMatchingElement()
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

        UpdateDefinition<WorkflowInstance>? capturedUpdate = null;
        var instanceRepo = new Mock<IWorkflowInstanceRepository>();
        instanceRepo
            .Setup(r => r.GetByFilter(It.IsAny<FilterDefinition<WorkflowInstance>>(), Ct))
            .ReturnsAsync([instance]);
        instanceRepo
            .Setup(r => r.UpdateArrayFields(instance.Id, It.IsAny<UpdateDefinition<WorkflowInstance>>(),
                It.IsAny<IEnumerable<ArrayFilterDefinition>>(), Ct))
            .Callback<string, UpdateDefinition<WorkflowInstance>, IEnumerable<ArrayFilterDefinition>,
                CancellationToken>((_, update, _, _) => capturedUpdate = update)
            .Returns(Task.CompletedTask);

        var service = CreateService(instanceRepo);
        await service.SyncUserInInstances(user, AllFields, Ct);

        instanceRepo.Verify(
            r => r.UpdateArrayFields(instance.Id, It.IsAny<UpdateDefinition<WorkflowInstance>>(),
                It.IsAny<IEnumerable<ArrayFilterDefinition>>(), Ct), Times.Once);
        instanceRepo.Verify(r => r.UpdateFields(It.IsAny<string>(), It.IsAny<UpdateDefinition<WorkflowInstance>>(), Ct),
            Times.Never);

        var rendered = RenderUpdate(capturedUpdate!);
        Assert.Equal("Jane Doe", rendered["$set"]["Properties.PracticalSupervisor.$[elem].DisplayName"].AsString);
        Assert.Equal("jane@invalid.invalid", rendered["$set"]["Properties.PracticalSupervisor.$[elem].Email"].AsString);
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

        instanceRepo.Verify(
            r => r.UpdateArrayFields(It.IsAny<string>(), It.IsAny<UpdateDefinition<WorkflowInstance>>(),
                It.IsAny<IEnumerable<ArrayFilterDefinition>>(), Ct), Times.Never);
    }

    [Fact]
    public async Task SyncUserInInstances_OnlySpecifiedFieldsAreUpdated()
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

        UpdateDefinition<WorkflowInstance>? capturedUpdate = null;
        var instanceRepo = new Mock<IWorkflowInstanceRepository>();
        instanceRepo
            .Setup(r => r.GetByFilter(It.IsAny<FilterDefinition<WorkflowInstance>>(), Ct))
            .ReturnsAsync([instance]);
        instanceRepo
            .Setup(r => r.UpdateFields(instance.Id, It.IsAny<UpdateDefinition<WorkflowInstance>>(), Ct))
            .Callback<string, UpdateDefinition<WorkflowInstance>, CancellationToken>((_, update, _) =>
                capturedUpdate = update)
            .Returns(Task.CompletedTask);

        var service = CreateService(instanceRepo);

        // Only sync DisplayName, not Email
        await service.SyncUserInInstances(user, [u => u.DisplayName!], Ct);

        var rendered = RenderUpdate(capturedUpdate!);
        var setFields = rendered["$set"].AsBsonDocument;
        Assert.True(setFields.Contains("Properties.Student.DisplayName"));
        Assert.False(setFields.Contains("Properties.Student.Email")); // not requested, must be absent
    }
}