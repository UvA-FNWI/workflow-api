using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using UvA.Workflow.Import;

namespace UvA.Workflow.ImportExport;

public class CsvService : IFileParserService
{
    public bool CanHandle(string contentType) =>
        contentType is "text/csv" or "application/csv" or ".csv";

    public IEnumerable<Dictionary<string, string>> ParseRows(Stream fileStream)
    {
        using var reader = new StreamReader(fileStream, leaveOpen: true);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
        };
        using var csv = new CsvReader(reader, config);

        csv.Read();
        csv.ReadHeader();
        var headers = csv.HeaderRecord!;

        while (csv.Read())
        {
            var dict = new Dictionary<string, string>();
            foreach (var header in headers)
                dict[header] = csv.GetField(header) ?? string.Empty;
            yield return dict;
        }
    }
}