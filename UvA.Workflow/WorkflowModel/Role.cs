namespace UvA.Workflow.Entities.Domain;

public class Role
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
    /// List of roles to inherit actions from 
    /// </summary>
    public string[] InheritFrom { get; set; } = [];

    /// <summary>
    /// List of global actions for this role
    /// </summary>
    public List<Action> Actions { get; set; } = [];

    public BilingualString DisplayTitle => Title ?? Name;
}

public enum NotificationType
{
    NewInstanceMessage
}

public enum RoleAction
{
    View,
    Submit,
    Edit,
    Undo,
    ViewAdminTools,
    Execute,
    ViewHidden,
    ViewUsers,
    ViewStates,
    Delete,
    AddInstanceMessage,
    ViewAnswerMessages,
    AssignMessages,
    CreateInstance,
    CreateRelatedInstance
}

public class Action
{
    public const string All = "<All>";

    /// <summary>
    /// Internal name of the action
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Localized label of the action as shown in the user interface
    /// </summary>
    public BilingualString? Label { get; set; }

    /// <summary>
    /// List of roles that can perform this action
    /// </summary>
    public string[] Roles { get; set; } = [];

    /// <summary>
    /// Target form for View / Submit / Edit actions
    /// </summary>
    public string? Form { get; set; }

    /// <summary>
    /// List of target forms for View / Submit / Edit actions  
    /// </summary>
    public string[] Forms { get; set; } = [];

    public string[] AllForms => Form != null ? Forms.Append(Form).ToArray() : Forms;

    /// <summary>
    /// List of target collections for ??? (not yet implemented)
    /// </summary>
    public string[] Collections { get; set; } = [];

    /// <summary>
    /// Target propertyDefinition for the view hidden propertyDefinition action
    /// </summary>
    public string? Question { get; set; }

    /// <summary>
    /// Entity type this action applies to
    /// </summary>
    [YamlMember(Alias = "workflowDefinition")]
    public string? WorkflowDefinition { get; set; }

    /// <summary>
    /// Type of action
    /// </summary>
    public RoleAction Type { get; set; }

    /// <summary>
    /// Condition that determines whether the action is permitted
    /// </summary>
    public Condition? Condition { get; set; }

    /// <summary>
    /// List of triggers to run for an Execute action
    /// </summary>
    public Trigger[] Triggers { get; set; } = [];

    /// <summary>
    /// List of steps during which this action is permitted 
    /// </summary>
    public string[] Steps { get; set; } = [];

    /// <summary>
    /// Target property for the CreateRelatedInstance action
    /// </summary>
    public string? Property { get; set; }

    /// <summary>
    /// Weird thing, maybe we should get rid of this?
    /// </summary>
    public string? UserProperty { get; set; }

    /// <summary>
    /// Weird thing, maybe we should get rid of this?
    /// </summary>
    public int? Limit { get; set; }

    public bool MatchesForm(string form)
        => Forms.Contains(form) || Form == form || Form == All;

    public bool MatchesCollection(string property)
        => Collections.Contains(property);

    public Action Clone() => (Action)MemberwiseClone();
}