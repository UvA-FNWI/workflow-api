namespace UvA.Workflow.Users;

public enum LoginMethod
{
    Uva,
    Vu,
    AmsterdamUmc,
    EduId
}

public interface ILoginMethodClassifier
{
    LoginMethod? Classify(string? userName);
}