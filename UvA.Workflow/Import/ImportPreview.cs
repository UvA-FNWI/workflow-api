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
    string[] ValidationErrors
);

public enum ImportPreviewErrorType
{
    UnknownColumn,
    DuplicateStudent,
    StudentNotFound,
    UserNotFound
}