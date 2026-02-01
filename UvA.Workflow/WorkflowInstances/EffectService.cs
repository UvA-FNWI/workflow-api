using UvA.Workflow.Events;

namespace UvA.Workflow.WorkflowInstances;

public class EffectService(
    InstanceService instanceService,
    IWorkflowInstanceRepository instanceRepository,
    IInstanceEventService eventService,
    ModelService modelService,
    IMailService mailService)
{
    public async Task RunEffects(WorkflowInstance instance, Effect[] effects, User user, CancellationToken ct,
        MailMessage? mail = null)
    {
        var context = modelService.CreateContext(instance);
        await instanceService.Enrich(
            modelService.WorkflowDefinitions[instance.WorkflowDefinition],
            [context],
            effects.SelectMany(e => e.Properties),
            ct
        );
        foreach (var effect in effects.Where(t => t.Condition.IsMet(context)))
        {
            if (effect.Event != null) await AddEvent(instance, effect.Event, user, ct);
            if (effect.UndoEvent != null) await UndoEvent(instance, effect.UndoEvent, user, ct);
            if (effect.SendMail != null) await SendMail(instance, effect.SendMail, ct, mail);
            if (effect.SetProperty != null) await SetProperty(instance, context, effect.SetProperty, ct);
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

    public async Task AddEvent(WorkflowInstance instance, string eventName, User user, CancellationToken ct)
    {
        var ev = instance.Events.GetValueOrDefault(eventName);
        ev ??= instance.Events[eventName] = new InstanceEvent { Id = eventName };
        ev.Date = DateTime.Now;
        await eventService.UpdateEvent(instance, ev.Id, user, ct);
    }

    private async Task SetProperty(WorkflowInstance instance, ObjectContext context, SetProperty setProperty,
        CancellationToken ct)
    {
        instance.Properties[setProperty.Property] = BsonValue.Create(setProperty.ValueExpression.Execute(context));
        await instanceRepository.SaveValue(instance, null, setProperty.Property, ct);
    }
}