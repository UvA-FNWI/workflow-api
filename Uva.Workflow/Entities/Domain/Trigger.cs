using Uva.Workflow.Entities.Domain.Conditions;
using Uva.Workflow.Expressions;
using Uva.Workflow.Services;
using YamlDotNet.Serialization;

namespace Uva.Workflow.Entities.Domain;

public class EndStep;

public class Trigger
{
    public Condition? Condition { get; set; }
    public SendMessage? SendMail { get; set; }
    public Http? Http { get; set; }
    public SetProperty? SetProperty { get; set; }
    public string? Event { get; set; }
    public string? UndoEvent { get; set; }

    public IEnumerable<Lookup?> Properties => [
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
    public string? Target { get; set; }
    public string Property { get; set; } = null!;
    public string Value { get; set; } = null!;

    public Expression ValueExpression => ExpressionParser.Parse(Value);
}

public class Http
{
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
    [YamlMember(Alias = "template")]
    public string? TemplateKey { get; set; }
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