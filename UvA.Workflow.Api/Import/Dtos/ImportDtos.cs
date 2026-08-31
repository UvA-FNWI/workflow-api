using System.Text.Json;
using UvA.Workflow.Import;

namespace UvA.Workflow.Api.Import.Dtos;

public class ImportPreviewRequest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [FromForm] public required IFormFile File { get; set; }
    [FromForm] public required string ColumnMapping { get; set; }

    public ColumnMapping[] Mappings =>
        JsonSerializer.Deserialize<ColumnMapping[]>(ColumnMapping, JsonOptions)!;
}

public record ImportConfirmRequest(
    ImportConfirmRow[] Rows
);

public record ImportablePropertyDto(string Name, BilingualString Title, DataType DataType);

public record GetColumnNamesResponse(ImportablePropertyDto Identifier, ImportablePropertyDto[] Columns);