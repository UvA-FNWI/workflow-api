namespace UvA.Workflow.Users;

public interface ILoginMethodClassifier
{
    Task<string?> ClassifyAsync(string? userName, CancellationToken ct = default);
}