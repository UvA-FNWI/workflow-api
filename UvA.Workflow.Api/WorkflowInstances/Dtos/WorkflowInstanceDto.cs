using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.Users.Dtos;
using UvA.Workflow.Api.WorkflowDefinitions.Dtos;
using UvA.Workflow.Events;
using UvA.Workflow.Notifications;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public record WorkflowInstanceBasicDto(
    string Id,
    string? CurrentStep
);

public record WorkflowInstanceDto(
    string Id,
    string? Title,
    WorkflowDefinitionDto WorkflowDefinition,
    string? CurrentStep,
    string? ParentId,
    ActionDto[] Actions,
    FieldDto[] Fields,
    StepDto[] Steps,
    SubmissionDto[] Submissions,
    RoleAction[] Permissions,
    bool CanUseAdminTools,
    bool CanImpersonate,
    string[] ViewerRoles,
    InfoCardDto[] InfoCards
);

public record FieldDto(string? Key, BilingualString Title, object? Value, bool IsHighlighted, int? Order);

public record StepVersionDto
{
    public int VersionNumber { get; init; }
    public List<string> EventIds { get; init; } = [];
    public DateTime SubmittedAt { get; init; }
    public List<SubmissionDto> Submissions { get; init; } = [];
}

public record StepHeaderStatusDto(
    StepHeaderPillType Type,
    BilingualString Label
);

public record StepDto(
    string Id,
    BilingualString Title,
    Icon? Icon,
    string? Event,
    DateTime? DateCompleted,
    DateTimeOffset? Deadline,
    StepDto[]? Children,
    StepHeaderStatusDto? HeaderStatus,
    StepResultsType ResultsType,
    bool ExpectsSubmission,
    bool HasSubmission,
    StepHierarchyMode HierarchyMode = StepHierarchyMode.Sequential,
    List<StepVersionDto>? Versions = null);

public record ActionDto(
    ActionType Type,
    BilingualString Title,
    string? Form = null,
    string? Name = null,
    string? UserId = null,
    MailMessage? Mail = null,
    string? Property = null,
    string[] Steps = null!,
    ActionIntent Intent = ActionIntent.Primary,
    FormLayout? FormLayout = null,
    bool AutoOpenForm = false
)
{
    public string Id => $"{Type}_{Name ?? Property ?? Form ?? UserId}";

    public static ActionDto Create(InstanceService.AllowedAction action)
    {
        var steps = action.DisplaySteps ?? [];

        ActionDto dto = action.Action.Type switch
        {
            RoleAction.CreateRelatedInstance => new(
                ActionType.CreateInstance,
                action.Action.Label ?? Add(action.WorkflowDefinition?.DisplayTitle ?? "form"),
                Form: action.Action.Property
            ),
            RoleAction.Execute => new(
                ActionType.Execute,
                action.Action.Label ?? action.Action.Name ?? "Action",
                Name: action.Action.Name,
                Mail: action.Mail
            ),
            RoleAction.Submit => new(
                ActionType.SubmitForm,
                action.Action.Label ?? Add(action.Form?.Name ?? "form"),
                Form: action.Form?.Name,
                FormLayout: action.Form?.Layout,
                AutoOpenForm: !action.Action.NoAutoOpenForm
            ),
            _ => throw new ArgumentOutOfRangeException()
        };
        return dto with
        {
            Steps = steps,
            Intent = action.Action.Intent
        };
    }

    private static BilingualString Add(BilingualString target) =>
        new($"Add {target.En.ToLower()}", $"{target.Nl} toevoegen");
}

public record InstanceEventDto(
    string Id,
    DateTime? Date
)
{
    /// <summary>
    /// Creates an InstanceEventDto from an InstanceEvent domain entity
    /// </summary>
    public static InstanceEventDto Create(InstanceEvent instanceEvent)
    {
        return new InstanceEventDto(instanceEvent.Id, instanceEvent.Date);
    }
}

public record RelatedUserRolesDto(
    string Role,
    BilingualString Title,
    UserDto[] Users,
    bool AllowsExternalUsers,
    bool AllowsAssignment,
    bool AllowsMultipleUsers,
    bool CanEdit
);

public record RelatedUserGroupDto(
    string Name,
    BilingualString Title,
    RelatedUserRolesDto[] UserRoles
);

public record InfoCardItemDto(
    string Name,
    InfoCardItemType Type,
    BilingualString Text,
    BilingualString Url
)
{
    public static InfoCardItemDto? TryCreate(InfoCardItem item, ObjectContext context)
    {
        var url = item.UrlTemplate?.Apply(context);
        return url == null || string.IsNullOrWhiteSpace(url.En) && string.IsNullOrWhiteSpace(url.Nl)
            ? null
            : new InfoCardItemDto(item.Name, item.Type, item.Text, url);
    }
}

public record InfoCardUserDto(string DisplayName, string? Picture);

public record InfoCardFieldDto(
    BilingualString Title,
    object Value,
    string? Href,
    string? Icon
);

public record InfoCardDto(
    string Name,
    BilingualString Title,
    InfoCardType Type,
    InfoCardUserDto? User = null,
    InfoCardFieldDto[]? Fields = null,
    BilingualString? EmptyText = null,
    RelatedUserGroupDto[]? Groups = null,
    InfoCardItemDto[]? Items = null,
    BilingualString? Content = null
);