namespace UvA.Workflow.WorkflowModel;

public class Role : INamed, IDeclaredKeys
{
    /// <summary>
    /// Internal name of this role
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Localized title of the role shown to the user
    /// </summary>
    public BilingualString? Title { get; set; }

    /// <summary>
    /// Order in which this role should be shown
    /// </summary>
    public int Order { get; set; } = int.MaxValue;

    /// <summary>
    /// List of roles to inherit actions from 
    /// </summary>
    public string[] InheritFrom { get; set; } = [];

    /// <summary>
    /// List of global actions for this role
    /// </summary>
    public List<Action> Actions { get; set; } = [];

    public BilingualString DisplayTitle => Title ?? Name;

    [YamlIgnore] public HashSet<string> DeclaredKeys { get; set; } = new();

    public Role Clone()
    {
        var clone = (Role)MemberwiseClone();
        clone.Actions = new List<Action>(Actions);
        return clone;
    }
}

public enum NotificationType
{
    NewInstanceMessage
}