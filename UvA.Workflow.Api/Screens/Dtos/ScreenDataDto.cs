namespace UvA.Workflow.Api.Screens.Dtos;

public record ScreenDataDto(
    string Name,
    string WorkflowDefinition,
    ScreenColumnDto[] Columns,
    ScreenRowDto[] Rows)
{
    public static ScreenDataDto Create(Screen screen, ScreenColumnDto[] columns, ScreenRowDto[] rows)
    {
        return new ScreenDataDto(
            screen.Name,
            screen.WorkflowDefinition ?? "",
            columns,
            rows);
    }
}

public record ScreenColumnDto(
    int Id,
    BilingualString Title,
    string? Property,
    FilterType FilterType,
    DisplayType DisplayType,
    UvA.Workflow.Entities.Domain.SortDirection? DefaultSort,
    bool Link,
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
            dataType);
    }

    private static DataType GetDataType(Column column)
    {
        // Value templates are always text
        if (column.ValueTemplate != null)
            return DataType.String;

        // CurrentStep is always string
        if (column.CurrentStep)
            return DataType.String;

        // Event columns are DateTime
        if (column.Property != null && column.Property.EndsWith("Event"))
            return DataType.DateTime;

        // Use the underlying propertyDefinition's data type if available
        if (column.Question != null)
            return column.Question.DataType;

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