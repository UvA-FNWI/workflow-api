using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Messages;

namespace UvA.Workflow.Api.Messages;

public class MessagesController(MessageService messageService) : ApiControllerBase
{
    [HttpGet("{instanceId}")]
    public async Task<ActionResult<IEnumerable<MessageDto>>> GetMessages(string instanceId, CancellationToken ct)
    {
        var messages = await messageService.GetVisibleMessages(instanceId, ct);
        return Ok(await ToDtos(messages, ct));
    }

    [HttpPost("{instanceId}")]
    public async Task<ActionResult<MessageDto>> AddMessage(string instanceId, AddMessageInput input,
        CancellationToken ct)
    {
        var message = await messageService.AddMessage(instanceId, input, ct);
        return Ok((await ToDtos([message], ct))[0]);
    }

    private async Task<IReadOnlyList<MessageDto>> ToDtos(IReadOnlyList<Message> messages, CancellationToken ct)
    {
        var users = await messageService.GetAuthors(messages, ct);
        return messages.Select(m => MessageDto.Create(m, users)).ToList();
    }
}

public record MessageDto(string Id, string? QuestionName, bool IsClosed, MessageItemDto[] Items)
{
    public static MessageDto Create(Message message, IReadOnlyDictionary<string, User> users) => new(
        message.Id,
        message.QuestionName,
        message.IsClosed,
        message.Items.Select((item, index) => MessageItemDto.Create(item, index, users)).ToArray());
}

public record MessageUserDto(string Id, string DisplayName);

public record MessageItemDto(
    string Id,
    string? ParentItemId,
    DateTime DateTime,
    MessageUserDto User,
    string? Body,
    ItemKind Kind)
{
    public static MessageItemDto Create(MessageItem item, int index, IReadOnlyDictionary<string, User> users)
    {
        var user = users.GetValueOrDefault(item.AuthorId);
        return new(
            MessageItemIds.EnsureItemId(item, index),
            item.ParentItemId,
            item.DateTime,
            new MessageUserDto(item.AuthorId, user?.DisplayName ?? user?.UserName ?? item.AuthorId),
            item.Body,
            item.Kind);
    }
}