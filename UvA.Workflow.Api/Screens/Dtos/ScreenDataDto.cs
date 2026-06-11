using UvA.Workflow.WorkflowModel;
using SortDirection = UvA.Workflow.WorkflowModel.SortDirection;

namespace UvA.Workflow.Api.Screens.Dtos;

public record ScreenDataDto(
    string Name,
    string WorkflowDefinition,
    ScreenColumnDto[] Columns,
    ScreenRowDto[] Rows,
    ScreenGroupDto[]? Groups = null)
{
    public static ScreenDataDto Create(Screen screen, ScreenColumnDto[] columns, ScreenRowDto[] rows,
        ScreenGroupDto[]? groups = null)
    {
        return new ScreenDataDto(
            screen.Name,
            screen.WorkflowDefinition ?? "",
            columns,
            rows,
            groups);
    }
}

public record ScreenGroupDto(
    string Name,
    BilingualString Title,
    ScreenRowDto[] Rows);

public record ScreenColumnDto(
    int Id,
    BilingualString Title,
    string? Property,
    FilterType FilterType,
    DisplayType DisplayType,
    SortDirection? DefaultSort,
    bool Link,
    bool IsCurrentStep,
    DataType DataType)
{
    public static ScreenColumnDto Create(Column column, int id)
    {
        var dataType = GetDataType(column);
        return new ScreenColumnDto(
            id,
            column.DisplayTitle,
            column.Property,
            column.FilterType,
            column.DisplayType,
            column.DefaultSort,
            column.Link,
            column.CurrentStep,
            dataType);
    }

    private static DataType GetDataType(Column column)
    {
        // Value templates are always text
        if (column.ValueTemplate != null)
            return DataType.String;

        if (column.CurrentStep)
            return DataType.Object;

        // Event columns are DateTime
        if (column.Property != null && column.Property.EndsWith("Event"))
            return DataType.DateTime;

        // Use the underlying propertyDefinition's data type if available
        if (column.PropertyDefinition != null)
            return column.PropertyDefinition.DataType;

        // Default to string for anything else
        return DataType.String;
    }
}

public record ScreenRowDto(
    string Id,
    Dictionary<int, object?> Values)
{
    public static ScreenRowDto Create(string id, Dictionary<int, object?> values)
    {
        return new ScreenRowDto(id, values);
    }
}