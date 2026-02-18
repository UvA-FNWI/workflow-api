using UvA.Workflow.Events;
using UvA.Workflow.Notifications;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.WorkflowInstances;

public class EffectService(
    InstanceService instanceService,
    IInstanceEventService eventService,
    ModelService modelService,
    IMailService mailService,
    IMailLogRepository mailLogRepository,
    IOptions<GraphMailOptions> graphMailOptions)
{
    private readonly GraphMailOptions _graphMailOptions = graphMailOptions.Value;

    public async Task RunEffects(WorkflowInstance instance, Effect[] effects, User user, CancellationToken ct,
        MailMessage? mail = null)
    {
        var context = modelService.CreateContext(instance);
        foreach (var effect in effects.Where(t => t.Condition.IsMet(context)))
        {
            if (effect.Event != null) await AddEvent(instance, effect.Event, user, ct);
            if (effect.UndoEvent != null) await UndoEvent(instance, effect.UndoEvent, user, ct);
            if (effect.SendMail != null) await SendMail(instance, effect.SendMail, user, ct, mail);
            if (effect.SetProperty != null) await SetProperty(instance, effect.SetProperty, ct);
        }
    }

    private async Task SendMail(WorkflowInstance instance, SendMessage sendMail, User user, CancellationToken ct,
        MailMessage? mail = null)
    {
        if (mail == null && !sendMail.SendAutomatically)
            throw new Exception("Mail message not provided");

        mail ??= (await Mail.FromModel(instance, sendMail, modelService))!.ToMailMessage();
        await mailService.Send(mail);

        await mailLogRepository.Log(new MailLogEntry
        {
            WorkflowInstanceId = instance.Id,
            WorkflowDefinition = instance.WorkflowDefinition,
            ExecutedBy = user.Id,
            OverrideRecipient = _graphMailOptions.OverrideRecipient,
            Subject = mail.Subject,
            Body = mail.Body,
            AttachmentTemplate = mail.AttachmentTemplate,
            To = MailDeliveryResolver.GetEffectiveRecipients(mail.To, _graphMailOptions.OverrideRecipient)
                .Select(r => new MailLogRecipient(r.MailAddress, r.DisplayName))
                .ToArray(),
            Cc = MailDeliveryResolver.GetEffectiveRecipients(mail.Cc, _graphMailOptions.OverrideRecipient)
                .Select(r => new MailLogRecipient(r.MailAddress, r.DisplayName))
                .ToArray(),
            Bcc = MailDeliveryResolver.GetEffectiveRecipients(mail.Bcc, _graphMailOptions.OverrideRecipient)
                .Select(r => new MailLogRecipient(r.MailAddress, r.DisplayName))
                .ToArray(),
            Attachments = mail.Attachments?
                .Select(a => new MailLogAttachment(a.FileName, a.Content))
                .ToArray() ?? []
        }, ct);
    }

    private async Task UndoEvent(WorkflowInstance instance, string eventName, User user, CancellationToken ct)
    {
        if (!instance.Events.TryGetValue(eventName, out var ev))
            return;
        ev.Date = null;
        await eventService.UpdateEvent(instance, ev.Id, user, ct);
    }

    public async Task AddEvent(WorkflowInstance instance, string eventName, User user, CancellationToken ct)
    {
        var ev = instance.Events.GetValueOrDefault(eventName);
        ev ??= instance.Events[eventName] = new InstanceEvent { Id = eventName };
        ev.Date = DateTime.Now;
        await eventService.UpdateEvent(instance, ev.Id, user, ct);
    }

    private async Task SetProperty(WorkflowInstance instance, SetProperty setProperty, CancellationToken ct)
    {
        instance.Properties[setProperty.Property] =
            setProperty.ValueExpression.Execute(modelService.CreateContext(instance)).ToBsonDocument();
        await instanceService.SaveValue(instance, null, setProperty.Property, ct);
    }
}