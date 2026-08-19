using System.Text.Json;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Submissions;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Import;

public class ImportService(
    IEnumerable<IFileParserService> parsers,
    IWorkflowInstanceRepository workflowInstanceRepository,
    AnswerConversionService answerConversionService,
    IAnswerService answerService,
    ModelService modelService,
    IUserRepository userRepository,
    RightsService rightsService) : IImportService
{
    public bool IsImportableType(DataType dt) => dt is
        DataType.String or DataType.Int or DataType.Double or
        DataType.Date or DataType.DateTime or DataType.User;

    public async Task<PropertyDefinition[]> GetEditableImportableProperties(
        string workflowDefinition, string[] bulkEditProperties)
    {
        var definition = modelService.WorkflowDefinitions[workflowDefinition];
        var stub = new WorkflowInstance { WorkflowDefinition = workflowDefinition };
        // Collect edit actions from all roles that apply to this workflow definition,
        var wfKey = workflowDefinition.Split('/')[0];
        var editActions = modelService.Roles.Values
            .SelectMany(r => r.Actions.Where(a =>
                a.Type == RoleAction.Edit &&
                (a.WorkflowDefinition == null || a.WorkflowDefinition == wfKey)))
            .Distinct()
            .ToArray();
        var editableMap = rightsService.CanEditProperties(stub, bulkEditProperties, editActions);

        return definition.Properties
            .Where(p => editableMap.GetValueOrDefault(p.Name) && IsImportableType(p.DataType))
            .ToArray();
    }

    public async Task<ImportPreview> PreviewAsync(
        string workflowDefinition,
        string screenName,
        Stream fileStream,
        string contentType,
        ColumnMapping[] mappings,
        CancellationToken ct)
    {
        var bulkEditConfig = modelService.WorkflowDefinitions[workflowDefinition]
                                 .Screens.FirstOrDefault(s => s.Name == screenName)?.BulkEdit
                             ?? throw new InvalidOperationException(
                                 $"Screen '{screenName}' does not support bulk edit.");

        var identifierMapping = mappings.FirstOrDefault(m => m.PropertyName == bulkEditConfig.Identifier.Property)
                                ?? throw new InvalidOperationException(
                                    $"No mapping provided for identifier property '{bulkEditConfig.Identifier.Property}'.");


        // Define the columns, consisting of the identifier column,
        // read only columns and data columns
        var stub = new WorkflowInstance { WorkflowDefinition = workflowDefinition };

        var identifierProperty = modelService.GetProperty(stub, bulkEditConfig.Identifier.Property.Split('.'))
                                 ?? throw new InvalidOperationException(
                                     $"Property '{bulkEditConfig.Identifier.Property}' defined as BulkEdit identifier was not found on workflow '{workflowDefinition}'.");

        var identifierColumn = ImportPreviewColumn.FromBulkEditProperty(bulkEditConfig.Identifier,
            identifierProperty);

        var readOnlyColumns = bulkEditConfig.ReadOnlyProperties != null
            ? bulkEditConfig.ReadOnlyProperties.Select(p =>
                ImportPreviewColumn.FromBulkEditProperty(p, modelService.GetProperty(stub, p.Property.Split('.'))!))
            : [];

        var dataMappings = mappings.Where(m => m.PropertyName != bulkEditConfig.Identifier.Property).ToArray();

        var dataColumns = dataMappings
            .Select(m =>
            {
                var prop = modelService.GetProperty(stub, m.PropertyName.Split('.'));
                return prop == null ? null : new ImportPreviewColumn(m.PropertyName, prop.DisplayName, prop.DataType);
            })
            .OfType<ImportPreviewColumn>()
            .ToArray();

        var columns = new ImportPreviewColumn[] { identifierColumn }.Concat(readOnlyColumns).Concat(dataColumns)
            .ToArray();

        // Get the rows from the file and get all related instances
        var rows = ParseFile(fileStream, contentType).ToList();

        var identifierValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var sn = row.GetValueOrDefault(identifierMapping.ExcelColumn)?.Trim();
            if (string.IsNullOrEmpty(sn)) continue;
            if (!identifierValues.Add(sn)) duplicates.Add(sn);
        }

        var identifierFilter = Builders<WorkflowInstance>.Filter.In(
            $"Properties.{bulkEditConfig.Identifier.Property}",
            identifierValues);
        var instances =
            await workflowInstanceRepository.GetByWorkflowDefinition(workflowDefinition, identifierFilter, ct);
        var instanceByIdentifier = BuildIdentifierLookup(instances, bulkEditConfig);

        // Fill the rows for all instances
        var previewRows = new List<ImportPreviewRow>(rows.Count);
        foreach (var row in rows)
        {
            var errors = new List<ImportError>();
            var identifier = row.GetValueOrDefault(identifierMapping.ExcelColumn)?.Trim() ?? string.Empty;

            var values = new Dictionary<string, string>();
            foreach (var mapping in mappings)
            {
                if (!row.TryGetValue(mapping.ExcelColumn, out var rawValue)) continue;

                values[mapping.PropertyName] = rawValue;

                var prop = modelService.GetProperty(stub, mapping.PropertyName.Split('.'));

                if (prop == null || string.IsNullOrWhiteSpace(rawValue) || prop.DataType == DataType.String) continue;


                var converted = await ConvertRawValueToBson(prop, rawValue, ct);
                if (converted == BsonNull.Value)
                    errors.Add(ImportError.From(
                        prop.DataType == DataType.User ? ImportErrorType.UserNotFound : ImportErrorType.InvalidDataType,
                        mapping.PropertyName));
            }

            if (string.IsNullOrEmpty(identifier))
            {
                errors.Add(ImportError.From(ImportErrorType.EntryNotFound, bulkEditConfig.Identifier.Property));
                previewRows.Add(new ImportPreviewRow(string.Empty, values, errors.ToArray()));
                continue;
            }

            if (duplicates.Contains(identifier))
                errors.Add(ImportError.From(ImportErrorType.DuplicateEntry, bulkEditConfig.Identifier.Property));

            if (!instanceByIdentifier.TryGetValue(identifier, out var instance))
            {
                errors.Add(ImportError.From(ImportErrorType.EntryNotFound, bulkEditConfig.Identifier.Property));
                previewRows.Add(new ImportPreviewRow(string.Empty, values, errors.ToArray()));
                continue;
            }

            values[bulkEditConfig.Identifier.Property] = identifier;

            var editActions =
                await rightsService.GetAllowedActions(instance, RightsEvaluationMode.RequestContext, RoleAction.Edit);

            if (editActions.Length == 0)
            {
                errors.Add(ImportError.From(ImportErrorType.NotAllowed, bulkEditConfig.Identifier.Property));
            }
            else
            {
                var canEdit =
                    rightsService.CanEditProperties(instance, dataMappings.Select(m => m.PropertyName), editActions);

                errors.AddRange(dataMappings
                    .Where(m => !canEdit.GetValueOrDefault(m.PropertyName))
                    .Select(m => ImportError.From(ImportErrorType.NotAllowed, m.PropertyName)));
            }


            foreach (var roProp in bulkEditConfig.ReadOnlyProperties ?? [])
            {
                var bson = instance.GetProperty(roProp.Property.Split('.'));
                if (bson != null && bson != BsonNull.Value)
                    values[roProp.Property] = bson.ToString() ?? "";
            }

            previewRows.Add(new ImportPreviewRow(instance.Id, values, errors.ToArray()));
        }


        return new ImportPreview(columns, previewRows);
    }

    public async Task ImportAsync(
        string workflowDefinition,
        string screenName,
        ImportConfirmRow[] rows,
        CancellationToken ct)
    {
        var bulkEditConfig = modelService.WorkflowDefinitions[workflowDefinition]
                                 .Screens.FirstOrDefault(s => s.Name == screenName)?.BulkEdit
                             ?? throw new InvalidOperationException(
                                 $"Screen '{screenName}' does not support bulk edit.");

        var stub = new WorkflowInstance { WorkflowDefinition = workflowDefinition };
        var readOnlyProperties = bulkEditConfig.ReadOnlyProperties?.Select(p => p.Property).ToArray()
                                 ?? [];
        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row.InstanceId)) continue;

            var instance = await workflowInstanceRepository.GetById(row.InstanceId, ct);
            if (instance == null) continue;

            var instanceEditActions =
                await rightsService.GetAllowedActions(instance, RightsEvaluationMode.RequestContext, RoleAction.Edit);
            if (instanceEditActions.Length == 0)
                throw new ForbiddenWorkflowActionException(instance.Id, RoleAction.Edit, null);

            var propertiesToWrite = row.Values.Keys
                .Where(p => p != bulkEditConfig.Identifier.Property && !readOnlyProperties.Contains(p))
                .ToArray();

            var canEditPropertiesDictionary =
                rightsService.CanEditProperties(instance, propertiesToWrite, instanceEditActions);

            foreach (var propertyName in propertiesToWrite)
            {
                if (!canEditPropertiesDictionary[propertyName])
                    throw new ForbiddenWorkflowActionException(instance.Id, RoleAction.Edit, propertyName);

                var prop = modelService.GetProperty(stub, propertyName.Split('.'));
                if (prop == null)
                    throw new InvalidOperationException(
                        $"Property '{propertyName}' does not exist on workflow definition '{workflowDefinition}'.");
                ;

                var rawValue = row.Values[propertyName];
                var bsonValue = await ConvertRawValueToBson(prop, rawValue, ct);
                await answerService.SavePropertyValue(instance, propertyName.Split('.'), prop, bsonValue,
                    shouldLog: true, ct);
            }
        }
    }

    /// <summary>
    /// Builds a case-insensitive lookup of Identifier → WorkflowInstance
    /// by reading the Identifier property from each instance.
    /// </summary>
    private static Dictionary<string, WorkflowInstance> BuildIdentifierLookup(
        IEnumerable<WorkflowInstance> instances, BulkEditConfig bulkEditConfig)
    {
        var lookup = new Dictionary<string, WorkflowInstance>(StringComparer.OrdinalIgnoreCase);
        var pathParts = bulkEditConfig.Identifier.Property.Split('.');

        foreach (var instance in instances)
        {
            var bson = instance.GetProperty(pathParts);
            var key = bson?.ToString()?.Trim();
            if (string.IsNullOrEmpty(key)) continue;
            lookup.TryAdd(key, instance);
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