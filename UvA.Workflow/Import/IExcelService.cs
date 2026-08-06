namespace UvA.Workflow.DocumentIO;

public interface IExcelService
{
    public enum FileImportResult
    {
        Success,
        InvalidSheet,
        InvalidColumns,
        InvalidFile,
        IterationError
    };

    IEnumerable<Dictionary<string, string>> ParseRows(Stream fileStream);

    (FileImportResult, List<Dictionary<int, string>>?, Dictionary<int, string>?) LoadWorksheetData(Stream stream,
        string? sheetName,
        IEnumerable<int> columnIndices);
}