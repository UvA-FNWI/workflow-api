using UvA.Workflow.Expressions;

namespace UvA.Workflow.Entities.Domain;

public class EndStep;

public class Trigger
{
    /// <summary>
    /// Condition that determines if this trigger is active
    /// </summary>
    public Condition? Condition { get; set; }

    /// <summary>
    /// Send an email
    /// </summary>
    public SendMessage? SendMail { get; set; }

    /// <summary>
    /// Do an http call to an external service
    /// </summary>
    public Http? Http { get; set; }

    /// <summary>
    /// Set a property on the current instance
    /// </summary>
    public SetProperty? SetProperty { get; set; }

    /// <summary>
    /// Complete an event
    /// </summary>
    public string? Event { get; set; }

    /// <summary>
    /// Undo an event
    /// </summary>
    public string? UndoEvent { get; set; }

    public IEnumerable<Lookup?> Properties =>
    [
        ..Condition?.Properties ?? [],
        ..SendMail?.SubjectTemplate?.Properties ?? [],
        ..SendMail?.BodyTemplate?.Properties ?? [],
        ..SendMail?.ToAddressTemplate?.Properties ?? [],
        ..Http?.UrlTemplate.Properties ?? [],
        ..SetProperty?.ValueExpression.Properties ?? [],
        SendMail?.To
    ];
}

public class SetProperty
{
    /// <summary>
    /// Target property name
    /// </summary>
    public string Property { get; set; } = null!;

    /// <summary>
    /// Expression that determines the value of the property
    /// </summary>
    public string Value { get; set; } = null!;

    public Expression ValueExpression => ExpressionParser.Parse(Value);
}

public class Http
{
    /// <summary>
    /// Template for the url to call
    /// </summary>
    public string Url { get; set; } = null!;

    private Template? _urlTemplate;
    public Template UrlTemplate => _urlTemplate ??= new Template(Url);
}

public class SendMessage
{
    public string? To { get; set; } = null!;
    public string? ToAddress { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }
    [YamlMember(Alias = "template")] public string? TemplateKey { get; set; }
    public bool SendAsMail { get; set; }
    public bool SendAutomatically { get; set; }
    public Attachment[] Attachments { get; set; } = [];

    private Template? _subjectTemplate, _bodyTemplate, _toAddressTemplate;
    public Template? SubjectTemplate => _subjectTemplate ??= Template.Create(Subject);
    public Template? BodyTemplate => _bodyTemplate ??= Template.Create(Body);
    public Template? ToAddressTemplate => _toAddressTemplate ??= Template.Create(ToAddress);
}

public class Attachment
{
    public string Template { get; set; } = null!;
}