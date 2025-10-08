namespace UvA.Workflow.Services;

public class TriggerService(InstanceService instanceService, ModelService modelService, IMailService mailService)
{
    public async Task RunTriggers(WorkflowInstance instance, Trigger[] triggers, MailMessage? mail = null)
    {
        var context = modelService.CreateContext(instance);
        foreach (var trigger in triggers.Where(t => t.Condition.IsMet(context)))
        {
            if (trigger.Event != null) await Event(instance, trigger.Event);
            if (trigger.UndoEvent != null) await UndoEvent(instance, trigger.UndoEvent);
            if (trigger.SendMail != null) await SendMail(instance, trigger.SendMail, mail);
            if (trigger.SetProperty != null) await SetProperty(instance, trigger.SetProperty);
        }
    }

    private async Task SendMail(WorkflowInstance instance, SendMessage sendMail, MailMessage? mail = null)
    {
        if (mail == null && !sendMail.SendAutomatically)
            throw new Exception("Mail message not provided");

        mail ??= (await Mail.FromModel(instance, sendMail, modelService))!.ToMailMessage();
        // TODO: sendAsMail vs message
        await mailService.Send(mail);
    }

    private async Task UndoEvent(WorkflowInstance instance, string eventName)
    {
        if (!instance.Events.TryGetValue(eventName, out var ev))
            return;
        ev.Date = null;
        await instanceService.UpdateEvent(instance, ev.Id);
    }

    private async Task Event(WorkflowInstance instance, string eventName)
    {
        var ev = instance.Events.GetValueOrDefault(eventName);
        ev ??= instance.Events[eventName] = new InstanceEvent { Id = eventName };
        ev.Date = DateTime.Now;
        await instanceService.UpdateEvent(instance, ev.Id);
    }

    private async Task SetProperty(WorkflowInstance instance, SetProperty setProperty)
    {
        instance.Properties[setProperty.Property] =
            setProperty.ValueExpression.Execute(modelService.CreateContext(instance)).ToBsonDocument();
        await instanceService.SaveValue(instance, null, setProperty.Property);
    }
}