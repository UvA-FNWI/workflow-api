using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.Users.Dtos;

namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public enum InstanceHistoryType
{
    Event,
    Property,
    Undo
}

public record InstanceHistoryItemDto(
    string Id,
    InstanceHistoryType Type,
    BilingualString Title,
    DateTime Timestamp,
    UserSearchResultDto User,
    PropertyHistoryDto? PropertyHistory,
    UndoHistoryDto? UndoHistory
);

public record PropertyHistoryDto(
    QuestionDto Question,
    AnswerChangeGroupDto AnswerChangeGroup
);

public record UndoHistoryDto(string? Reason);