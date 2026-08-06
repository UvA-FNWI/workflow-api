using ClosedXML.Excel;

namespace UvA.Workflow.DocumentIO;

public class ExcelService : IExcelService
{
    public IEnumerable<Dictionary<string, string>> ParseRows(Stream fileStream)
    {
        using var workbook = new XLWorkbook(fileStream);
        var worksheet = workbook.Worksheets.First();
        var headers = worksheet.Row(1).Cells()
            .Select(c => c.GetValue<string>())
            .ToList();

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            var dict = new Dictionary<string, string>();
            for (int i = 0; i < headers.Count; i++)
                dict[headers[i]] = row.Cell(i + 1).GetValue<string>();
            yield return dict;
        }
    }

    public (IExcelService.FileImportResult, List<Dictionary<int, string>>?, Dictionary<int, string>?)
        LoadWorksheetData(Stream stream, string? sheetName, IEnumerable<int> columnIndices)
    {
        // implement as needed
        throw new NotImplementedException();
    }
}