using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Moq;
using UvA.Workflow.Api.Users;
using UvA.Workflow.Notifications;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Users;

public class ExternalUserEmailUpdateServiceTests
{
    private static ExternalUserEmailUpdateService CreateService(WorkflowDefinition workflowDefinition)
    {
        var contentProvider = new TestContentProvider();
        var parser = new ModelParser(contentProvider);
        var modelService = new ModelService(parser);
        modelService.WorkflowDefinitions[workflowDefinition.Name] = workflowDefinition;
        return new ExternalUserEmailUpdateService(null!, null!, modelService, null!);
    }

    private static WorkflowDefinition CreateWorkflowDefinition(
        List<PropertyDefinition> properties,
        List<Form>? forms = null)
        => new()
        {
            Name = "TestWorkflow",
            Properties = properties,
            Forms = forms ?? []
        };

    private static User CreateUser(string userId)
        => new() { Id = userId, UserName = "user-" + userId, Email = "user@test.invalid" };

    private static ExternalUserEmailUpdateService CreateServiceForUpdate(WorkflowDefinition workflowDefinition)
    {
        var repoMock = new Mock<IWorkflowInstanceRepository>();
        repoMock
            .Setup(r => r.UpdateFields(
                It.IsAny<string>(),
                It.IsAny<MongoDB.Driver.UpdateDefinition<WorkflowInstance>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var contentProvider = new TestContentProvider();
        var modelService = new ModelService(new ModelParser(contentProvider));
        modelService.WorkflowDefinitions[workflowDefinition.Name] = workflowDefinition;

        var userServiceMock = new Mock<IUserService>();
        var rightsService = new RightsService(modelService, userServiceMock.Object, repoMock.Object);
        var mailBuilder = UnitTestsHelpers.CreateMailBuilder(
            new Mock<IMailLayoutResolver>().Object, new Mock<IConfiguration>().Object);
        var instanceService = new InstanceService(
            repoMock.Object, modelService, userServiceMock.Object, rightsService, mailBuilder);

        return new ExternalUserEmailUpdateService(null!, null!, modelService, instanceService);
    }

    private static User CreateUpdatedUser(string userId) => new()
    {
        Id = userId,
        UserName = "new-username",
        DisplayName = "New Name",
        Email = "new@test.invalid",
        ProviderKey = "eduid"
    };

    [Fact]
    public async Task GetMatchingInstanceOnlyProperties_NonUserProperty_ReturnsUserNotInAnswer()
    {
        var userId = ObjectId.GenerateNewId().ToString();
        var propDef = new PropertyDefinition { Name = "Title", Type = "String" };
        var workflowDef = CreateWorkflowDefinition([propDef]);

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("TestWorkflow")
            .WithCurrentStep("Start")
            .WithProperties(("Title", b => b.Value("some value")))
            .Build();

        var service = CreateService(workflowDef);

        var result = await service.PrepareAnswerReferenceUpdate(instance, CreateUser(userId), CancellationToken.None);

        Assert.Equal(ExternalUserEmailAnswerUpdateResult.UserNotInAnswer, result.Result);
        Assert.Empty(result.EditableProperties);
    }

    [Fact]
    public async Task GetMatchingInstanceOnlyProperties_UserPropertyInForm_ReturnsUserNotInAnswer()
    {
        var userId = ObjectId.GenerateNewId().ToString();
        var propDef = new PropertyDefinition { Name = "Supervisor", Type = "User" };
        var form = new Form
        {
            Name = "SupervisorForm",
            PropertyName = "SupervisorForm",
            Pages = [new Page { Name = "Page1", Fields = [propDef] }]
        };
        var workflowDef = CreateWorkflowDefinition([propDef], [form]);

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("TestWorkflow")
            .WithCurrentStep("Start")
            .WithProperties(("Supervisor", b => b.Person(objectId: userId)))
            .Build();

        var service = CreateService(workflowDef);

        var result = await service.PrepareAnswerReferenceUpdate(instance, CreateUser(userId), CancellationToken.None);

        Assert.Equal(ExternalUserEmailAnswerUpdateResult.UserNotInAnswer, result.Result);
    }

    [Fact]
    public async Task GetMatchingInstanceOnlyProperties_NullInstanceValue_ReturnsUserNotInAnswer()
    {
        var userId = ObjectId.GenerateNewId().ToString();
        var propDef = new PropertyDefinition { Name = "Supervisor", Type = "User" };
        var workflowDef = CreateWorkflowDefinition([propDef]);

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("TestWorkflow")
            .WithCurrentStep("Start")
            .Build(); // No properties set → GetProperty returns null

        var service = CreateService(workflowDef);

        var result = await service.PrepareAnswerReferenceUpdate(instance, CreateUser(userId), CancellationToken.None);

        Assert.Equal(ExternalUserEmailAnswerUpdateResult.UserNotInAnswer, result.Result);
    }

    [Fact]
    public async Task GetMatchingInstanceOnlyProperties_NonArrayUserProperty_MatchingUserId_ReturnsUpdated()
    {
        var userId = ObjectId.GenerateNewId().ToString();
        var propDef = new PropertyDefinition { Name = "Supervisor", Type = "User" };
        var workflowDef = CreateWorkflowDefinition([propDef]);

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("TestWorkflow")
            .WithCurrentStep("Start")
            .WithProperties(("Supervisor", b => b.Person(objectId: userId)))
            .Build();

        var service = CreateService(workflowDef);

        var result = await service.PrepareAnswerReferenceUpdate(instance, CreateUser(userId), CancellationToken.None);

        Assert.Equal(ExternalUserEmailAnswerUpdateResult.Updated, result.Result);
        var property = Assert.Single(result.EditableProperties);
        Assert.Equal("Supervisor", property.Name);
    }

    [Fact]
    public async Task GetMatchingInstanceOnlyProperties_NonArrayUserProperty_NonMatchingUserId_ReturnsUserNotInAnswer()
    {
        var userId = ObjectId.GenerateNewId().ToString();
        var otherUserId = ObjectId.GenerateNewId().ToString();
        var propDef = new PropertyDefinition { Name = "Supervisor", Type = "User" };
        var workflowDef = CreateWorkflowDefinition([propDef]);

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("TestWorkflow")
            .WithCurrentStep("Start")
            .WithProperties(("Supervisor", b => b.Person(objectId: otherUserId)))
            .Build();

        var service = CreateService(workflowDef);

        var result = await service.PrepareAnswerReferenceUpdate(instance, CreateUser(userId), CancellationToken.None);

        Assert.Equal(ExternalUserEmailAnswerUpdateResult.UserNotInAnswer, result.Result);
    }

    [Fact]
    public async Task GetMatchingInstanceOnlyProperties_ArrayUserProperty_ContainsMatchingUserId_ReturnsUpdated()
    {
        var userId = ObjectId.GenerateNewId().ToString();
        var otherUserId = ObjectId.GenerateNewId().ToString();
        var propDef = new PropertyDefinition { Name = "Assessors", Type = "[User]" };
        var workflowDef = CreateWorkflowDefinition([propDef]);

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("TestWorkflow")
            .WithCurrentStep("Start")
            .WithProperties(("Assessors", b => b.Array(
                pb => pb.Person(objectId: otherUserId),
                pb => pb.Person(objectId: userId))))
            .Build();

        var service = CreateService(workflowDef);

        var result = await service.PrepareAnswerReferenceUpdate(instance, CreateUser(userId), CancellationToken.None);

        Assert.Equal(ExternalUserEmailAnswerUpdateResult.Updated, result.Result);
        var property = Assert.Single(result.EditableProperties);
        Assert.Equal("Assessors", property.Name);
    }

    [Fact]
    public async Task GetMatchingInstanceOnlyProperties_ArrayUserProperty_NoMatchingUserId_ReturnsUserNotInAnswer()
    {
        var userId = ObjectId.GenerateNewId().ToString();
        var otherUserId1 = ObjectId.GenerateNewId().ToString();
        var otherUserId2 = ObjectId.GenerateNewId().ToString();
        var propDef = new PropertyDefinition { Name = "Assessors", Type = "[User]" };
        var workflowDef = CreateWorkflowDefinition([propDef]);

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("TestWorkflow")
            .WithCurrentStep("Start")
            .WithProperties(("Assessors", b => b.Array(
                pb => pb.Person(objectId: otherUserId1),
                pb => pb.Person(objectId: otherUserId2))))
            .Build();

        var service = CreateService(workflowDef);

        var result = await service.PrepareAnswerReferenceUpdate(instance, CreateUser(userId), CancellationToken.None);

        Assert.Equal(ExternalUserEmailAnswerUpdateResult.UserNotInAnswer, result.Result);
    }

    [Fact]
    public async Task UpdateAnswerReferences_SingleUserProperty_UpdatesUserDataOnInstance()
    {
        var userId = ObjectId.GenerateNewId().ToString();
        var propDef = new PropertyDefinition { Name = "Supervisor", Type = "User" };
        var workflowDef = CreateWorkflowDefinition([propDef]); // no forms → instance-only property

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("TestWorkflow")
            .WithCurrentStep("Start")
            .WithProperties(("Supervisor", b => b.Person(objectId: userId)))
            .Build();

        var plan = new ExternalUserEmailAnswerUpdatePlan(
            ExternalUserEmailAnswerUpdateResult.Updated, [], [propDef]);

        await CreateServiceForUpdate(workflowDef)
            .UpdateAnswerReferences(plan, CreateUpdatedUser(userId), instance, CancellationToken.None);

        var stored = BsonSerializer.Deserialize<InstanceUser>(instance.Properties["Supervisor"].AsBsonDocument);
        Assert.Equal(userId, stored.Id);
        Assert.Equal("new@test.invalid", stored.Email);
        Assert.Equal("New Name", stored.DisplayName);
        Assert.Equal("new-username", stored.UserName);
    }

    [Fact]
    public async Task UpdateAnswerReferences_ArrayUserProperty_UpdatesMatchingUserAndPreservesOthers()
    {
        var targetId = ObjectId.GenerateNewId().ToString();
        var otherId = ObjectId.GenerateNewId().ToString();
        var propDef = new PropertyDefinition { Name = "Assessors", Type = "[User]" };
        var workflowDef = CreateWorkflowDefinition([propDef]);

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("TestWorkflow")
            .WithCurrentStep("Start")
            .WithProperties(("Assessors", b => b.Array(
                pb => pb.Person(objectId: otherId, displayName: "Other"),
                pb => pb.Person(objectId: targetId, displayName: "Old Name"))))
            .Build();

        var plan = new ExternalUserEmailAnswerUpdatePlan(
            ExternalUserEmailAnswerUpdateResult.Updated, [], [propDef]);

        await CreateServiceForUpdate(workflowDef)
            .UpdateAnswerReferences(plan, CreateUpdatedUser(targetId), instance, CancellationToken.None);

        var array = instance.Properties["Assessors"].AsBsonArray;
        Assert.Equal(2, array.Count);

        // Other user must be unchanged
        Assert.Equal(otherId, array[0].AsBsonDocument["_id"].ToString());
        Assert.Equal("Other", array[0].AsBsonDocument["DisplayName"].AsString);

        // Target user must reflect new data
        Assert.Equal(targetId, array[1].AsBsonDocument["_id"].ToString());
        Assert.Equal("new@test.invalid", array[1].AsBsonDocument["Email"].AsString);
        Assert.Equal("New Name", array[1].AsBsonDocument["DisplayName"].AsString);
    }

    [Fact]
    public async Task UpdateAnswerReferences_NullPropertyValue_DoesNotThrowAndLeavesPropertyAbsent()
    {
        var userId = ObjectId.GenerateNewId().ToString();
        var propDef = new PropertyDefinition { Name = "Supervisor", Type = "User" };
        var workflowDef = CreateWorkflowDefinition([propDef]);

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("TestWorkflow")
            .WithCurrentStep("Start")
            // "Supervisor" intentionally not set
            .Build();

        var plan = new ExternalUserEmailAnswerUpdatePlan(
            ExternalUserEmailAnswerUpdateResult.Updated, [], [propDef]);

        // Should not throw
        await CreateServiceForUpdate(workflowDef)
            .UpdateAnswerReferences(plan, CreateUpdatedUser(userId), instance, CancellationToken.None);

        Assert.False(instance.Properties.ContainsKey("Supervisor"));
    }
}