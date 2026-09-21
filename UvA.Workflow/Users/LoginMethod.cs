namespace UvA.Workflow.Users;

public enum LoginMethod
{
    Uva,
    AmsterdamUmc,
    EduId
}

public interface ILoginMethodClassifier
{
    LoginMethod? Classify(string? userName);
}