using System.Text.Json;
using UvA.Workflow.Submissions;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.DocumentIO;


namespace UvA.Workflow.Import;

// TODO: This implementation is made by AI. It needs to be changed into an own version.
public class ImportService(
    IExcelService excelService,
    IWorkflowInstanceRepository workflowInstanceRepository,
    AnswerConversionService answerConversionService,
    AnswerService answerService,
    ModelService modelService)
{
    public async Task<ImportPreview> PreviewAsync(
        Stream fileStream,
        string contentType,
        string workflowDefinition,
        ColumnMapping[] mappings,
        CancellationToken ct)
    {
        var rows = ParseFile(fileStream, contentType);
        var instances = (await workflowInstanceRepository
                .GetByWorkflowDefinition(workflowDefinition, Builders<WorkflowInstance>.Filter.Empty, ct))
            .ToList();

        var previewRows = new List<ImportPreviewRow>();

        foreach (var (row, index) in rows.Select((r, i) => (r, i)))
        {
            var errors = new List<string>();
            var values = new Dictionary<string, string?>();

            // Try to find matching instance by a key column (e.g. "Id")
            var instanceId = row.GetValueOrDefault("Id");
            var instance = instances.FirstOrDefault(i => i.Id == instanceId);

            if (instance == null)
            {
                previewRows.Add(new ImportPreviewRow(
                    instanceId ?? $"row-{index}",
                    values,
                    [$"No instance found for Id '{instanceId}'"]));
                continue;
            }

            foreach (var mapping in mappings)
            {
                if (!row.TryGetValue(mapping.ExcelColumn, out var rawValue))
                    continue;

                var property = modelService.GetProperty(instance, mapping.PropertyName);
                if (property == null)
                {
                    errors.Add($"Property '{mapping.PropertyName}' not found on '{workflowDefinition}'");
                    continue;
                }

                values[mapping.PropertyName] = rawValue;
            }

            previewRows.Add(new ImportPreviewRow(instance.Id, values, errors.ToArray()));
        }

        return new ImportPreview(previewRows.ToArray(), []);
    }

    public async Task ImportAsync(
        Stream fileStream,
        string contentType,
        string workflowDefinition,
        ColumnMapping[] mappings,
        CancellationToken ct)
    {
        var rows = ParseFile(fileStream, contentType);
        var instances = (await workflowInstanceRepository
                .GetByWorkflowDefinition(workflowDefinition, Builders<WorkflowInstance>.Filter.Empty, ct))
            .ToList();

        foreach (var row in rows)
        {
            var instanceId = row.GetValueOrDefault("Id");
            var instance = instances.FirstOrDefault(i => i.Id == instanceId);
            if (instance == null) continue;

            foreach (var mapping in mappings)
            {
                if (!row.TryGetValue(mapping.ExcelColumn, out var rawValue)) continue;

                var property = modelService.GetProperty(instance, mapping.PropertyName);
                if (property == null) continue;

                // Wrap raw string as JsonElement so AnswerConversionService can handle type conversion
                var jsonElement = JsonSerializer.SerializeToElement(rawValue);
                var bsonValue = await answerConversionService.ConvertToValue(jsonElement, property, ct);
                var pathParts = new[] { mapping.PropertyName };

                await answerService.SavePropertyValue(instance, pathParts, property, bsonValue, shouldLog: true, ct);
            }
        }
    }

    private IEnumerable<Dictionary<string, string>> ParseFile(Stream fileStream, string contentType)
    {
        return contentType switch
        {
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" or ".xlsx"
                => excelService.ParseRows(fileStream),
            _ => throw new NotSupportedException($"File type '{contentType}' is not supported for import.")
        };
    }
}