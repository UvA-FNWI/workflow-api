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
    ImportError[] ValidationErrors
);

public record ImportConfirmRow(
    string InstanceId,
    Dictionary<string, string> Values // propertyName → raw string value
);

public record ImportError(string Column, string Code, BilingualString Message)
{
    public static ImportError From(ImportErrorType type, string column) => type switch
    {
        ImportErrorType.StudentNotFound => new(column, "StudentNotFound",
            new("Student not found", "Student niet gevonden")),
        ImportErrorType.DuplicateStudent => new(column, "DuplicateStudent",
            new("Student occurs multiple times", "Student komt meerdere keren voor")),
        ImportErrorType.UnknownColumn => new(column, "UnknownColumn", new("Unknown column", "Onbekende kolom")),
        ImportErrorType.UserNotFound => new(column, "UserNotFound",
            new("User not found", "Gebruiker niet gevonden")),
        ImportErrorType.InvalidDataType => new(column, "InvalidDataType",
            new("Value does not match the expected data type",
                "Waarde komt niet overeen met het verwachte gegevenstype")),
        _ => new(column, type.ToString(), new(type.ToString(), type.ToString()))
    };
}

public enum ImportErrorType
{
    UnknownColumn,
    DuplicateStudent,
    StudentNotFound,
    UserNotFound,
    InvalidDataType
}