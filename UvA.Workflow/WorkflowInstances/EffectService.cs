using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;
using UvA.Workflow.Events;
using UvA.Workflow.Expressions;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Jobs;
using UvA.Workflow.Notifications;
using UvA.Workflow.Persistence;
using UvA.Workflow.WorkflowModel;

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
    IConfiguration configuration,
    ILogger<EffectService> logger)
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    public async Task<EffectResult> RunEffect(Job job, WorkflowInstance instance, Effect effect, User user,
        ObjectContext context, CancellationToken ct)
    {
        var input = job.Input;
        if (effect.Event != null) await AddEvent(instance, effect.Event, user, ct);
        if (effect.UndoEvent != null) await UndoEvent(instance, effect.UndoEvent, user, ct);
        if (effect.SendMail != null) await SendMail(instance, effect.SendMail, user, ct, input?.Mail, job.Id);
        if (effect.SetProperty != null) await SetProperty(instance, context, effect.SetProperty, ct);
        if (effect.ServiceCall != null) await ServiceCall(context, effect, ct);
        var redirectUrl = effect.Redirect?.UrlTemplate.Execute(context);

        return new EffectResult(redirectUrl, effect.ShowConfetti);
    }

    private async Task SendMail(WorkflowInstance instance, SendMessage sendMail, User user, CancellationToken ct,
        MailMessage? mail = null, string? jobId = null)
    {
        if (mail == null && !sendMail.SendAutomatically)
            throw new Exception("Mail message not provided");

        mail ??= await mailBuilder.BuildAsync(instance, sendMail, modelService, ct);
        var dispatchResult = await mailService.Send(mail, ct);

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
            OverrideRecipient = dispatchResult.AppliedRecipientOverride,
            Subject = mail.Subject,
            Body = mail.Body,
            AttachmentTemplate = mail.AttachmentTemplate,
            To = dispatchResult.To
                .Select(r => new MailLogRecipient(r.MailAddress, r.DisplayName))
                .ToArray(),
            Cc = dispatchResult.Cc
                .Select(r => new MailLogRecipient(r.MailAddress, r.DisplayName))
                .ToArray(),
            Bcc = dispatchResult.Bcc
                .Select(r => new MailLogRecipient(r.MailAddress, r.DisplayName))
                .ToArray(),
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

        var isDisabled = service.Disabled != null
                         && bool.TryParse(Template.Create(service.Disabled).Apply(optionContext), out var d)
                         && d;
        if (isDisabled)
        {
            logger.LogWarning(
                "Service {Service} is disabled; skipping call to {Operation}.",
                serviceCall.Service, serviceCall.Operation);
            return;
        }

        var client = new HttpClient
        {
            BaseAddress = service.BaseUrl != null
                ? new Uri(Template.Create(service.BaseUrl).Apply(optionContext))
                : null
        };
        foreach (var header in service.Headers)
            client.DefaultRequestHeaders.Add(header.Key, Template.Create(header.Value).Apply(optionContext));

        var resolvedInputs = new Dictionary<Lookup, object?>();
        var missingInputs = new List<string>();
        foreach (var input in serviceCall.Inputs)
        {
            var value = context.Get(input.Value);
            resolvedInputs[input.Key] = value;
            if (value == null)
                missingInputs.Add($"{input.Key}<-{input.Value}");
        }

        if (missingInputs.Count > 0)
        {
            throw new InvalidOperationException(
                $"Service call {serviceCall.Service}.{serviceCall.Operation} has unresolved inputs: {string.Join(", ", missingInputs)}");
        }

        var requestContext = new ObjectContext(resolvedInputs);

        var request = new HttpRequestMessage(new HttpMethod(operation.Method),
            Template.Create(operation.Url).Apply(requestContext));
        request.Content = await BuildRequestContent(operation, requestContext, ct);

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
            context.Values.Add(effect.Name ?? operation.Name, operation.Outputs.ToDictionary(
                Lookup (o) => o.Name,
                object (o) => body?.RootElement.GetProperty(o.Path).GetString()!)
            );
        }
    }

    private async Task<HttpContent?> BuildRequestContent(ServiceOperation operation, ObjectContext requestContext,
        CancellationToken ct)
    {
        if (operation.Body != null)
            return JsonContent.Create(Process(operation.Body, requestContext));

        var fileInput = operation.Inputs.FirstOrDefault(i =>
            i.Type.TrimEnd('!').Equals("File", StringComparison.OrdinalIgnoreCase));
        if (fileInput == null)
            return null;

        if (requestContext.Get(fileInput.Name) is not ArtifactInfo fileInfo)
            return null;

        var artifact = await artifactService.GetArtifact(fileInfo.Id, ct);
        if (artifact == null)
            throw new EntityNotFoundException("Artifact", fileInfo.Id.ToString());

        var content = new ByteArrayContent(artifact.Content);
        var contentType = ContentTypeProvider.TryGetContentType(fileInfo.Name, out var mimeType)
            ? mimeType
            : "application/octet-stream";
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline")
        {
            FileName = Uri.EscapeDataString(fileInfo.Name)
        };
        return content;
    }

    private object Process(object source, ObjectContext context) => source switch
    {
        Dictionary<object, object> dict => dict.ToDictionary(o => o.Key.ToString()!, o => Process(o.Value, context)),
        List<object> list => list.Select(o => Process(o, context)).ToList(),
        string s => Template.Create(s).Apply(context),
        _ => source
    };
}