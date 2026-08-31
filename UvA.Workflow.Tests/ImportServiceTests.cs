using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using UvA.Workflow.Import;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Submissions;
using UvA.Workflow.Tests.Builders;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests;

public class ImportServiceTests
{
    // ── shared YAML ────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> DefaultPreviewYaml = new()
    {
        ["Common/Roles/Admin.yaml"] = "name: Admin",
        ["TestDef/Entity.yaml"] = """
                                  name: TestDef
                                  titlePlural: Test Defs
                                  """,
        ["TestDef/Properties.yaml"] = """
                                      properties:
                                        - name: StudentNumber
                                          type: String!
                                        - name: Title
                                          type: String!
                                        - name: EC
                                          type: Int!
                                        - name: Supervisor
                                          type: User!
                                        - name: OwnerName
                                          type: String!
                                      """,
        ["TestDef/Actions.yaml"] = """
                                   globalActions:
                                     - roles: [Admin]
                                       type: Edit
                                   """,
        ["TestDef/Screens/Overview.yaml"] = """
                                            name: Overview
                                            bulkEdit:
                                              identifier:
                                                property: StudentNumber
                                              editableProperties:
                                                - Title
                                                - EC
                                                - Supervisor
                                              readOnlyProperties:
                                                - property: OwnerName
                                            columns:
                                              - property: StudentNumber
                                            """
    };

    // ── shared test data ───────────────────────────────────────────────────────

    /// Default column mappings used by most preview tests.
    private static readonly ColumnMapping[] DefaultMappings =
        [new("A", "StudentNumber"), new("B", "Title")];

    /// Builds a minimal TestDef instance with the given StudentNumber (and any extra properties).
    private static WorkflowInstance BasicInstance(
        string studentNumber = "S001",
        params (string Name, Func<PropertyBuilder, BsonValue> Value)[] extraProperties) =>
        new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("TestDef")
            .WithCurrentStep("Start")
            .WithProperties(
                [("StudentNumber", b => b.Value(studentNumber)), ..extraProperties])
            .Build();

    /// Configures the repository mock to return <paramref name="instances"/> for any
    /// GetByWorkflowDefinition call on "TestDef".
    private static void SetupRepo(
        Mock<IWorkflowInstanceRepository> repo,
        params WorkflowInstance[] instances) =>
        repo.Setup(r => r.GetByWorkflowDefinition(
                "TestDef",
                It.IsAny<FilterDefinition<WorkflowInstance>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(instances);

    // ── single shared factory ──────────────────────────────────────────────────

    private static (
        ImportService Service,
        Mock<IWorkflowInstanceRepository> Repo,
        Mock<IUserRepository> UserRepo,
        Mock<IUserService> UserService)
        CreateService(
            Dictionary<string, string> yaml,
            string[]? globalRoles = null,
            Mock<IFileParserService>? parserMock = null,
            IAnswerService? answerService = null) // ← new optional parameter
    {
        globalRoles ??= [];
        parserMock ??= MakeParser();

        var modelService = new ModelService(new ModelParser(new DictionaryProvider(yaml)));

        var userServiceMock = new Mock<IUserService>();
        userServiceMock
            .Setup(s => s.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(globalRoles);
        userServiceMock
            .Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = ObjectId.GenerateNewId().ToString() });

        var repoMock = new Mock<IWorkflowInstanceRepository>();
        var userRepoMock = new Mock<IUserRepository>();

        var service = new ImportService(
            [parserMock.Object],
            repoMock.Object,
            new AnswerConversionService(userServiceMock.Object, userRepoMock.Object),
            answerService ?? null!, // ← forwarded
            modelService,
            userRepoMock.Object,
            new RightsService(modelService, userServiceMock.Object, repoMock.Object));

        return (service, repoMock, userRepoMock, userServiceMock);
    }

    /// Creates a mock file parser that returns the given rows.
    private static Mock<IFileParserService> MakeParser(params Dictionary<string, string>[] rows)
    {
        var mock = new Mock<IFileParserService>();
        mock.Setup(p => p.CanHandle(It.IsAny<string>())).Returns(true);
        mock.Setup(p => p.ParseRows(It.IsAny<Stream>())).Returns(rows);
        return mock;
    }

    // ── GetEditableImportableProperties ───────────────────────────────────────

    [Fact]
    public async Task GetEditableImportableProperties_MixedDataTypes_ReturnsOnlyImportableTypes()
    {
        var (service, _, _, _) = CreateService(new Dictionary<string, string>
        {
            ["Common/Roles/Admin.yaml"] = "name: Admin",
            ["TestDef/Entity.yaml"] = "name: TestDef\ntitlePlural: Test Defs",
            ["TestDef/Properties.yaml"] = """
                                          properties:
                                            - name: Title
                                              type: String!
                                            - name: Count
                                              type: Int!
                                            - name: Score
                                              type: Double!
                                            - name: StartDate
                                              type: Date!
                                            - name: SubmittedAt
                                              type: DateTime!
                                            - name: Owner
                                              type: User!
                                            - name: Attachment
                                              type: File!
                                            - name: IsPublic
                                              type: Boolean!
                                          """,
            ["TestDef/Actions.yaml"] = "globalActions:\n  - roles: [Admin]\n    type: Edit"
        });

        var result = await service.GetEditableImportableProperties("TestDef",
            ["Title", "Count", "Score", "StartDate", "SubmittedAt", "Owner", "Attachment", "IsPublic"]);

        var names = result.Select(p => p.Name).ToArray();
        Assert.Equal(["Title", "Count", "Score", "StartDate", "SubmittedAt", "Owner"], names);
        Assert.DoesNotContain("Attachment", names);
        Assert.DoesNotContain("IsPublic", names);
    }

    [Fact]
    public async Task GetEditableImportableProperties_EditActionScopedToOneProperty_ReturnsOnlyThatProperty()
    {
        var (service, _, _, _) = CreateService(new Dictionary<string, string>
        {
            ["Common/Roles/Admin.yaml"] = "name: Admin",
            ["TestDef/Entity.yaml"] = "name: TestDef\ntitlePlural: Test Defs",
            ["TestDef/Properties.yaml"] = """
                                          properties:
                                            - name: Title
                                              type: String!
                                            - name: EC
                                              type: Int!
                                            - name: Notes
                                              type: String
                                          """,
            ["TestDef/Actions.yaml"] = """
                                       globalActions:
                                         - roles: [Admin]
                                           type: Edit
                                           propertyDefinition: Title
                                       """
        });

        var result = await service.GetEditableImportableProperties("TestDef", ["Title", "EC", "Notes"]);

        Assert.Equal("Title", Assert.Single(result).Name);
    }

    [Fact]
    public async Task GetEditableImportableProperties_NoEditActions_ReturnsEmptyArray()
    {
        var (service, _, _, _) = CreateService(new Dictionary<string, string>
        {
            ["TestDef/Entity.yaml"] = "name: TestDef\ntitlePlural: Test Defs",
            ["TestDef/Properties.yaml"] =
                "properties:\n  - name: Title\n    type: String!\n  - name: EC\n    type: Int!"
        });

        Assert.Empty(await service.GetEditableImportableProperties("TestDef", ["Title", "EC"]));
    }

    // ── PreviewAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PreviewAsync_ScreenHasNoBulkEditConfig_ThrowsInvalidOperationException()
    {
        var yaml = new Dictionary<string, string>(DefaultPreviewYaml)
        {
            ["TestDef/Screens/Overview.yaml"] = "name: Overview\ncolumns:\n  - property: StudentNumber"
        };
        var (service, _, _, _) = CreateService(yaml, ["Admin"]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PreviewAsync("TestDef", "Overview", Stream.Null, "text/csv", [], CancellationToken.None));
    }

    [Fact]
    public async Task PreviewAsync_IdentifierMappingMissingFromMappings_ThrowsInvalidOperationException()
    {
        var (service, _, _, _) = CreateService(DefaultPreviewYaml, ["Admin"]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PreviewAsync("TestDef", "Overview", Stream.Null, "text/csv",
                [new("B", "Title")], CancellationToken.None));
    }

    [Fact]
    public async Task PreviewAsync_HappyPath_ReturnsPreviewWithCorrectColumnsAndNoErrors()
    {
        var (service, repo, _, _) = CreateService(DefaultPreviewYaml, ["Admin"],
            MakeParser(new Dictionary<string, string> { ["A"] = "S001", ["B"] = "My Thesis" }));

        var instance = BasicInstance(extraProperties: ("OwnerName", b => b.Value("John Doe")));
        SetupRepo(repo, instance);

        var result = await service.PreviewAsync("TestDef", "Overview", Stream.Null, "text/csv",
            DefaultMappings, CancellationToken.None);

        Assert.Equal(3, result.Columns.Length);
        Assert.Equal("StudentNumber", result.Columns[0].Name); // identifier
        Assert.Equal("OwnerName", result.Columns[1].Name); // read-only
        Assert.Equal("Title", result.Columns[2].Name); // data
        var row = Assert.Single(result.Rows);
        Assert.Equal(instance.Id, row.InstanceId);
        Assert.Empty(row.ValidationErrors);
    }

    [Fact]
    public async Task PreviewAsync_EmptyIdentifierCell_ProducesEntryNotFoundErrorAndEmptyInstanceId()
    {
        var (service, repo, _, _) = CreateService(DefaultPreviewYaml, ["Admin"],
            MakeParser(new Dictionary<string, string> { ["A"] = "   ", ["B"] = "My Thesis" }));
        SetupRepo(repo); // no instances

        var result = await service.PreviewAsync("TestDef", "Overview", Stream.Null, "text/csv",
            DefaultMappings, CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(string.Empty, row.InstanceId);
        var error = Assert.Single(row.ValidationErrors);
        Assert.Equal("EntryNotFound", error.Code);
        Assert.Equal("StudentNumber", error.Column);
    }

    [Fact]
    public async Task PreviewAsync_IdentifierNotMatchingAnyInstance_ProducesEntryNotFoundError()
    {
        var (service, repo, _, _) = CreateService(DefaultPreviewYaml, ["Admin"],
            MakeParser(new Dictionary<string, string> { ["A"] = "S999", ["B"] = "My Thesis" }));
        SetupRepo(repo); // no instances

        var result = await service.PreviewAsync("TestDef", "Overview", Stream.Null, "text/csv",
            DefaultMappings, CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(string.Empty, row.InstanceId);
        var error = Assert.Single(row.ValidationErrors);
        Assert.Equal("EntryNotFound", error.Code);
        Assert.Equal("StudentNumber", error.Column);
    }

    [Fact]
    public async Task PreviewAsync_DuplicateIdentifierRows_BothRowsGetDuplicateEntryError()
    {
        var (service, repo, _, _) = CreateService(DefaultPreviewYaml, ["Admin"], MakeParser(
            new Dictionary<string, string> { ["A"] = "S001", ["B"] = "Thesis A" },
            new Dictionary<string, string> { ["A"] = "S001", ["B"] = "Thesis B" }));
        SetupRepo(repo, BasicInstance());

        var result = await service.PreviewAsync("TestDef", "Overview", Stream.Null, "text/csv",
            DefaultMappings, CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, row =>
            Assert.Contains(row.ValidationErrors, e => e.Code == "DuplicateEntry" && e.Column == "StudentNumber"));
    }

    [Fact]
    public async Task PreviewAsync_InvalidDataTypeForIntProperty_ProducesInvalidDataTypeError()
    {
        var (service, repo, _, _) = CreateService(DefaultPreviewYaml, ["Admin"],
            MakeParser(new Dictionary<string, string> { ["A"] = "S001", ["C"] = "not-a-number" }));
        SetupRepo(repo, BasicInstance());

        var result = await service.PreviewAsync("TestDef", "Overview", Stream.Null, "text/csv",
            [new("A", "StudentNumber"), new("C", "EC")], CancellationToken.None);

        var error = Assert.Single(Assert.Single(result.Rows).ValidationErrors);
        Assert.Equal("InvalidDataType", error.Code);
        Assert.Equal("EC", error.Column);
    }

    [Fact]
    public async Task PreviewAsync_UserPropertyWithUnknownEmail_ProducesUserNotFoundError()
    {
        var (service, repo, userRepo, _) = CreateService(DefaultPreviewYaml, ["Admin"],
            MakeParser(new Dictionary<string, string> { ["A"] = "S001", ["D"] = "ghost@example.com" }));
        SetupRepo(repo, BasicInstance());
        userRepo.Setup(u => u.GetByEmail("ghost@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await service.PreviewAsync("TestDef", "Overview", Stream.Null, "text/csv",
            [new("A", "StudentNumber"), new("D", "Supervisor")], CancellationToken.None);

        var error = Assert.Single(Assert.Single(result.Rows).ValidationErrors);
        Assert.Equal("UserNotFound", error.Code);
        Assert.Equal("Supervisor", error.Column);
    }

    [Fact]
    public async Task PreviewAsync_UserHasNoEditRights_ProducesNotAllowedErrorOnIdentifierColumn()
    {
        // globalRoles: [] → user only gets the implicit "Registered" role which has no Edit action
        var (service, repo, _, _) = CreateService(DefaultPreviewYaml, [],
            MakeParser(new Dictionary<string, string> { ["A"] = "S001", ["B"] = "My Thesis" }));
        SetupRepo(repo, BasicInstance());

        var result = await service.PreviewAsync("TestDef", "Overview", Stream.Null, "text/csv",
            DefaultMappings, CancellationToken.None);

        var error = Assert.Single(Assert.Single(result.Rows).ValidationErrors);
        Assert.Equal("NotAllowed", error.Code);
        Assert.Equal("StudentNumber", error.Column);
    }

    [Fact]
    public async Task PreviewAsync_SpecificPropertyNotEditableForUser_ProducesNotAllowedOnThatPropertyOnly()
    {
        // Edit action scoped to Title only → EC should get NotAllowed, Title should not
        var yaml = new Dictionary<string, string>(DefaultPreviewYaml)
        {
            ["TestDef/Actions.yaml"] = """
                                       globalActions:
                                         - roles: [Admin]
                                           type: Edit
                                           propertyDefinition: Title
                                       """
        };
        var (service, repo, _, _) = CreateService(yaml, ["Admin"],
            MakeParser(new Dictionary<string, string> { ["A"] = "S001", ["B"] = "My Thesis", ["C"] = "6" }));
        SetupRepo(repo, BasicInstance());

        var result = await service.PreviewAsync("TestDef", "Overview", Stream.Null, "text/csv",
            [new("A", "StudentNumber"), new("B", "Title"), new("C", "EC")], CancellationToken.None);

        var error = Assert.Single(Assert.Single(result.Rows).ValidationErrors);
        Assert.Equal("NotAllowed", error.Code);
        Assert.Equal("EC", error.Column);
    }

    [Fact]
    public async Task PreviewAsync_ReadOnlyProperty_ValueIsFilledFromMatchingInstance()
    {
        var (service, repo, _, _) = CreateService(DefaultPreviewYaml, ["Admin"],
            MakeParser(new Dictionary<string, string> { ["A"] = "S001" }));
        SetupRepo(repo, BasicInstance(extraProperties: ("OwnerName", b => b.Value("Jane Doe"))));

        var result = await service.PreviewAsync("TestDef", "Overview", Stream.Null, "text/csv",
            [new("A", "StudentNumber")], CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.True(row.Values.TryGetValue("OwnerName", out var ownerValue));
        Assert.Equal("Jane Doe", ownerValue);
    }

    [Fact]
    public async Task PreviewAsync_IdentifierColumnAlwaysFirstThenReadOnlyThenData()
    {
        var (service, repo, _, _) = CreateService(DefaultPreviewYaml, ["Admin"]);
        SetupRepo(repo); // no instances needed — only columns matter

        var result = await service.PreviewAsync("TestDef", "Overview", Stream.Null, "text/csv",
            DefaultMappings, CancellationToken.None);

        Assert.Equal("StudentNumber", result.Columns[0].Name); // identifier
        Assert.Equal("OwnerName", result.Columns[1].Name); // read-only
        Assert.Equal("Title", result.Columns[2].Name); // data
    }

    [Fact]
    public async Task PreviewAsync_MappingForUnknownProperty_IsSilentlyDroppedFromDataColumns()
    {
        var (service, repo, _, _) = CreateService(DefaultPreviewYaml, ["Admin"]);
        SetupRepo(repo);

        var result = await service.PreviewAsync("TestDef", "Overview", Stream.Null, "text/csv",
            [new("A", "StudentNumber"), new("B", "Title"), new("Z", "DoesNotExistProperty")],
            CancellationToken.None);

        Assert.DoesNotContain(result.Columns, c => c.Name == "DoesNotExistProperty");
    }

    // ── ImportAsync ───────────────────────────────────────────────────────────
// ── ImportAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportAsync_RowWithEmptyInstanceId_IsSkippedWithoutError()
    {
        var (service, repo, _, _) = CreateService(DefaultPreviewYaml, ["Admin"]);

        await service.ImportAsync("TestDef", "Overview",
            [new ImportConfirmRow("", new Dictionary<string, string> { ["Title"] = "Ignored" })],
            CancellationToken.None);

        repo.Verify(r => r.GetById(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportAsync_RowWhereGetByIdReturnsNull_IsSkippedWithoutError()
    {
        var (service, repo, _, _) = CreateService(DefaultPreviewYaml, ["Admin"]);
        repo.Setup(r => r.GetById("missing-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowInstance?)null);

        await service.ImportAsync("TestDef", "Overview",
            [new ImportConfirmRow("missing-id", new Dictionary<string, string> { ["Title"] = "Ignored" })],
            CancellationToken.None);
        // No exception thrown — test passes
    }

    [Fact]
    public async Task ImportAsync_InstanceWithNoEditRights_ThrowsForbiddenWorkflowActionException()
    {
        // globalRoles: [] → no Edit action granted for any instance
        var (service, repo, _, _) = CreateService(DefaultPreviewYaml, []);
        var instance = BasicInstance();
        repo.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
            service.ImportAsync("TestDef", "Overview",
                [new ImportConfirmRow(instance.Id, new Dictionary<string, string> { ["Title"] = "New" })],
                CancellationToken.None));
    }

    [Fact]
    public async Task ImportAsync_PropertyNotEditableForUser_ThrowsForbiddenWorkflowActionException()
    {
        // Edit action scoped to Title only → writing EC (also in editableProperties) should be forbidden
        var yaml = new Dictionary<string, string>(DefaultPreviewYaml)
        {
            ["TestDef/Actions.yaml"] = """
                                       globalActions:
                                         - roles: [Admin]
                                           type: Edit
                                           propertyDefinition: Title
                                       """
        };
        var answerMock = new Mock<IAnswerService>();
        answerMock
            .Setup(a => a.SavePropertyValue(
                It.IsAny<WorkflowInstance>(), It.IsAny<string[]>(),
                It.IsAny<PropertyDefinition>(), It.IsAny<BsonValue>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var (service, repo, _, _) = CreateService(yaml, ["Admin"],
            answerService: answerMock.Object);

        var instance = BasicInstance();
        repo.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
            service.ImportAsync("TestDef", "Overview",
                [
                    new ImportConfirmRow(instance.Id, new Dictionary<string, string>
                    {
                        ["Title"] = "OK",
                        ["EC"] = "6" // not covered by the scoped Edit action
                    })
                ],
                CancellationToken.None));
    }

    [Fact]
    public async Task ImportAsync_UnknownPropertyInValues_ThrowsForbiddenWorkflowActionException()
    {
        var answerMock = new Mock<IAnswerService>();
        var (service, repo, _, _) = CreateService(DefaultPreviewYaml, ["Admin"],
            answerService: answerMock.Object);
        var instance = BasicInstance();
        repo.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        // CanEditProperties returns false for properties not found in the model,
        // so ForbiddenWorkflowActionException is thrown before GetProperty is reached.
        await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
            service.ImportAsync("TestDef", "Overview",
                [
                    new ImportConfirmRow(instance.Id, new Dictionary<string, string>
                    {
                        ["DoesNotExist"] = "value"
                    })
                ],
                CancellationToken.None));
    }

    [Fact]
    public async Task ImportAsync_IdentifierAndReadOnlyProperties_AreNeverPassedToSavePropertyValue()
    {
        var answerMock = new Mock<IAnswerService>();
        answerMock
            .Setup(a => a.SavePropertyValue(
                It.IsAny<WorkflowInstance>(), It.IsAny<string[]>(),
                It.IsAny<PropertyDefinition>(), It.IsAny<BsonValue>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var (service, repo, _, _) = CreateService(DefaultPreviewYaml, ["Admin"],
            answerService: answerMock.Object);
        var instance = BasicInstance();
        repo.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        await service.ImportAsync("TestDef", "Overview",
            [
                new ImportConfirmRow(instance.Id, new Dictionary<string, string>
                {
                    ["StudentNumber"] = "S001", // identifier — must be skipped
                    ["OwnerName"] = "ignored", // read-only — must be skipped
                    ["Title"] = "My Thesis"
                })
            ],
            CancellationToken.None);

        answerMock.Verify(a => a.SavePropertyValue(
            It.IsAny<WorkflowInstance>(),
            It.Is<string[]>(p => p[0] == "StudentNumber"),
            It.IsAny<PropertyDefinition>(), It.IsAny<BsonValue>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);

        answerMock.Verify(a => a.SavePropertyValue(
            It.IsAny<WorkflowInstance>(),
            It.Is<string[]>(p => p[0] == "OwnerName"),
            It.IsAny<PropertyDefinition>(), It.IsAny<BsonValue>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);

        // Title IS editable — must be saved
        answerMock.Verify(a => a.SavePropertyValue(
            instance,
            It.Is<string[]>(p => p[0] == "Title"),
            It.IsAny<PropertyDefinition>(), It.IsAny<BsonValue>(),
            /*shouldLog*/ true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_ValidRow_SavePropertyValueCalledForEachEditablePropertyWithShouldLogTrue()
    {
        var answerMock = new Mock<IAnswerService>();
        answerMock
            .Setup(a => a.SavePropertyValue(
                It.IsAny<WorkflowInstance>(), It.IsAny<string[]>(),
                It.IsAny<PropertyDefinition>(), It.IsAny<BsonValue>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var (service, repo, _, _) = CreateService(DefaultPreviewYaml, ["Admin"],
            answerService: answerMock.Object);
        var instance = BasicInstance();
        repo.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        await service.ImportAsync("TestDef", "Overview",
            [
                new ImportConfirmRow(instance.Id, new Dictionary<string, string>
                {
                    ["Title"] = "My Thesis",
                    ["EC"] = "30"
                })
            ],
            CancellationToken.None);

        answerMock.Verify(a => a.SavePropertyValue(
            instance,
            It.Is<string[]>(p => p[0] == "Title"),
            It.IsAny<PropertyDefinition>(), It.IsAny<BsonValue>(),
            /*shouldLog*/ true, It.IsAny<CancellationToken>()), Times.Once);

        answerMock.Verify(a => a.SavePropertyValue(
            instance,
            It.Is<string[]>(p => p[0] == "EC"),
            It.IsAny<PropertyDefinition>(), It.IsAny<BsonValue>(),
            /*shouldLog*/ true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_UserProperty_ResolvesEmailAndSavesNonNullBsonDocument()
    {
        BsonValue? capturedValue = null;
        var answerMock = new Mock<IAnswerService>();
        answerMock
            .Setup(a => a.SavePropertyValue(
                It.IsAny<WorkflowInstance>(),
                It.Is<string[]>(p => p[0] == "Supervisor"),
                It.IsAny<PropertyDefinition>(), It.IsAny<BsonValue>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowInstance, string[], PropertyDefinition, BsonValue, bool, CancellationToken>((_, _, _, v,
                _, _) => capturedValue = v)
            .Returns(Task.CompletedTask);

        var (service, repo, userRepo, userServiceMock) = CreateService(DefaultPreviewYaml, ["Admin"],
            answerService: answerMock.Object);
        var instance = BasicInstance();
        repo.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        var user = new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserName = "jdoe",
            DisplayName = "John Doe",
            Email = "john@example.com",
            ProviderKey = UserProviderKeys.Internal // ← required by InstanceUser.FromUser
        };

        userRepo.Setup(u => u.GetByEmail("john@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // ConvertUser calls userService.GetUser(userName) — must return the same user
        userServiceMock.Setup(s => s.GetUser("jdoe", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await service.ImportAsync("TestDef", "Overview",
            [
                new ImportConfirmRow(instance.Id, new Dictionary<string, string>
                {
                    ["Supervisor"] = "john@example.com"
                })
            ],
            CancellationToken.None);

        var doc = Assert.IsType<BsonDocument>(capturedValue);
        Assert.Equal(user.Email, doc["Email"].AsString);
        Assert.Equal(user.UserName, doc["UserName"].AsString);
        Assert.Equal(user.DisplayName, doc["DisplayName"].AsString);
    }

    [Fact]
    public async Task ImportAsync_EmptyRawValue_SavesBsonNullWithoutCallingParser()
    {
        BsonValue? capturedValue = null;
        var answerMock = new Mock<IAnswerService>();
        answerMock
            .Setup(a => a.SavePropertyValue(
                It.IsAny<WorkflowInstance>(), It.IsAny<string[]>(),
                It.IsAny<PropertyDefinition>(), It.IsAny<BsonValue>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowInstance, string[], PropertyDefinition, BsonValue, bool, CancellationToken>((_, _, _, v,
                _, _) => capturedValue = v)
            .Returns(Task.CompletedTask);

        var (service, repo, _, _) = CreateService(DefaultPreviewYaml, ["Admin"],
            answerService: answerMock.Object);
        var instance = BasicInstance();
        repo.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        await service.ImportAsync("TestDef", "Overview",
            [
                new ImportConfirmRow(instance.Id, new Dictionary<string, string>
                {
                    ["Title"] = "   " // whitespace — should short-circuit to BsonNull
                })
            ],
            CancellationToken.None);

        Assert.Equal(BsonNull.Value, capturedValue);
    }
}