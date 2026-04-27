using System.Text.Json;
using MongoDB.Bson;
using Moq;
using UvA.Workflow.Entities.Domain;
using UvA.Workflow.Journaling;
using UvA.Workflow.Services;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests;

public class InstanceUserStorageTests
{
    private static readonly ModelService ModelService =
        new(new ModelParser(new FileSystemProvider("../../../../Examples/Projects")));

    [Fact]
    public void FromUser_MapsOnlyInstanceFields()
    {
        var user = new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserName = "jdoe",
            DisplayName = "Jane Doe",
            Email = "j.doe@uva.nl",
            AuthProvider = UserAuthProvider.EduId,
            IsActive = false
        };

        var instanceUser = InstanceUser.FromUser(user);
        var bson = instanceUser.ToBsonDocument();

        Assert.Equal(user.Id, instanceUser.Id);
        Assert.Equal("jdoe", instanceUser.UserName);
        Assert.Equal("Jane Doe", instanceUser.DisplayName);
        Assert.Equal("j.doe@uva.nl", instanceUser.Email);
        Assert.Equal(["_id", "UserName", "DisplayName", "Email"], bson.Names);
        Assert.False(bson.Contains("AuthProvider"));
        Assert.False(bson.Contains("IsActive"));
    }

    [Fact]
    public async Task ConvertToValue_ForUser_StoresLeanInstanceUserDocument()
    {
        var user = new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserName = "jdoe",
            DisplayName = "Jane Doe",
            Email = "j.doe@uva.nl",
            AuthProvider = UserAuthProvider.EduId,
            IsActive = false
        };
        var userService = new Mock<IUserService>();
        userService.Setup(s => s.GetUser("jdoe", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        var service = new AnswerConversionService(userService.Object);
        var property = new PropertyDefinition { Name = "Supervisor", Type = "User!" };
        var value = JsonDocument.Parse("""
                                       {
                                         "userName": "jdoe",
                                         "displayName": "Jane Doe",
                                         "email": "j.doe@uva.nl"
                                       }
                                       """).RootElement;

        var result = await service.ConvertToValue(value, property, CancellationToken.None);
        var bson = result.AsBsonDocument;

        Assert.Equal(["_id", "UserName", "DisplayName", "Email"], bson.Names);
        Assert.Equal(user.Id, bson["_id"].ToString());
        Assert.Equal("jdoe", bson["UserName"].AsString);
        Assert.Equal("Jane Doe", bson["DisplayName"].AsString);
        Assert.Equal("j.doe@uva.nl", bson["Email"].AsString);
        Assert.False(bson.Contains("AuthProvider"));
        Assert.False(bson.Contains("IsActive"));
    }

    [Fact]
    public void ObjectContext_GetValue_ForUserAndArray_ReturnsInstanceUserTypes()
    {
        var userId = ObjectId.GenerateNewId().ToString();
        var userDoc = new BsonDocument
        {
            { "_id", ObjectId.Parse(userId) },
            { "UserName", "jdoe" },
            { "DisplayName", "Jane Doe" },
            { "Email", "j.doe@uva.nl" }
        };
        var singleProperty = new PropertyDefinition { Name = "Supervisor", Type = "User!" };
        var arrayProperty = new PropertyDefinition { Name = "Student", Type = "[User]!" };

        var single = ObjectContext.GetValue(userDoc, singleProperty);
        var array = ObjectContext.GetValue(new BsonArray { userDoc }, arrayProperty);

        var instanceUser = Assert.IsType<InstanceUser>(single);
        var instanceUsers = Assert.IsType<InstanceUser[]>(array);

        Assert.Equal("jdoe", instanceUser.UserName);
        Assert.Single(instanceUsers);
        Assert.Equal(userId, instanceUsers[0].Id);
    }

    [Fact]
    public async Task GetViewerRoles_MatchesSingleEmbeddedInstanceUserById()
    {
        var userId = ObjectId.GenerateNewId().ToString();
        var userService = new Mock<IUserService>();
        userService.Setup(s => s.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        userService.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = userId, UserName = "jdoe" });
        var rightsService = new RightsService(ModelService, userService.Object, Mock.Of<IWorkflowInstanceRepository>());
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .WithProperties(("Supervisor", _ => new BsonDocument
            {
                { "_id", ObjectId.Parse(userId) },
                { "UserName", "jdoe" },
                { "DisplayName", "Jane Doe" },
                { "Email", "j.doe@uva.nl" }
            }))
            .Build();

        var roles = await rightsService.GetViewerRoles(instance, CancellationToken.None);

        Assert.Contains("Supervisor", roles);
    }

    [Fact]
    public async Task GetViewerRoles_MatchesEmbeddedInstanceUserArrayById()
    {
        var userId = ObjectId.GenerateNewId().ToString();
        var userService = new Mock<IUserService>();
        userService.Setup(s => s.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        userService.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = userId, UserName = "jdoe" });
        var rightsService = new RightsService(ModelService, userService.Object, Mock.Of<IWorkflowInstanceRepository>());
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .WithProperties(("Student", _ => new BsonArray
            {
                new BsonDocument
                {
                    { "_id", ObjectId.Parse(userId) },
                    { "UserName", "jdoe" },
                    { "DisplayName", "Jane Doe" },
                    { "Email", "j.doe@uva.nl" }
                }
            }))
            .Build();

        var roles = await rightsService.GetViewerRoles(instance, CancellationToken.None);

        Assert.Contains("Student", roles);
    }

    [Fact]
    public async Task Create_WithUserProperty_StoresLeanInstanceUserDocument()
    {
        WorkflowInstance? created = null;
        var repository = new Mock<IWorkflowInstanceRepository>();
        repository.Setup(r => r.Create(It.IsAny<WorkflowInstance>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowInstance, CancellationToken>((instance, _) => created = instance)
            .Returns(Task.CompletedTask);
        var service = new WorkflowInstanceService(ModelService, repository.Object, Mock.Of<IInstanceJournalService>());
        var user = new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserName = "jdoe",
            DisplayName = "Jane Doe",
            Email = "j.doe@uva.nl",
            AuthProvider = UserAuthProvider.EduId,
            IsActive = false
        };

        await service.Create("Project", user, CancellationToken.None, userProperty: "Supervisor");

        var bson = Assert.IsType<BsonDocument>(created!.Properties["Supervisor"]);
        Assert.Equal(["_id", "UserName", "DisplayName", "Email"], bson.Names);
        Assert.False(bson.Contains("AuthProvider"));
        Assert.False(bson.Contains("IsActive"));
    }

    [Fact]
    public async Task Create_WithArrayUserProperty_StoresLeanInstanceUserDocumentInArray()
    {
        WorkflowInstance? created = null;
        var repository = new Mock<IWorkflowInstanceRepository>();
        repository.Setup(r => r.Create(It.IsAny<WorkflowInstance>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowInstance, CancellationToken>((instance, _) => created = instance)
            .Returns(Task.CompletedTask);
        var service = new WorkflowInstanceService(ModelService, repository.Object, Mock.Of<IInstanceJournalService>());
        var user = new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserName = "jdoe",
            DisplayName = "Jane Doe",
            Email = "j.doe@uva.nl",
            AuthProvider = UserAuthProvider.EduId,
            IsActive = false
        };

        await service.Create("Project", user, CancellationToken.None, userProperty: "Student");

        var array = Assert.IsType<BsonArray>(created!.Properties["Student"]);
        var bson = array.Single().AsBsonDocument;
        Assert.Equal(["_id", "UserName", "DisplayName", "Email"], bson.Names);
        Assert.False(bson.Contains("AuthProvider"));
        Assert.False(bson.Contains("IsActive"));
    }
}