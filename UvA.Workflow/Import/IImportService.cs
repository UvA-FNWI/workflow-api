using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Import;

public interface IImportService
{
    Task<PropertyDefinition[]> GetEditableImportableProperties(string workflowDefinition, string[] bulkEditProperties);

    Task<ImportPreview> PreviewAsync(
        string workflowDefinition,
        string screenName,
        Stream fileStream,
        string contentType,
        ColumnMapping[] mappings,
        CancellationToken ct);

    Task ImportAsync(
        string workflowDefinition,
        string screenName,
        ImportConfirmRow[] rows,
        CancellationToken ct);
}