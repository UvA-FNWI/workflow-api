using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UvA.Workflow.Events;
using UvA.Workflow.Expressions;
using UvA.Workflow.Jobs;
using UvA.Workflow.Notifications;
using UvA.Workflow.Persistence;

namespace UvA.Workflow.WorkflowInstances;

public record EffectResult(string? RedirectUrl = null, bool? ShowConfetti = null)
{
    public static EffectResult operator +(EffectResult result, EffectResult other)
        => new(result.RedirectUrl ?? other.RedirectUrl, result.ShowConfetti ?? other.ShowConfetti);
}

public class EffectService(
    InstanceService instanceService,
    IInstanceEventService eventService,
    ModelService modelService,
    IMailService mailService,
    MailBuilder mailBuilder,
    IArtifactService artifactService,
    IMailLogRepository mailLogRepository,
    IOptions<GraphMailOptions> graphMailOptions,
    IConfiguration configuration,
    ILogger<EffectService> logger)
{
    private readonly GraphMailOptions _graphMailOptions = graphMailOptions.Value;

    public async Task<EffectResult> RunEffect(Job job, WorkflowInstance instance, Effect effect, User user,
        ObjectContext context, CancellationToken ct)
    {
        var input = job.Input;
        if (effect.Event != null) await AddEvent(instance, effect.Event, user, ct);
        if (effect.UndoEvent != null) await UndoEvent(instance, effect.UndoEvent, user, ct);
        if (effect.SendMail != null) await SendMail(instance, effect.SendMail, user, ct, input?.Mail, job.Id);
        if (effect.SetProperty != null) await SetProperty(instance, context, effect.SetProperty, ct);
        if (effect.ServiceCall != null) await ServiceCall(instance, context, effect, ct);
        var redirectUrl = effect.Redirect?.UrlTemplate.Execute(context);

        return new EffectResult(redirectUrl, effect.ShowConfetti);
    }

    private async Task SendMail(WorkflowInstance instance, SendMessage sendMail, User user, CancellationToken ct,
        MailMessage? mail = null, string? jobId = null)
    {
        if (mail == null && !sendMail.SendAutomatically)
            throw new Exception("Mail message not provided");

        mail ??= await mailBuilder.BuildAsync(instance, sendMail, modelService, ct);
        await mailService.Send(mail);

        var attachments = new List<ArtifactInfo>();
        if (mail.Attachments is { Count: > 0 } mailAttachments)
        {
            foreach (var a in mailAttachments)
            {
                var artifact = await artifactService.SaveArtifact(a.FileName, a.Content);
                attachments.Add(artifact);
            }
        }

        await mailLogRepository.Log(new MailLogEntry
        {
            WorkflowInstanceId = instance.Id,
            WorkflowDefinition = instance.WorkflowDefinition,
            ExecutedBy = user.Id,
            JobId = jobId,
            OverrideRecipient = _graphMailOptions.OverrideRecipient,
            Subject = mail.Subject,
            Body = mail.Body,
            AttachmentTemplate = mail.AttachmentTemplate,
            To = mail.To
                .Select(r => new MailLogRecipient(r.MailAddress, r.DisplayName))
                .ToArray(),
            Cc = mail.Cc?
                .Select(r => new MailLogRecipient(r.MailAddress, r.DisplayName))
                .ToArray() ?? [],
            Bcc = mail.Bcc?
                .Select(r => new MailLogRecipient(r.MailAddress, r.DisplayName))
                .ToArray() ?? [],
            Attachments = attachments.ToArray()
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

    private async Task SetProperty(WorkflowInstance instance, ObjectContext context, SetProperty setProperty,
        CancellationToken ct)
    {
        instance.Properties[setProperty.Property] = BsonValue.Create(setProperty.ValueExpression.Execute(context));
        await instanceService.SaveValue(instance, null, setProperty.Property, ct);
    }

    private async Task ServiceCall(WorkflowInstance instance, ObjectContext context, Effect effect,
        CancellationToken ct)
    {
        var serviceCall = effect.ServiceCall!;
        var service = modelService.Services.Get(serviceCall.Service);
        var operation = service.Operations.Get(serviceCall.Operation);

        var workflowDefinition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var inputLookups = serviceCall.Inputs.Values.SelectMany(v => ExpressionParser.Parse(v).Properties);
        await instanceService.Enrich(workflowDefinition, [context], inputLookups, ct, replaceStep: false);

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
            new ObjectContext(serviceCall.Inputs.ToDictionary(Lookup (i) => i.Key,
                i => ExpressionParser.Parse(i.Value).Execute(context)));

        var request = new HttpRequestMessage(new HttpMethod(operation.Method),
            Template.Create(operation.Url).Apply(requestContext));
        if (operation.Body != null)
        {
            var b = Process(operation.Body, requestContext);
            request.Content = JsonContent.Create(b);
        }

        var result = await client.SendAsync(request, ct);
        try
        {
            result.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException e)
        {
            string? responseBody = null;
            try
            {
                responseBody = result.Content == null ? null : await result.Content.ReadAsStringAsync(ct);
            }
            catch (Exception readError)
            {
                logger.LogWarning(readError,
                    "Failed to read error response body for service call {Service}.{Operation}",
                    serviceCall.Service, serviceCall.Operation);
            }

            logger.LogError(e,
                "Service call {Service}.{Operation} failed. Method: {Method}, Url: {Url}, StatusCode: {StatusCode}, ReasonPhrase: {ReasonPhrase}, ResponseBody: {ResponseBody}",
                serviceCall.Service,
                serviceCall.Operation,
                request.Method.Method,
                request.RequestUri?.ToString(),
                (int?)result.StatusCode,
                result.ReasonPhrase,
                responseBody);
            throw;
        }

        if (operation.Outputs.Any())
        {
            var body = await result.Content.ReadFromJsonAsync<JsonDocument>(ct);

            ObjectContext? responseContext = null;
            if (operation.Outputs.Any(o => o.Template != null))
            {
                // Build a merged context for output template evaluation.
                // Priority (highest wins on key collision):
                //   JSON response root > service config (optionContext) > main workflow context
                // JSON response root has the highest priority as it is the direct subject of the output.
                // Service config values (base URLs, tokens, etc.) come next.
                // Workflow instance properties are available as a lower-priority fallback.

                responseContext = new ObjectContext(
                    context.Values
                        .Concat(optionContext.Values)
                        .Concat(body!.RootElement.EnumerateObject()
                            .ToDictionary(Lookup (p) => p.Name, object? (p) => p.Value.ToString()))
                        .GroupBy(p => p.Key)
                        .ToDictionary(g => g.Key, g => g.Last().Value)
                );
            }

            context.Values.Add(effect.Name ?? operation.Name, operation.Outputs.ToDictionary(
                Lookup (o) => o.Name,
                object (o) => o.Template != null
                    ? Template.Create(o.Template).Apply(responseContext!)
                    : body?.RootElement.GetProperty(o.Path!).ToString()!)
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