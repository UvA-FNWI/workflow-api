using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Moq;
using UvA.Workflow.Api.Messages;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Messages;
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
        MockCurrentUser("Examiner");
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
    public async Task GetMessages_HidesExcludedFieldThreads()
    {
        var instance = CreateInstance();
        MockCurrentUser("Examiner");
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

    [Fact]
    public async Task AddMessage_ThrowsForbidden_WhenUserCannotAdd()
    {
        var instance = CreateInstance();
        MockCurrentUser("Examiner");

        await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
            CreateController().AddMessage(instance.Id, new AddMessageInput("Title", null, Body: "Hi"), _ct));
    }

    [Fact]
    public async Task AddMessage_CreatesThread_WhenUserCanAdd()
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
            new AddMessageInput("Title", null, Body: "Please clarify"), _ct);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<MessageDto>(ok.Value);
        Assert.NotNull(saved);
        Assert.Equal("Title", dto.QuestionName);
        Assert.Equal("Please clarify", Assert.Single(dto.Items).Body);
        Assert.False(dto.IsClosed);
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
}