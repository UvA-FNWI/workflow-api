using System.Text.Json;
using MongoDB.Bson;
using Moq;
using UvA.Workflow.Api.Authentication;
using UvA.Workflow.Journaling;
using UvA.Workflow.Organizations;
using UvA.Workflow.Tests.Builders;
using UvA.Workflow.Users;
using UvA.Workflow.Users.EduId;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;

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
            PreferredLanguage = "nl-NL",
            ProviderKey = EduIdDirectoryKeys.ProviderKey,
            IsActive = false
        };

        var instanceUser = InstanceUser.FromUser(user);
        var bson = instanceUser.ToBsonDocument();

        Assert.Equal(user.Id, instanceUser.Id);
        Assert.Equal("jdoe", instanceUser.UserName);
        Assert.Equal("Jane Doe", instanceUser.DisplayName);
        Assert.Equal("j.doe@uva.nl", instanceUser.Email);
        Assert.Equal("nl-NL", instanceUser.PreferredLanguage);
        Assert.True(bson["IsExternal"].AsBoolean);
        Assert.Equal(BsonNull.Value, bson["Organization"]);
        Assert.Equal(["_id", "UserName", "DisplayName", "Email", "PreferredLanguage", "Organization", "IsExternal"],
            bson.Names);
        Assert.False(bson.Contains("AuthProvider"));
        Assert.False(bson.Contains("IsActive"));
    }

    [Fact]
    public async Task ConvertToValue_ForUser_StoresLeanInstanceUserDocument()
    {
        var orgId = ObjectId.GenerateNewId().ToString();
        var user = new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserName = "jdoe",
            DisplayName = "Jane Doe",
            Email = "j.doe@uva.nl",
            ProviderKey = EduIdDirectoryKeys.ProviderKey,
            PreferredLanguage = "nl",
            Organization = new Organization { Id = orgId, Name = "Test University" },
            IsActive = false
        };
        var userService = new Mock<IUserService>();
        var userRepository = new Mock<IUserRepository>();
        userService.Setup(s => s.GetUser("jdoe", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        var service = new AnswerConversionService(userService.Object, userRepository.Object);
        var property = new PropertyDefinition { Name = "Supervisor", Type = "User!" };
        var value = JsonDocument.Parse("""
                                       {
                                         "userName": "jdoe",
                                         "displayName": "Jane Doe",
                                         "email": "j.doe@uva.nl",
                                         "searchSource": "DataNose",
                                         "organization": { "id": "orgId", "name": "Test University" }
                                       }
                                       """.Replace("orgId", orgId)).RootElement;

        var result = await service.ConvertToValue(value, property, CancellationToken.None);
        var bson = result.AsBsonDocument;
        var org = bson["Organization"].AsBsonDocument;

        Assert.Equal(["_id", "UserName", "DisplayName", "Email", "PreferredLanguage", "Organization", "IsExternal"],
            bson.Names);
        Assert.Equal(user.Id, bson["_id"].ToString());
        Assert.Equal("jdoe", bson["UserName"].AsString);
        Assert.Equal("Jane Doe", bson["DisplayName"].AsString);
        Assert.Equal("j.doe@uva.nl", bson["Email"].AsString);
        Assert.Equal("nl", bson["PreferredLanguage"].AsString);
        Assert.Equal(orgId, org["_id"].ToString());
        Assert.Equal("Test University", org["Name"].AsString);
        Assert.True(bson["IsExternal"].AsBoolean);
        Assert.False(bson.Contains("AuthProvider"));
        Assert.False(bson.Contains("IsActive"));
    }

    [Fact]
    public async Task ConvertToValue_ForMissingExternalUser_DoesNotCreateFromAnswerPayload()
    {
        var userService = new Mock<IUserService>();
        var userRepository = new Mock<IUserRepository>();
        userService.Setup(s => s.GetUser("external@example.org", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        userRepository.Setup(r => r.GetByEmail("external@example.org", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        var service = new AnswerConversionService(userService.Object, userRepository.Object);
        var property = new PropertyDefinition { Name = "Supervisor", Type = "User!" };
        var value = JsonDocument.Parse("""
                                       {
                                         "userName": "external@example.org",
                                         "displayName": "External User",
                                         "email": "external@example.org",
                                         "providerKey": "eduid",
                                         "isExternal": true
                                       }
                                       """).RootElement;

        var result = await service.ConvertToValue(value, property, CancellationToken.None);

        Assert.Equal(BsonNull.Value, result);
        userService.Verify(s => s.AddOrUpdateUser(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Organization?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ConvertToValue_ForActivatedExternalUser_FallsBackToEmailLookup()
    {
        var user = new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserName = "eduid-123",
            DisplayName = "External User",
            Email = "external@example.org",
            ProviderKey = EduIdDirectoryKeys.ProviderKey,
            Organization = new Organization("org-1", "External Org"),
            IsActive = true
        };
        var userService = new Mock<IUserService>();
        var userRepository = new Mock<IUserRepository>();
        userService.Setup(s => s.GetUser("external@example.org", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        userRepository.Setup(r => r.GetByEmail("external@example.org", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        var service = new AnswerConversionService(userService.Object, userRepository.Object);
        var property = new PropertyDefinition { Name = "Supervisor", Type = "User!" };
        var value = JsonDocument.Parse("""
                                       {
                                         "userName": "external@example.org",
                                         "displayName": "External User",
                                         "email": "external@example.org",
                                         "providerKey": "eduid",
                                         "isExternal": true
                                       }
                                       """).RootElement;

        var result = await service.ConvertToValue(value, property, CancellationToken.None);

        var bson = result.AsBsonDocument;
        Assert.Equal(user.Id, bson["_id"].ToString());
        Assert.Equal("eduid-123", bson["UserName"].AsString);
        Assert.Equal("external@example.org", bson["Email"].AsString);
        Assert.True(bson["IsExternal"].AsBoolean);
        userService.Verify(s => s.AddOrUpdateUser(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Organization?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ConvertToValue_ForMissingInternalUser_IgnoresSubmittedProviderKey()
    {
        var user = new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserName = "student-123",
            DisplayName = "Student Name",
            Email = "student@uva.nl",
            ProviderKey = UserProviderKeys.Internal
        };
        var userService = new Mock<IUserService>();
        var userRepository = new Mock<IUserRepository>();
        userService.Setup(s => s.GetUser("student-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        userService.Setup(s => s.AddOrUpdateUser(
                "student-123",
                "Student Name",
                "student@uva.nl",
                UserProviderKeys.Internal,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        var service = new AnswerConversionService(userService.Object, userRepository.Object);
        var property = new PropertyDefinition { Name = "Student", Type = "User!" };
        var value = JsonDocument.Parse("""
                                       {
                                         "userName": "student-123",
                                         "displayName": "Student Name",
                                         "email": "student@uva.nl",
                                         "providerKey": "eduid",
                                         "isExternal": false
                                       }
                                       """).RootElement;

        var result = await service.ConvertToValue(value, property, CancellationToken.None);

        Assert.False(result.AsBsonDocument["IsExternal"].AsBoolean);
        userService.Verify(s => s.AddOrUpdateUser(
                "student-123",
                "Student Name",
                "student@uva.nl",
                UserProviderKeys.Internal,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
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
    public void ObjectContext_GetValue_ForOrganizationAndArray_ReturnsInstanceOrganizationTypes()
    {
        var organizationId = ObjectId.GenerateNewId().ToString();
        var organizationDoc = new BsonDocument
        {
            { "_id", ObjectId.Parse(organizationId) },
            { "Name", "FNWI" }
        };
        var singleProperty = new PropertyDefinition { Name = "Faculty", Type = "Organization!" };
        var arrayProperty = new PropertyDefinition { Name = "Faculties", Type = "[Organization]!" };

        var single = ObjectContext.GetValue(organizationDoc, singleProperty);
        var array = ObjectContext.GetValue(new BsonArray { organizationDoc }, arrayProperty);

        var instanceOrganization = Assert.IsType<InstanceOrganization>(single);
        var instanceOrganizations = Assert.IsType<InstanceOrganization[]>(array);

        Assert.Equal("FNWI", instanceOrganization.Name);
        Assert.Single(instanceOrganizations);
        Assert.Equal(organizationId, instanceOrganizations[0].Id);
    }

    [Fact]
    public async Task ConvertToValue_ForUnknownOrganization_ReturnsBsonNull()
    {
        var organizationId = ObjectId.GenerateNewId().ToString();
        var userService = new Mock<IUserService>();
        var organizationService = new Mock<IOrganizationService>();
        organizationService.Setup(r => r.GetOrganization(organizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Organization?)null);
        var service = new AnswerConversionService(userService.Object);
        var property = new PropertyDefinition { Name = "Faculty", Type = "Organization!" };
        var value = JsonDocument.Parse($$"""
                                         {
                                           "id": "{{organizationId}}",
                                           "name": "Unknown"
                                         }
                                         """).RootElement;

        var result = await service.ConvertToValue(value, property, CancellationToken.None);

        Assert.Equal(BsonNull.Value, result);
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
            ProviderKey = EduIdDirectoryKeys.ProviderKey,
            IsActive = false
        };

        await service.Create("Project", user, CancellationToken.None, userProperty: "Supervisor");

        var bson = Assert.IsType<BsonDocument>(created!.Properties["Supervisor"]);
        Assert.Equal(["_id", "UserName", "DisplayName", "Email", "Organization", "IsExternal"], bson.Names);
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
            ProviderKey = EduIdDirectoryKeys.ProviderKey,
            IsActive = false
        };

        await service.Create("Project", user, CancellationToken.None, userProperty: "Student");

        var bson = Assert.IsType<BsonDocument>(created!.Properties["Student"]);
        Assert.Equal(["_id", "UserName", "DisplayName", "Email", "Organization", "IsExternal"], bson.Names);
        Assert.False(bson.Contains("AuthProvider"));
        Assert.False(bson.Contains("IsActive"));
    }
}