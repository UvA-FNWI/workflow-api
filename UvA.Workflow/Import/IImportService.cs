namespace UvA.Workflow.Import;

public interface IImportService
{
    Task<ImportPreview> PreviewAsync(
        Stream fileStream,
        string fileType,
        ColumnMapping[] mappings,
        string workflowDefinition,
        CancellationToken ct);

    Task ImportAsync(
        Stream fileStream,
        string fileType,
        ColumnMapping[] mappings,
        string workflowDefinition,
        CancellationToken ct);
}