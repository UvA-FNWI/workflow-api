using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Moq;
using UvA.Workflow.Api.Messages;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Messages;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Controllers;

public class MessagesControllerTests : ControllerTestsBase
{
    private readonly Mock<IMessageRepository> _messageRepoMock = new();

    private MessagesController CreateController()
        => new(new MessageService(
            _messageRepoMock.Object,
            _workflowInstanceRepoMock.Object,
            _modelService,
            _rightsService,
            _userServiceMock.Object,
            _userRepoMock.Object));

    private WorkflowInstance CreateInstance()
    {
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .WithProperties(("Title", b => b.Value("My Thesis")))
            .Build();
        MockInstance(instance);
        _messageRepoMock.Setup(r => r.GetMessages(instance.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        UnitTestsHelpers.AdminUser.DisplayName = "Admin";
        _userRepoMock.Setup(r => r.GetByIds(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([UnitTestsHelpers.AdminUser]);
        return instance;
    }

    [Fact]
    public async Task GetMessages_ThrowsForbidden_WhenUserCannotView()
    {
        var instance = CreateInstance();
        MockCurrentUser("NotARole");

        await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
            CreateController().GetMessages(instance.Id, _ct));
    }

    [Fact]
    public async Task GetMessages_ReturnsThreads_WhenUserCanView()
    {
        var instance = CreateInstance();
        MockCurrentUser("SystemAdmin");
        var stored = new Message
        {
            Id = ObjectId.GenerateNewId().ToString(),
            InstanceId = instance.Id,
            QuestionName = "Title",
            Items =
            [
                new MessageItem
                {
                    AuthorId = UnitTestsHelpers.AdminUser.Id,
                    DateTime = DateTime.UtcNow,
                    Kind = ItemKind.Message,
                    Body = "Please clarify"
                }
            ]
        };
        _messageRepoMock.Setup(r => r.GetMessages(instance.Id, _ct)).ReturnsAsync([stored]);

        var result = await CreateController().GetMessages(instance.Id, _ct);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IEnumerable<MessageDto>>(ok.Value).ToList();
        var dto = Assert.Single(dtos);
        Assert.Equal("Title", dto.QuestionName);
        Assert.Equal("Please clarify", Assert.Single(dto.Items).Body);
    }

    [Fact]
    public async Task GetMessages_ThrowsForbidden_WhenUserCanViewInstanceButNotMessages()
    {
        var instance = CreateInstance();
        MockCurrentUser("Examiner");

        await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
            CreateController().GetMessages(instance.Id, _ct));
    }

    [Fact]
    public async Task GetMessages_HidesExcludedFieldThreads()
    {
        var instance = CreateInstance();
        MockCurrentUser("SystemAdmin");
        _messageRepoMock.Setup(r => r.GetMessages(instance.Id, _ct)).ReturnsAsync(
        [
            new Message
            {
                Id = ObjectId.GenerateNewId().ToString(),
                InstanceId = instance.Id,
                QuestionName = "TurnitinId",
                Items =
                [
                    new MessageItem
                    {
                        AuthorId = UnitTestsHelpers.AdminUser.Id,
                        DateTime = DateTime.UtcNow,
                        Kind = ItemKind.Message,
                        Body = "hidden"
                    }
                ]
            }
        ]);

        var result = await CreateController().GetMessages(instance.Id, _ct);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<MessageDto>>(ok.Value));
    }

    [Theory]
    [InlineData("SupervisorForm", null, true)]
    [InlineData("SupervisorObjectForm", null, true)]
    [InlineData(null, "AssessmentSupervisor.Consent", true)]
    [InlineData("SupervisorForm", "AssessmentSupervisor.Consent", true)]
    [InlineData("SupervisorForm", "AssessmentReviewer.Consent", false)]
    public async Task Messages_EnforcesTargetScopedGrants(string? form, string? property, bool canAccessSupervisor)
    {
        var instance = CreateInstance();
        MockCurrentUser("SystemAdmin");
        var definition = _modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        definition.Forms.Single(candidate => candidate.Name == "AssessmentSupervisor").Name = "SupervisorForm";
        var objectQuestion = definition.Properties.Single(candidate => candidate.Name == "AssessmentSupervisor");
        definition.Forms.Add(new Form
        {
            Name = "SupervisorObjectForm",
            WorkflowDefinition = definition,
            Pages =
            [
                new Page
                {
                    Name = "Questions",
                    PageElements =
                    [
                        new PageElement { Question = objectQuestion.Name, QuestionDefinition = objectQuestion }
                    ]
                }
            ]
        });
        var actions = definition.Roles.Single(role => role.Name == "SystemAdmin").Actions;
        actions.RemoveAll(action => action.Type is RoleAction.ViewMessages or RoleAction.AddMessages);
        actions.Add(new() { Type = RoleAction.ViewMessages, Form = form, PropertyDefinition = property });
        actions.Add(new()
            { Type = RoleAction.AddMessages, Forms = form == null ? [] : [form], PropertyDefinition = property });

        var supervisor = new Message
        {
            Id = "supervisor-thread", InstanceId = instance.Id, QuestionName = "AssessmentSupervisor.Consent"
        };
        var reviewer = new Message
        {
            Id = "reviewer-thread", InstanceId = instance.Id, QuestionName = "AssessmentReviewer.Consent"
        };
        var instanceThread = new Message { Id = "instance-thread", InstanceId = instance.Id };
        _messageRepoMock.Setup(r => r.GetMessages(instance.Id, _ct))
            .ReturnsAsync([supervisor, reviewer, instanceThread]);
        _messageRepoMock.Setup(r => r.AddMessage(It.IsAny<Message>(), _ct)).Returns(Task.CompletedTask);
        var controller = CreateController();

        var result = await controller.GetMessages(instance.Id, _ct);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var visible = Assert.IsAssignableFrom<IEnumerable<MessageDto>>(ok.Value);
        var create = new AddMessageInput(supervisor.QuestionName, null, Body: "Review note");
        if (canAccessSupervisor)
        {
            Assert.Equal(supervisor.Id, Assert.Single(visible).Id);
            var created = await controller.AddMessage(instance.Id, create, _ct);
            Assert.IsType<OkObjectResult>(created.Result);
        }
        else
        {
            Assert.Empty(visible);
            await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
                controller.AddMessage(instance.Id, create, _ct));
        }

        foreach (var target in new[] { reviewer.QuestionName, instanceThread.QuestionName })
            await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
                controller.AddMessage(instance.Id, create with { QuestionName = target }, _ct));

        foreach (var kind in Enum.GetValues<ItemKind>())
            await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
                controller.AddMessage(instance.Id, create with { ReplyToId = reviewer.Id, Kind = kind }, _ct));
        _messageRepoMock.Verify(r => r.AddMessageItem(instance.Id, reviewer.Id, It.IsAny<MessageItem>(), _ct),
            Times.Never);
    }

    [Fact]
    public async Task Messages_EnforcesCardSourcesForInstanceThreadsOnly()
    {
        var instance = CreateInstance();
        MockCurrentUser("SystemAdmin");
        var definition = _modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var messagesCard = Assert.Single(definition.InfoCards, card => card.Type == InfoCardType.Messages);
        messagesCard.ExcludedSources = ["SystemAdmin"];

        var instanceThread = new Message { Id = "instance-thread", InstanceId = instance.Id };
        var fieldThread = new Message { Id = "field-thread", InstanceId = instance.Id, QuestionName = "Title" };
        _messageRepoMock.Setup(r => r.GetMessages(instance.Id, _ct)).ReturnsAsync([instanceThread, fieldThread]);
        var controller = CreateController();

        var result = await controller.GetMessages(instance.Id, _ct);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var visible = Assert.Single(Assert.IsAssignableFrom<IEnumerable<MessageDto>>(ok.Value));
        Assert.Equal(fieldThread.Id, visible.Id);

        var newError = await Assert.ThrowsAsync<InvalidWorkflowStateException>(() =>
            controller.AddMessage(instance.Id, new AddMessageInput(null, null, Body: "Hidden"), _ct));
        Assert.Equal("MessagesDisabled", newError.Code);

        var replyError = await Assert.ThrowsAsync<InvalidWorkflowStateException>(() =>
            controller.AddMessage(instance.Id, new AddMessageInput(null, instanceThread.Id, Body: "Hidden"), _ct));
        Assert.Equal("MessagesDisabled", replyError.Code);
    }

    [Fact]
    public async Task AddMessage_ThrowsForbidden_WhenUserCannotAdd()
    {
        var instance = CreateInstance();
        MockCurrentUser("Examiner");

        await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
            CreateController().AddMessage(instance.Id, new AddMessageInput("Title", null, Body: "Hi"), _ct));
    }

    [Fact]
    public async Task AddMessage_CreatesThread_WhenUserCanAddWithoutViewPermission()
    {
        var instance = CreateInstance();
        MockCurrentUser("SystemAdmin");
        var definition = _modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        definition.Roles.Single(role => role.Name == "SystemAdmin").Actions
            .RemoveAll(action => action.Type == RoleAction.ViewMessages);
        Message? saved = null;
        _messageRepoMock.Setup(r => r.AddMessage(It.IsAny<Message>(), _ct))
            .Callback<Message, CancellationToken>((m, _) =>
            {
                m.Id = ObjectId.GenerateNewId().ToString();
                saved = m;
            })
            .Returns(Task.CompletedTask);

        var result = await CreateController().AddMessage(instance.Id,
            new AddMessageInput("Title", null, Body: "Please clarify"), _ct);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<MessageDto>(ok.Value);
        Assert.NotNull(saved);
        Assert.Equal("Title", dto.QuestionName);
        Assert.Equal("Please clarify", Assert.Single(dto.Items).Body);
        Assert.False(dto.IsClosed);
    }

    [Fact]
    public async Task AddMessage_AcceptsEmbeddedQuestionPath()
    {
        var instance = CreateInstance();
        MockCurrentUser("SystemAdmin");
        Message? saved = null;
        _messageRepoMock.Setup(r => r.AddMessage(It.IsAny<Message>(), _ct))
            .Callback<Message, CancellationToken>((m, _) =>
            {
                m.Id = ObjectId.GenerateNewId().ToString();
                saved = m;
            })
            .Returns(Task.CompletedTask);

        var result = await CreateController().AddMessage(instance.Id,
            new AddMessageInput("AssessmentReviewer.Consent", null, Body: "Check this answer"), _ct);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<MessageDto>(ok.Value);
        Assert.Equal("AssessmentReviewer.Consent", saved?.QuestionName);
        Assert.Equal("AssessmentReviewer.Consent", dto.QuestionName);
    }

    [Fact]
    public async Task AddMessage_RejectsLeafNameOfEmbeddedQuestion()
    {
        var instance = CreateInstance();
        MockCurrentUser("SystemAdmin");

        var error = await Assert.ThrowsAsync<InvalidWorkflowStateException>(() =>
            CreateController().AddMessage(instance.Id, new AddMessageInput("Consent", null, Body: "nope"), _ct));
        Assert.Equal("MessagesDisabled", error.Code);
    }

    [Fact]
    public async Task AddMessage_RejectsExcludedField()
    {
        var instance = CreateInstance();
        MockCurrentUser("SystemAdmin");

        await Assert.ThrowsAsync<InvalidWorkflowStateException>(() =>
            CreateController().AddMessage(instance.Id, new AddMessageInput("TurnitinId", null, Body: "nope"),
                _ct));
    }

    [Theory]
    [InlineData(ItemKind.Message)]
    [InlineData(ItemKind.Close)]
    [InlineData(ItemKind.Reopen)]
    public async Task AddMessage_RejectsThreadUpdate_WhenUserCannotViewItsTarget(ItemKind kind)
    {
        const string storedTarget = "AssessmentSupervisor.Consent";
        const string readableTarget = "AssessmentReviewer.Consent";
        var instance = CreateInstance();
        MockCurrentUser("SystemAdmin");
        var definition = _modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var actions = definition.Roles.Single(role => role.Name == "SystemAdmin").Actions;
        actions.RemoveAll(action => action.Type is RoleAction.ViewMessages or RoleAction.AddMessages);
        actions.Add(new() { Type = RoleAction.AddMessages, PropertyDefinition = storedTarget });
        actions.Add(new() { Type = RoleAction.ViewMessages, PropertyDefinition = readableTarget });

        var thread = new Message
        {
            Id = "supervisor-thread",
            InstanceId = instance.Id,
            QuestionName = storedTarget,
            Items = [new MessageItem { Kind = ItemKind.Message, Body = "Existing note" }]
        };
        if (kind == ItemKind.Reopen)
            thread.Items = [.. thread.Items, new MessageItem { Kind = ItemKind.Close }];
        _messageRepoMock.Setup(r => r.GetMessages(instance.Id, _ct)).ReturnsAsync([thread]);

        var error = await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() => CreateController().AddMessage(
            instance.Id, new AddMessageInput(readableTarget, thread.Id, kind, Body: "New note"), _ct));
        Assert.Contains(nameof(RoleAction.ViewMessages), error.Message);
        _messageRepoMock.Verify(r => r.AddMessageItem(instance.Id, thread.Id, It.IsAny<MessageItem>(), _ct),
            Times.Never);
    }

    [Fact]
    public async Task AddMessage_RepliesAndCloses()
    {
        var instance = CreateInstance();
        MockCurrentUser("SystemAdmin");
        var thread = new Message
        {
            Id = ObjectId.GenerateNewId().ToString(),
            InstanceId = instance.Id,
            QuestionName = null,
            Items =
            [
                new MessageItem
                {
                    AuthorId = UnitTestsHelpers.AdminUser.Id,
                    DateTime = DateTime.UtcNow,
                    Kind = ItemKind.Message,
                    Body = "General note"
                }
            ]
        };
        _messageRepoMock.Setup(r => r.GetMessages(instance.Id, _ct)).ReturnsAsync([thread]);
        _messageRepoMock.Setup(r => r.AddMessageItem(instance.Id, thread.Id, It.IsAny<MessageItem>(), _ct))
            .ReturnsAsync((string _, string _, MessageItem item, CancellationToken _) =>
            {
                thread.Items = thread.Items.Append(item).ToArray();
                return thread;
            });

        var reply = await CreateController().AddMessage(instance.Id,
            new AddMessageInput(null, thread.Id, Body: "Acknowledged"), _ct);
        var replyDto = Assert.IsType<MessageDto>(Assert.IsType<OkObjectResult>(reply.Result).Value);
        Assert.Equal(2, replyDto.Items.Length);
        Assert.False(replyDto.IsClosed);

        var close = await CreateController().AddMessage(instance.Id,
            new AddMessageInput(null, thread.Id, ItemKind.Close), _ct);
        var closeDto = Assert.IsType<MessageDto>(Assert.IsType<OkObjectResult>(close.Result).Value);
        Assert.True(closeDto.IsClosed);

        await Assert.ThrowsAsync<InvalidWorkflowStateException>(() =>
            CreateController().AddMessage(instance.Id,
                new AddMessageInput(null, thread.Id, Body: "too late"), _ct));

        var reopen = await CreateController().AddMessage(instance.Id,
            new AddMessageInput(null, thread.Id, ItemKind.Reopen), _ct);
        var reopenDto = Assert.IsType<MessageDto>(Assert.IsType<OkObjectResult>(reopen.Result).Value);
        Assert.False(reopenDto.IsClosed);

        var afterReopen = await CreateController().AddMessage(instance.Id,
            new AddMessageInput(null, thread.Id, Body: "Thanks"), _ct);
        var afterDto = Assert.IsType<MessageDto>(Assert.IsType<OkObjectResult>(afterReopen.Result).Value);
        Assert.False(afterDto.IsClosed);
        Assert.Equal(5, afterDto.Items.Length);
    }

    [Fact]
    public async Task AddMessage_NestedReply_SetsParentItemId()
    {
        var instance = CreateInstance();
        MockCurrentUser("SystemAdmin");
        var rootId = ObjectId.GenerateNewId().ToString();
        var thread = new Message
        {
            Id = ObjectId.GenerateNewId().ToString(),
            InstanceId = instance.Id,
            QuestionName = null,
            Items =
            [
                new MessageItem
                {
                    Id = rootId,
                    AuthorId = UnitTestsHelpers.AdminUser.Id,
                    DateTime = DateTime.UtcNow,
                    Kind = ItemKind.Message,
                    Body = "Root"
                }
            ]
        };
        _messageRepoMock.Setup(r => r.GetMessages(instance.Id, _ct)).ReturnsAsync([thread]);
        MessageItem? captured = null;
        _messageRepoMock.Setup(r => r.AddMessageItem(instance.Id, thread.Id, It.IsAny<MessageItem>(), _ct))
            .ReturnsAsync((string _, string _, MessageItem item, CancellationToken _) =>
            {
                captured = item;
                thread.Items = thread.Items.Append(item).ToArray();
                return thread;
            });

        await CreateController().AddMessage(instance.Id,
            new AddMessageInput(null, thread.Id, Body: "Nested", ParentItemId: rootId), _ct);

        Assert.NotNull(captured);
        Assert.Equal(rootId, captured.ParentItemId);
        Assert.Equal(ItemKind.Message, captured.Kind);
    }

    [Theory]
    [InlineData(null, "third", true)]
    [InlineData(0, null, false)]
    [InlineData(1, null, true)]
    [InlineData(1, "legacy-3", false)]
    [InlineData(2, "legacy-3", true)]
    [InlineData(2, "third", false)]
    [InlineData(4, "third", true)]
    public async Task AddMessage_EnforcesConfiguredReplyDepth(int? maxReplyDepth, string? parentItemId, bool allowed)
    {
        var instance = CreateInstance();
        MockCurrentUser("SystemAdmin");
        _modelService.WorkflowDefinitions[instance.WorkflowDefinition].Messages!.MaxReplyDepth = maxReplyDepth;
        var authorId = UnitTestsHelpers.AdminUser.Id;
        var thread = new Message
        {
            Id = ObjectId.GenerateNewId().ToString(),
            InstanceId = instance.Id,
            Items =
            [
                new MessageItem { AuthorId = authorId, Body = "Opening message" },
                new MessageItem { AuthorId = authorId, Kind = ItemKind.Close },
                new MessageItem { AuthorId = authorId, Kind = ItemKind.Reopen },
                new MessageItem { AuthorId = authorId, Body = "Legacy direct reply" },
                new MessageItem
                    { AuthorId = authorId, Id = "second", ParentItemId = "legacy-3", Body = "Second level" },
                new MessageItem { AuthorId = authorId, Id = "third", ParentItemId = "second", Body = "Third level" }
            ]
        };
        _messageRepoMock.Setup(r => r.GetMessages(instance.Id, _ct)).ReturnsAsync([thread]);
        _messageRepoMock.Setup(r => r.AddMessageItem(instance.Id, thread.Id, It.IsAny<MessageItem>(), _ct))
            .ReturnsAsync((string _, string _, MessageItem item, CancellationToken _) =>
            {
                thread.Items = [.. thread.Items, item];
                return thread;
            });
        var input = new AddMessageInput(null, thread.Id, Body: "Reply", ParentItemId: parentItemId);
        var controller = CreateController();

        if (allowed)
        {
            var result = await controller.AddMessage(instance.Id, input, _ct);
            var dto = Assert.IsType<MessageDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
            Assert.Equal(parentItemId ?? "legacy-0", dto.Items[^1].ParentItemId);
        }
        else
        {
            var error = await Assert.ThrowsAsync<InvalidWorkflowStateException>(() =>
                controller.AddMessage(instance.Id, input, _ct));
            Assert.Equal("MessageReplyDepthExceeded", error.Code);
            _messageRepoMock.Verify(r => r.AddMessageItem(instance.Id, thread.Id, It.IsAny<MessageItem>(), _ct),
                Times.Never);
        }
    }

    [Fact]
    public async Task AddMessage_ReturnsNotFound_WhenInstanceMissing()
    {
        MockCurrentUser("SystemAdmin");
        var missingId = ObjectId.GenerateNewId().ToString();
        _workflowInstanceRepoMock.Setup(r => r.GetById(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowInstance?)null);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            CreateController().AddMessage(missingId, new AddMessageInput(null, null, Body: "Hi"), _ct));
    }

    [Fact]
    public void MessageItem_Kind_SerializesAsString()
    {
        var message = new Message
        {
            Id = ObjectId.GenerateNewId().ToString(),
            InstanceId = ObjectId.GenerateNewId().ToString(),
            Items =
            [
                new MessageItem
                {
                    AuthorId = ObjectId.GenerateNewId().ToString(),
                    DateTime = DateTime.UtcNow,
                    Kind = ItemKind.Close
                }
            ]
        };

        var document = message.ToBsonDocument();
        var kind = document["Items"].AsBsonArray[0].AsBsonDocument["Kind"];
        Assert.Equal(BsonType.String, kind.BsonType);
        Assert.Equal("Close", kind.AsString);

        var roundTrip = BsonSerializer.Deserialize<Message>(document);
        Assert.Equal(ItemKind.Close, Assert.Single(roundTrip.Items).Kind);
    }

    [Fact]
    public void MessageItem_LegacyIds_SerializeAsStrings()
    {
        var message = new Message
        {
            Id = ObjectId.GenerateNewId().ToString(),
            InstanceId = ObjectId.GenerateNewId().ToString(),
            Items =
            [
                new MessageItem
                {
                    Id = "legacy-0",
                    AuthorId = ObjectId.GenerateNewId().ToString(),
                    DateTime = DateTime.UtcNow,
                    Kind = ItemKind.Message,
                    Body = "Root",
                },
                new MessageItem
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    ParentItemId = "legacy-0",
                    AuthorId = ObjectId.GenerateNewId().ToString(),
                    DateTime = DateTime.UtcNow,
                    Kind = ItemKind.Message,
                    Body = "Reply",
                },
            ],
        };

        var document = message.ToBsonDocument();
        var reply = document["Items"].AsBsonArray[1].AsBsonDocument;
        Assert.Equal(BsonType.String, reply["ParentItemId"].BsonType);
        Assert.Equal("legacy-0", reply["ParentItemId"].AsString);

        var roundTrip = BsonSerializer.Deserialize<Message>(document);
        Assert.Equal("legacy-0", roundTrip.Items[1].ParentItemId);
    }

    [Fact]
    public void AssessmentForms_ExposeMessagesOnDistinctInstancePaths()
    {
        var project = _modelService.WorkflowDefinitions["Project"];
        UvA.Workflow.WorkflowModel.Action[] grants =
        [
            new() { Type = RoleAction.ViewMessages, Form = "AssessmentReviewer" },
            new() { Type = RoleAction.AddMessages, PropertyDefinition = "AssessmentSupervisor.Consent" }
        ];
        var reviewer = FormDto.Create(project.Forms.Single(form => form.Name == "AssessmentReviewer"),
            new ObjectContext([]), "project", grants);
        var supervisor = FormDto.Create(project.Forms.Single(form => form.Name == "AssessmentSupervisor"),
            new ObjectContext([]), "project", grants);

        var reviewerConsent = Consent(reviewer);
        var supervisorConsent = Consent(supervisor);

        Assert.Equal("Assessment_Consent", reviewerConsent.Id);
        Assert.Equal(reviewerConsent.Id, supervisorConsent.Id);
        Assert.Equal(new QuestionMessagesDto("project", "AssessmentReviewer.Consent", CanView: true, CanAdd: false),
            reviewerConsent.Messages);
        Assert.Equal(new QuestionMessagesDto("project", "AssessmentSupervisor.Consent", CanView: false, CanAdd: true),
            supervisorConsent.Messages);
    }

    private static QuestionDto Consent(FormDto form)
        => form.Pages.SelectMany(page => page.Elements).Select(element => element.Question)
            .Single(question => question?.Name == "Consent")!;
}