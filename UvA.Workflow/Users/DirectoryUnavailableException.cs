namespace UvA.Workflow.Users;

/// <summary>
/// The directory could not answer a search. Callers should tell the user the directory
/// is unavailable rather than presenting an empty result as "nobody matched".
/// </summary>
public class DirectoryUnavailableException : Exception
{
    public DirectoryUnavailableException()
        : base("The user directory is unavailable.")
    {
    }
}