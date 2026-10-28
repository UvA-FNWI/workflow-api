using UvA.Workflow.Infrastructure;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Messages;

public class MessageService(
    IMessageRepository repository,
    IWorkflowInstanceRepository instanceRepository,
    ModelService modelService,
    RightsService rightsService,
    IUserService userService,
    IUserRepository userRepository)
{
    /// <summary>
    /// Returns threads visible to the caller. Read authorization uses instance <see cref="RoleAction.View"/>
    /// </summary>
    public async Task<IReadOnlyList<Message>> GetVisibleMessages(string instanceId, CancellationToken ct)
    {
        var instance = await GetAuthorizedInstance(instanceId, RoleAction.View, ct);
        var definition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var messages = await repository.GetMessages(instanceId, ct) ?? [];
        return messages.Where(m => definition.AllowsMessages(m.QuestionName)).ToList();
    }

    public async Task<Message> AddMessage(string instanceId, AddMessageInput input, CancellationToken ct)
    {
        var user = await userService.GetCurrentUser(ct)
                   ?? throw new UnauthorizedAccessException();

        var instance = await GetAuthorizedInstance(instanceId, RoleAction.AddInstanceMessage, ct);
        var definition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];

        if (input.ReplyToId != null)
            return await AddReply(instance, definition, user, input, ct);

        if (input.Kind != ItemKind.Message)
            throw new InvalidWorkflowStateException(instance.Id, "InvalidMessageKind",
                "A new thread must be a message.");

        if (string.IsNullOrWhiteSpace(input.Body))
            throw new ArgumentException("Message body is required.");

        if (!definition.AllowsMessages(input.QuestionName))
            throw new InvalidWorkflowStateException(instance.Id, "MessagesDisabled",
                "Messages are not enabled for this target.");

        var message = new Message
        {
            InstanceId = instance.Id,
            QuestionName = input.QuestionName,
            Items = [CreateItem(user.Id, input with { ParentItemId = null })]
        };
        await repository.AddMessage(message, ct);
        return message;
    }

    public async Task<IReadOnlyDictionary<string, User>> GetAuthors(IEnumerable<Message> messages,
        CancellationToken ct)
    {
        var userIds = messages.SelectMany(m => m.Items).Select(i => i.AuthorId).Distinct().ToList();
        if (userIds.Count == 0)
            return new Dictionary<string, User>();
        return (await userRepository.GetByIds(userIds, ct)).ToDictionary(u => u.Id);
    }

    private async Task<Message> AddReply(WorkflowInstance instance, WorkflowDefinition definition, User user,
        AddMessageInput input, CancellationToken ct)
    {
        var existing = (await repository.GetMessages(instance.Id, ct))
                       .FirstOrDefault(m => m.Id == input.ReplyToId)
                       ?? throw new EntityNotFoundException("Message", input.ReplyToId!);

        if (!definition.AllowsMessages(existing.QuestionName))
            throw new InvalidWorkflowStateException(instance.Id, "MessagesDisabled",
                "Messages are not enabled for this target.");

        switch (input.Kind)
        {
            case ItemKind.Reopen:
                if (!existing.IsClosed)
                    throw new InvalidWorkflowStateException(instance.Id, "MessageOpen",
                        "This conversation is already open.");
                break;
            case ItemKind.Close:
                if (existing.IsClosed)
                    throw new InvalidWorkflowStateException(instance.Id, "MessageClosed",
                        "This conversation is already closed.");
                break;
            case ItemKind.Message:
                if (existing.IsClosed)
                    throw new InvalidWorkflowStateException(instance.Id, "MessageClosed",
                        "This conversation is closed. Reopen it before replying.");
                if (string.IsNullOrWhiteSpace(input.Body))
                    throw new ArgumentException("Message body is required.");
                break;
            default:
                throw new InvalidWorkflowStateException(instance.Id, "InvalidMessageKind",
                    "Unsupported message kind.");
        }

        var itemInput = input.Kind == ItemKind.Message
            ? input with { ParentItemId = ResolveParentItemId(existing, input.ParentItemId) }
            : input with { ParentItemId = null, Body = null };

        var updated = await repository.AddMessageItem(instance.Id, existing.Id, CreateItem(user.Id, itemInput), ct);
        return updated ?? throw new EntityNotFoundException("Message", existing.Id);
    }

    private static string ResolveParentItemId(Message thread, string? parentItemId)
    {
        var root = thread.Items.FirstOrDefault(i => i.Kind == ItemKind.Message)
                   ?? throw new InvalidWorkflowStateException(thread.InstanceId, "InvalidMessageThread",
                       "Conversation has no opening message.");

        var rootId = MessageItemIds.EnsureItemId(root, 0);
        if (string.IsNullOrEmpty(parentItemId) || parentItemId == rootId)
            return rootId;

        var knownIds = thread.Items
            .Where(i => i.Kind == ItemKind.Message)
            .Select((item, index) => MessageItemIds.EnsureItemId(item, index))
            .ToHashSet(StringComparer.Ordinal);

        if (!knownIds.Contains(parentItemId))
            throw new InvalidWorkflowStateException(thread.InstanceId, "InvalidParentItem",
                "Parent message was not found in this conversation.");

        return parentItemId;
    }

    private async Task<WorkflowInstance> GetAuthorizedInstance(string instanceId, RoleAction action,
        CancellationToken ct)
    {
        var instance = await instanceRepository.GetById(instanceId, ct)
                       ?? throw new EntityNotFoundException("WorkflowInstance", instanceId);
        await rightsService.EnsureAuthorizedForAction(instance, action);
        return instance;
    }

    private static MessageItem CreateItem(string authorId, AddMessageInput input) => new()
    {
        Id = MessageItemIds.CreateId(),
        ParentItemId = input.ParentItemId,
        AuthorId = authorId,
        DateTime = DateTime.UtcNow,
        Kind = input.Kind,
        Body = input.Body
    };
}

public record AddMessageInput(
    string? QuestionName,
    string? ReplyToId,
    ItemKind Kind = ItemKind.Message,
    string? Body = null,
    string? ParentItemId = null
);