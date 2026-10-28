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
            Items = [CreateItem(user.Id, input)]
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

        if (existing.IsClosed)
            throw new InvalidWorkflowStateException(instance.Id, "MessageClosed",
                "This message thread is closed.");

        if (input.Kind == ItemKind.Message && string.IsNullOrWhiteSpace(input.Body))
            throw new ArgumentException("Message body is required.");

        var updated = await repository.AddMessageItem(instance.Id, existing.Id, CreateItem(user.Id, input), ct);
        return updated ?? throw new EntityNotFoundException("Message", existing.Id);
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
    string? Body = null
);