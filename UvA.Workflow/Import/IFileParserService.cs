namespace UvA.Workflow.Import;

public interface IFileParserService
{
    bool CanHandle(string contentType);
    IEnumerable<Dictionary<string, string>> ParseRows(Stream fileStream);
}