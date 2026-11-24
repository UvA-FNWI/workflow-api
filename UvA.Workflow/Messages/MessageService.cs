using UvA.Workflow.Infrastructure;
using UvA.Workflow.WorkflowModel;
using WorkflowAction = UvA.Workflow.WorkflowModel.Action;

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
    /// Returns threads visible to the caller. Read authorization uses <see cref="RoleAction.ViewMessages"/>,
    /// not instance <see cref="RoleAction.View"/>, so participants such as students do not see staff threads.
    /// </summary>
    public async Task<IReadOnlyList<Message>> GetVisibleMessages(string instanceId, CancellationToken ct)
    {
        var (instance, actions) = await GetInstanceAndActions(instanceId, ct, RoleAction.ViewMessages);
        var definition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var messages = await repository.GetMessages(instanceId, ct) ?? [];
        var canViewInstanceThreads = HasTargetPermission(definition, actions, RoleAction.ViewMessages, null) &&
                                     messages.Any(message => string.IsNullOrEmpty(message.QuestionName)) &&
                                     await CanAccessInstanceThreads(instance, definition, ct);
        return messages.Where(message => string.IsNullOrEmpty(message.QuestionName)
            ? canViewInstanceThreads
            : definition.AllowsMessages(message.QuestionName) &&
              HasTargetPermission(definition, actions, RoleAction.ViewMessages, message.QuestionName)).ToList();
    }

    public async Task<Message> AddMessage(string instanceId, AddMessageInput input, CancellationToken ct)
    {
        var user = await userService.GetCurrentUser(ct)
                   ?? throw new UnauthorizedAccessException();

        var (instance, actions) = await GetInstanceAndActions(instanceId, ct, input.ReplyToId == null
            ? [RoleAction.AddMessages]
            : [RoleAction.AddMessages, RoleAction.ViewMessages]);
        var definition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];

        if (input.ReplyToId != null)
            return await AddReply(instance, definition, actions, user, input, ct);

        EnsureTargetPermissions(instance, definition, actions, input.QuestionName, RoleAction.AddMessages);

        if (input.Kind != ItemKind.Message)
            throw new InvalidWorkflowStateException(instance.Id, "InvalidMessageKind",
                "A new thread must be a message.");

        if (string.IsNullOrWhiteSpace(input.Body))
            throw new ArgumentException("Message body is required.");

        await EnsureTargetEnabled(instance, definition, input.QuestionName, ct);

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

    private async Task<Message> AddReply(WorkflowInstance instance, WorkflowDefinition definition,
        WorkflowAction[] actions, User user, AddMessageInput input, CancellationToken ct)
    {
        var existing = (await repository.GetMessages(instance.Id, ct))
                       .FirstOrDefault(m => m.Id == input.ReplyToId)
                       ?? throw new EntityNotFoundException("Message", input.ReplyToId!);

        EnsureTargetPermissions(instance, definition, actions, existing.QuestionName,
            RoleAction.AddMessages, RoleAction.ViewMessages);
        await EnsureTargetEnabled(instance, definition, existing.QuestionName, ct);

        switch (input.Kind)
        {
            case ItemKind.Reopen:
            case ItemKind.Close:
                if (!definition.MessagesAllowClose)
                    throw new InvalidWorkflowStateException(instance.Id, "MessagesCloseDisabled",
                        "Closing conversations is disabled for this workflow.");
                break;
        }

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

    private async Task<(WorkflowInstance Instance, WorkflowAction[] Actions)> GetInstanceAndActions(
        string instanceId, CancellationToken ct, params RoleAction[] requestedActions)
    {
        var instance = await instanceRepository.GetById(instanceId, ct)
                       ?? throw new EntityNotFoundException("WorkflowInstance", instanceId);
        var actions = await rightsService.GetAllowedActions(instance, requestedActions);
        // The first action is required before loading the message target.
        var requiredAction = requestedActions[0];
        if (!actions.Any(action => action.Type == requiredAction))
            throw new ForbiddenWorkflowActionException(instance.Id, requiredAction, null);
        return (instance, actions);
    }

    private static void EnsureTargetPermissions(WorkflowInstance instance, WorkflowDefinition definition,
        WorkflowAction[] actions, string? questionName, params RoleAction[] requiredPermissions)
    {
        foreach (var permission in requiredPermissions)
        {
            if (!HasTargetPermission(definition, actions, permission, questionName))
                throw new ForbiddenWorkflowActionException(instance.Id, permission, null);
        }
    }

    public static bool HasTargetPermission(WorkflowDefinition definition, IEnumerable<WorkflowAction> actions,
        RoleAction permission, string? questionName)
    {
        var grants = actions.Where(action => action.Type == permission);
        if (string.IsNullOrEmpty(questionName))
            return grants.Any(action => action.AllForms.Length == 0 && action.PropertyDefinition == null);

        return grants.Any(action =>
            (action.PropertyDefinition == null || action.PropertyDefinition == questionName) &&
            (action.AllForms.Length == 0 || definition.Forms.Any(form =>
                action.MatchesForm(form.Name) && FormContainsQuestion(form, questionName))));
    }

    private static bool FormContainsQuestion(Form form, string questionName)
    {
        var prefix = form.PropertyName == null ? "" : $"{form.PropertyName}.";
        if (!questionName.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var parts = questionName[prefix.Length..].Split('.');
        var question = form.GetCurrentPages().SelectMany(page => page.Questions)
            .FirstOrDefault(candidate => candidate.Name == parts[0] &&
                                         (candidate.Sources == null || candidate.Sources.Contains(form.PropertyName)));
        for (var index = 1; index < parts.Length; index++)
        {
            if (question is not ObjectPropertyDefinition { WorkflowDefinition: { } embedded } ||
                !embedded.Properties.TryGetValue(parts[index], out question))
                return false;
        }

        return question != null;
    }

    private async Task EnsureTargetEnabled(WorkflowInstance instance, WorkflowDefinition definition,
        string? questionName, CancellationToken ct)
    {
        var enabled = string.IsNullOrEmpty(questionName)
            ? await CanAccessInstanceThreads(instance, definition, ct)
            : definition.AllowsMessages(questionName);
        if (!enabled)
            throw new InvalidWorkflowStateException(instance.Id, "MessagesDisabled",
                "Messages are not enabled for this target.");
    }

    private async Task<bool> CanAccessInstanceThreads(WorkflowInstance instance, WorkflowDefinition definition,
        CancellationToken ct)
    {
        if (!definition.AllowsInstanceMessages())
            return false;

        var cards = definition.InfoCards.Where(card => card.Enabled && card.Type == InfoCardType.Messages).ToArray();
        if (cards.Any(card => card.Sources is not { Length: > 0 } && card.ExcludedSources is not { Length: > 0 }))
            return true;

        var viewerRoles = await rightsService.GetViewerRoles(instance, ct);
        return cards.Any(card => card.IsVisibleToRoles(viewerRoles));
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