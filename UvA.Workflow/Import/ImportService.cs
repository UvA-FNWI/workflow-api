using System.Text.Json;
using UvA.Workflow.Submissions;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Import;

public class ImportService(
    IEnumerable<IFileParserService> parsers,
    IWorkflowInstanceRepository workflowInstanceRepository,
    AnswerConversionService answerConversionService,
    AnswerService answerService,
    ModelService modelService,
    IUserRepository userRepository)
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
            var errors = new List<ImportError>();
            var studentNumber = row.GetValueOrDefault(studentNumberMapping.ExcelColumn)?.Trim() ?? string.Empty;

            var values = new Dictionary<string, string>();
            foreach (var mapping in mappings)
            {
                if (!row.TryGetValue(mapping.ExcelColumn, out var rawValue)) continue;

                values[mapping.PropertyName] = rawValue;

                var prop = modelService.GetProperty(stub, mapping.PropertyName);

                if (prop == null || string.IsNullOrWhiteSpace(rawValue) || prop.DataType == DataType.String) continue;


                var converted = await ConvertRawValueToBson(prop, rawValue, ct);
                if (converted == BsonNull.Value)
                    errors.Add(ImportError.From(
                        prop.DataType == DataType.User ? ImportErrorType.UserNotFound : ImportErrorType.InvalidDataType,
                        mapping.PropertyName));
            }

            if (string.IsNullOrEmpty(studentNumber))
            {
                errors.Add(ImportError.From(ImportErrorType.StudentNotFound, StudentNameProperty));
                previewRows.Add(new ImportPreviewRow(string.Empty, values, errors.ToArray()));
                continue;
            }

            if (duplicates.Contains(studentNumber))
                errors.Add(ImportError.From(ImportErrorType.DuplicateStudent, StudentNameProperty));

            if (!instanceByStudentNumber.TryGetValue(studentNumber, out var entry))
            {
                errors.Add(ImportError.From(ImportErrorType.StudentNotFound, StudentNameProperty));
                previewRows.Add(new ImportPreviewRow(string.Empty, values, errors.ToArray()));
                continue;
            }

            values[StudentNameProperty] = entry.DisplayName;

            previewRows.Add(new ImportPreviewRow(entry.Instance.Id, values, errors.ToArray()));
        }


        return new ImportPreview(columns, previewRows);
    }

    public async Task ImportAsync(
        string workflowDefinition,
        ImportConfirmRow[] rows,
        CancellationToken ct)
    {
        var stub = new WorkflowInstance { WorkflowDefinition = workflowDefinition };

        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row.InstanceId)) continue;

            var instance = await workflowInstanceRepository.GetById(row.InstanceId, ct);
            if (instance == null) continue;

            foreach (var (propertyName, rawValue) in row.Values)
            {
                if (propertyName is StudentNumberProperty or StudentNameProperty) continue;

                var prop = modelService.GetProperty(stub, propertyName);
                if (prop == null) continue;

                var bsonValue = await ConvertRawValueToBson(prop, rawValue, ct);
                await answerService.SavePropertyValue(instance, [propertyName], prop, bsonValue, shouldLog: true, ct);
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

    private async Task<BsonValue> ConvertRawValueToBson(PropertyDefinition prop, string rawValue, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return BsonNull.Value;

        JsonElement element = prop.DataType switch
        {
            DataType.Int when int.TryParse(rawValue, out var i)
                => JsonSerializer.SerializeToElement(i),
            DataType.Double when double.TryParse(rawValue, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var d)
                => JsonSerializer.SerializeToElement(d),
            DataType.User => await ResolveUserElement(rawValue, ct),
            _
                => JsonSerializer.SerializeToElement(rawValue)
        };

        return await answerConversionService.ConvertToValue(element, prop, ct);
    }

    private async Task<JsonElement> ResolveUserElement(string email, CancellationToken ct)
    {
        var user = await userRepository.GetByEmail(email.Trim(), ct);
        if (user == null) return JsonSerializer.SerializeToElement<object?>(null);

        var userInput = new { userName = user.UserName, displayName = user.DisplayName, email = user.Email };
        return JsonSerializer.SerializeToElement(userInput, AnswerConversionService.Options);
    }

    private IEnumerable<Dictionary<string, string>> ParseFile(Stream fileStream, string contentType)
    {
        var parser = parsers.FirstOrDefault(p => p.CanHandle(contentType))
                     ?? throw new NotSupportedException($"File type '{contentType}' is not supported for import.");
        return parser.ParseRows(fileStream);
    }
}