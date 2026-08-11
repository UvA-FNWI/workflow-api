using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Import;

public record ImportPreview(
    ImportPreviewColumn[] Columns,
    List<ImportPreviewRow> Rows
);

public record ImportPreviewColumn(string Name, BilingualString Title, DataType DataType);

public record ImportPreviewRow(
    string InstanceId,
    Dictionary<string, string> Values, // propertyName → parsed value
    ImportPreviewError[] ValidationErrors
);

public record ImportPreviewError(string Column, string Code, BilingualString Message)
{
    public static ImportPreviewError From(ImportPreviewErrorType type, string column) => type switch
    {
        ImportPreviewErrorType.StudentNotFound => new(column, "StudentNotFound",
            new("Student not found", "Student niet gevonden")),
        ImportPreviewErrorType.DuplicateStudent => new(column, "DuplicateStudent",
            new("Student occurs multiple times", "Student komt meerdere keren voor")),
        ImportPreviewErrorType.UnknownColumn => new(column, "UnknownColumn", new("Unknown column", "Onbekende kolom")),
        ImportPreviewErrorType.UserNotFound => new(column, "UserNotFound",
            new("User not found", "Gebruiker niet gevonden")),
        _ => new(column, type.ToString(), new(type.ToString(), type.ToString()))
    };
}

public enum ImportPreviewErrorType
{
    UnknownColumn,
    DuplicateStudent,
    StudentNotFound,
    UserNotFound
}