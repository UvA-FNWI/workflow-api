using System.Text.Json.Serialization;
using Uva.Workflow.Entities.Domain.Conditions;
using YamlDotNet.Serialization;

namespace Uva.Workflow.Entities.Domain;

public class Role
{
    public string Name { get; set; } = null!;
    public string? OldName { get; set; }
    public string? ShortName { get; set; }
    public BilingualString? Title { get; set; }
    public string[] InheritFrom { get; set; } = [];
    public List<Action> Actions { get; set; } = [];
    public bool Assignable { get; set; }
    public NotificationType[] Notifications { get; set; } = [];

    [YamlIgnore] public BilingualString DisplayTitle => Title ?? Name;
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

    public string? Name { get; set; }
    
    public BilingualString? Label { get; set; }
    public string[] Roles { get; set; } = [];

    public string? Form { get; set; }
    public string[] Forms { get; set; } = [];
    public string[] AllForms => Form != null ? Forms.Append(Form).ToArray() : Forms;
    public string[] Collections { get; set; } = [];
    
    
    public string? Question { get; set; }
    [YamlMember(Alias = "entity")]
    [JsonPropertyName("entity")]
    public string? EntityType { get; set; }
    public RoleAction Type { get; set; }
    public Condition? Condition { get; set; }
    public Trigger[] Triggers { get; set; } = [];
    public string[] Steps { get; set; } = [];
    public string? Property { get; set; }
    
    public string? UserProperty { get; set; }
    public int? Limit { get; set; }

    public bool MatchesForm(string form)
        => Forms.Contains(form) || Form == form || Form == All;

    public bool MatchesCollection(string property)
        => Collections.Contains(property);
    
    public Action Clone() => (Action)MemberwiseClone();
}