using UvA.Workflow.Events;

namespace UvA.Workflow.WorkflowInstances;

public class TriggerService(
    InstanceService instanceService,
    IInstanceEventService eventService,
    ModelService modelService,
    IMailService mailService)
{
    public async Task RunTriggers(WorkflowInstance instance, Trigger[] triggers, User user, CancellationToken ct,
        MailMessage? mail = null)
    {
        var context = modelService.CreateContext(instance);
        foreach (var trigger in triggers.Where(t => t.Condition.IsMet(context)))
        {
            if (trigger.Event != null) await AddEvent(instance, trigger.Event, user, ct);
            if (trigger.UndoEvent != null) await UndoEvent(instance, trigger.UndoEvent, user, ct);
            if (trigger.SendMail != null) await SendMail(instance, trigger.SendMail, ct, mail);
            if (trigger.SetProperty != null) await SetProperty(instance, trigger.SetProperty, ct);
        }
    }

    private async Task SendMail(WorkflowInstance instance, SendMessage sendMail, CancellationToken ct,
        MailMessage? mail = null)
    {
        if (mail == null && !sendMail.SendAutomatically)
            throw new Exception("Mail message not provided");

        mail ??= (await Mail.FromModel(instance, sendMail, modelService))!.ToMailMessage();
        // TODO: sendAsMail vs message
        await mailService.Send(mail);
    }

    private async Task UndoEvent(WorkflowInstance instance, string eventName, User user, CancellationToken ct)
    {
        if (!instance.Events.TryGetValue(eventName, out var ev))
            return;
        ev.Date = null;
        await eventService.UpdateEvent(instance, ev.Id, user, ct);
    }

    private async Task AddEvent(WorkflowInstance instance, string eventName, User user, CancellationToken ct)
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