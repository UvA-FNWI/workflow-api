using System.Text.Json;
using UvA.Workflow.Submissions;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.DocumentIO;


namespace UvA.Workflow.Import;

public class ImportService(
    IExcelService excelService,
    IWorkflowInstanceRepository workflowInstanceRepository,
    AnswerConversionService answerConversionService,
    AnswerService answerService,
    ModelService modelService)
{
    private const string StudentNumberProperty = "UserName";
    private const string StudentNameProperty = "DisplayName";

    public bool IsImportableType(DataType dt) => dt is
        DataType.String or DataType.Int or DataType.Double or
        DataType.Date or DataType.DateTime or DataType.User;

    public async Task<ImportPreview> PreviewAsync(
        Stream fileStream,
        string contentType,
        string workflowDefinition,
        ColumnMapping[] mappings,
        CancellationToken ct)
    {
        var studentNumberMapping = mappings.FirstOrDefault(m => m.PropertyName == StudentNumberProperty)
                                   ?? throw new InvalidOperationException(
                                       "No StudentNumber column mapping was provided.");

        var dataMappings = mappings.Where(m => m.PropertyName != StudentNumberProperty).ToArray();

        var studentNumberColumn = new ImportPreviewColumn(StudentNumberProperty,
            new BilingualString("Student Number", "Studentnummer"), DataType.String);

        var studentNameColumn = new ImportPreviewColumn(StudentNameProperty,
            new BilingualString("Student Name (from database)", "Naam student (uit database)"), DataType.String);

        var stub = new WorkflowInstance { WorkflowDefinition = workflowDefinition };

        var dataColumns = dataMappings
            .Select(m =>
            {
                var prop = modelService.GetProperty(stub, m.PropertyName);
                return prop == null ? null : new ImportPreviewColumn(m.PropertyName, prop.DisplayName, prop.DataType);
            })
            .OfType<ImportPreviewColumn>()
            .ToArray();

        var columns = new[] { studentNumberColumn, studentNameColumn }.Concat(dataColumns).ToArray();

        var rows = ParseFile(fileStream, contentType).ToList();

        var studentNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var sn = row.GetValueOrDefault(studentNumberMapping.ExcelColumn)?.Trim();
            if (string.IsNullOrEmpty(sn)) continue;
            if (!studentNumbers.Add(sn)) duplicates.Add(sn);
        }

        var studentFilter =
            Builders<WorkflowInstance>.Filter.In($"Properties.Student.{StudentNumberProperty}", studentNumbers);
        var instances = await workflowInstanceRepository.GetByWorkflowDefinition(workflowDefinition, studentFilter, ct);
        var instanceByStudentNumber = BuildStudentLookup(instances);

        var previewRows = new List<ImportPreviewRow>(rows.Count);
        foreach (var row in rows)
        {
            var errors = new List<ImportPreviewError>();
            var studentNumber = row.GetValueOrDefault(studentNumberMapping.ExcelColumn)?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(studentNumber))
            {
                errors.Add(ImportPreviewError.From(ImportPreviewErrorType.StudentNotFound, StudentNameProperty));
                previewRows.Add(new ImportPreviewRow(string.Empty, [], errors.ToArray()));
                continue;
            }

            if (duplicates.Contains(studentNumber))
                errors.Add(ImportPreviewError.From(ImportPreviewErrorType.DuplicateStudent, StudentNameProperty));

            if (!instanceByStudentNumber.TryGetValue(studentNumber, out var entry))
            {
                errors.Add(ImportPreviewError.From(ImportPreviewErrorType.StudentNotFound, StudentNameProperty));
                previewRows.Add(new ImportPreviewRow(string.Empty,
                    new Dictionary<string, string> { [StudentNumberProperty] = studentNumber }, errors.ToArray()));
                continue;
            }

            var values = new Dictionary<string, string>();
            values[StudentNumberProperty] = studentNumber;
            values[StudentNameProperty] = entry.DisplayName;
            foreach (var mapping in mappings)
            {
                if (row.TryGetValue(mapping.ExcelColumn, out var rawValue))
                    values[mapping.PropertyName] = rawValue;
            }

            previewRows.Add(new ImportPreviewRow(entry.Instance.Id, values, errors.ToArray()));
        }


        return new ImportPreview(columns, previewRows);
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

    /// <summary>
    /// Builds a case-insensitive lookup of StudentNumber → WorkflowInstance
    /// by reading the Student user property from each instance.
    /// </summary>
    private static Dictionary<string, (WorkflowInstance Instance, string DisplayName)> BuildStudentLookup(
        IEnumerable<WorkflowInstance> instances)
    {
        var lookup = new Dictionary<string, (WorkflowInstance, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var instance in instances)
        {
            var studentBson = instance.GetProperty("Student");
            if (studentBson is not BsonDocument studentDoc) continue;
            if (!studentDoc.TryGetValue("UserName", out var userName)) continue;

            var key = userName.AsString?.Trim();
            if (string.IsNullOrEmpty(key)) continue;

            var displayName = studentDoc.TryGetValue("DisplayName", out var dn) ? dn.AsString ?? "" : "";

            lookup.TryAdd(key, (instance, displayName));
        }

        return lookup;
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