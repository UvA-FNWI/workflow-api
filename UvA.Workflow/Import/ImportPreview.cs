namespace UvA.Workflow.Import;

public record ImportPreview(
    ImportPreviewRow[] Rows,
    string[] Errors
);

public record ImportPreviewRow(
    string InstanceId,
    Dictionary<string, string> Values, // propertyName → parsed value
    string[] ValidationErrors
);