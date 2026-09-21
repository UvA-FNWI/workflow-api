namespace UvA.Workflow.Users;

public enum LoginMethod
{
    Uva,
    Vu,
    Amc,
    AmsterdamUmc,
    EduId
}

public interface ILoginMethodClassifier
{
    LoginMethod? Classify(string? userName);
}