namespace UvA.Workflow.Users;

public interface ILoginMethodClassifier
{
    string? Classify(string? userName);
}