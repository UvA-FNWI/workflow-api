namespace UvA.Workflow.Api.Features.EntityTypes.Dtos;

public record EntityTypeDto(
    string Name,
    BilingualString? Title,
    BilingualString TitlePlural,
    int? Index,
    bool IsAlwaysVisible,
    string? InheritsFrom,
    bool IsEmbedded
);