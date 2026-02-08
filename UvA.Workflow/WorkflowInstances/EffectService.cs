using System.Net.Http.Json;
using System.Text.Json;
using UvA.Workflow.Events;
using UvA.Workflow.Expressions;
using UvA.Workflow.Jobs;

namespace UvA.Workflow.WorkflowInstances;

public record EffectResult(string? RedirectUrl = null);

public class EffectService(
    InstanceService instanceService,
    IInstanceEventService eventService,
    ModelService modelService,
    IMailService mailService,
    IConfiguration configuration)
{
    public async Task<EffectResult> RunEffect(JobInput? input, WorkflowInstance instance, Effect effect, User user,
        ObjectContext context, CancellationToken ct)
    {
        string? redirectUrl = null;
        if (effect.Event != null) await AddEvent(instance, effect.Event, user, ct);
        if (effect.UndoEvent != null) await UndoEvent(instance, effect.UndoEvent, user, ct);
        if (effect.SendMail != null) await SendMail(instance, effect.SendMail, ct, input?.Mail);
        if (effect.SetProperty != null) await SetProperty(instance, context, effect.SetProperty, ct);
        if (effect.ServiceCall != null) await ServiceCall(context, effect, ct);
        redirectUrl = effect.Redirect?.UrlTemplate.Execute(context);

        return new EffectResult(redirectUrl);
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
        await instanceService.SaveValue(instance, null, setProperty.Property, ct);
    }

    private async Task ServiceCall(ObjectContext context, Effect effect, CancellationToken ct)
    {
        var serviceCall = effect.ServiceCall!;
        var service = modelService.Services.Get(serviceCall.Service);
        var operation = service.Operations.Get(serviceCall.Operation);

        var optionContext = new ObjectContext(
            configuration
                .GetSection("Services").GetSection(serviceCall.Service)
                .Get<Dictionary<string, string>>()?
                .ToDictionary(Lookup (l) => l.Key, object? (l) => l.Value) ?? new()
        );

        var client = new HttpClient
        {
            BaseAddress = service.BaseUrl != null
                ? new Uri(Template.Create(service.BaseUrl).Apply(optionContext))
                : null
        };
        foreach (var header in service.Headers)
            client.DefaultRequestHeaders.Add(header.Key, Template.Create(header.Value).Apply(optionContext));

        var requestContext =
            new ObjectContext(serviceCall.Inputs.ToDictionary(Lookup (i) => i.Key, i => context.Get(i.Value)));

        var request = new HttpRequestMessage(new HttpMethod(operation.Method),
            Template.Create(operation.Url).Apply(requestContext));
        if (operation.Body != null)
            request.Content = JsonContent.Create(Process(operation.Body, requestContext));

        var result = await client.SendAsync(request, ct);
        result.EnsureSuccessStatusCode();

        if (operation.Outputs.Any())
        {
            var body = await result.Content.ReadFromJsonAsync<JsonDocument>(ct);
            context.Values.Add(effect.Name ?? operation.Name, operation.Outputs.ToDictionary(
                Lookup (o) => o.Name,
                object (o) => body?.RootElement.GetProperty(o.Path).GetString()!)
            );
        }
    }

    private object Process(object source, ObjectContext context) => source switch
    {
        Dictionary<object, object> dict => dict.ToDictionary(o => o.Key.ToString()!, o => Process(o.Value, context)),
        List<object> list => list.Select(o => Process(o, context)).ToList(),
        string s => Template.Create(s).Apply(context),
        _ => source
    };
}