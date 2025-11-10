namespace UvA.Workflow.Api.EntityTypes.Dtos;

public record EntityTypeDto(
    string Name,
    BilingualString? Title,
    BilingualString TitlePlural,
    int? Index,
    bool IsAlwaysVisible,
    string? InheritsFrom,
    bool IsEmbedded,
    string[] Screens
)
{
    public static EntityTypeDto Create(EntityType entityType)
    {
        return new EntityTypeDto(
            entityType.Name,
            entityType.Title,
            entityType.TitlePlural,
            entityType.Index,
            entityType.IsAlwaysVisible,
            entityType.InheritsFrom,
            entityType.IsEmbedded,
            entityType.Screens.Keys.ToArray()
        );
    }
}