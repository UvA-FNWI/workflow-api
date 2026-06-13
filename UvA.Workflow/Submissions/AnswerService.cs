using System.Reflection.Metadata.Ecma335;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Serilog;
using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Journaling;
using UvA.Workflow.Persistence;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Submissions;

public record QuestionContext(
    WorkflowInstance Instance,
    FormSubmissionState SubmissionState,
    Form Form,
    PropertyDefinition PropertyDefinition);

public class AnswerService(
    SubmissionService submissionService,
    ModelService modelService,
    InstanceService instanceService,
    RightsService rightsService,
    IArtifactService artifactService,
    AnswerConversionService answerConversionService,
    IInstanceEventService instanceEventService,
    IInstanceJournalService instanceJournalService)
{
    public async Task<QuestionContext> GetQuestionContext(
        string instanceId, string submissionId, string questionName, CancellationToken ct)
    {
        var (instance, submissionState, form, _) =
            await submissionService.GetSubmissionContext(instanceId, submissionId, null, ct);

        // Get the propertyDefinition
        var question = modelService.GetQuestion(instance, form.PropertyName, questionName);
        if (question == null)
            throw new EntityNotFoundException("PropertyDefinition", questionName);

        return new QuestionContext(instance, submissionState, form, question);
    }

    public async Task<Answer[]> SaveAnswer(QuestionContext context, JsonElement? value, User user, CancellationToken ct)
    {
        var (instance, _, form, question) = context;

        // Get current answer
        var currentAnswer = instance.GetProperty(form.PropertyName, question.Name);

        // Convert new answer to BsonValue
        var newAnswer = await answerConversionService.ConvertToValue(value, question, ct);

        // Save if value changed
        if (newAnswer != currentAnswer)
        {
            instance.SetProperty(newAnswer, form.PropertyName, question.Name);
            await instanceService.SaveValue(instance, form.PropertyName, question!.Name, ct);

            // Only remove the cleared file from storage if it wasn't part of a submitted version.
            if (currentAnswer != null && newAnswer.IsBsonNull && question.DataType == DataType.File)
                await DeleteArtifactIfNotVersioned(instance, form, ArtifactInfo.FromBson(currentAnswer), ct);

            // if the form was ever submitted, then log the change
            if (await WasFormEverSubmitted(instance.Id, form, ct))
            {
                await instanceJournalService.LogPropertyChange(instance.Id,
                    PropertyChangeEntry.Create(context.PropertyDefinition, currentAnswer, user), ct);
            }
        }

        // Get questions to update (including dependent questions)
        var questionsToUpdate = question.DependentQuestions.Append(question).Distinct().ToArray();

        // Check if user can view hidden fields
        var canViewHidden = await rightsService.Can(instance, RoleAction.ViewHidden, form.Name);
        var updates = modelService.GetQuestionStatus(instance, form, canViewHidden, questionsToUpdate);

        // Build response
        return Answer.Create(instance, form, updates);
    }

    private async Task<bool> WasFormEverSubmitted(string instanceId, Form form, CancellationToken ct)
    {
        foreach (var eventId in FormSubmissionState.GetSubmissionEventIds(form))
            if (await instanceEventService.WasEventEverTriggered(instanceId, eventId, ct))
                return true;

        return false;
    }

    /// <summary>
    /// When the form was last submitted, taken from its submission event(s). We read the event date
    /// ourselves rather than using FormSubmissionState.DateSubmitted because a rejection suppresses the
    /// submission event instead of removing it, and we still want to keep files from a rejected submission.
    /// Null if the form was never submitted.
    /// </summary>
    private static DateTime? GetLastSubmissionDate(WorkflowInstance instance, Form form)
        => FormSubmissionState.GetSubmissionEventIds(form).Select(instance.GetEventDate).Max();

    /// <summary>
    /// Removes the file from storage unless it was part of a submitted version, meaning it was already there
    /// when the form was last submitted. Anything added after that, or before the form was ever submitted, is
    /// just a draft and gets deleted. The caller still updates the property that points to the file.
    /// </summary>
    private async Task DeleteArtifactIfNotVersioned(WorkflowInstance instance, Form form, ArtifactInfo? info,
        CancellationToken ct)
    {
        if (info == null)
            return;

        var lastSubmissionDate = GetLastSubmissionDate(instance, form);
        if (lastSubmissionDate == null || info.CreatedOn > lastSubmissionDate)
            await artifactService.TryDeleteArtifact(info.ArtifactId, ct);
    }

    public async Task<Artifact?> GetArtifact(QuestionContext context, string artifactId, CancellationToken ct)
    {
        var (instance, _, form, question) = context;

        var value = instance.GetProperty(form.PropertyName, question.Name);
        if (value == null) return null;
        if (question.IsArray)
        {
            var array = value as BsonArray ?? [];
            if (array.All(a => ArtifactInfo.FromBson(a)?.ArtifactId != artifactId)) return null;
        }
        else
        {
            var info = ArtifactInfo.FromBson(value);
            if (info?.ArtifactId != artifactId) return null;
        }

        return await artifactService.GetArtifact(artifactId, ct);
    }

    public async Task SaveArtifact(QuestionContext context, string artifactName, Stream contents,
        CancellationToken ct = default)
    {
        var (instance, _, _, propertyDefinition) = context;
        var artifactId = S3ArtifactService.ToArtifactId(instance.Id, propertyDefinition.Name);
        var artifactInfo = await artifactService.SaveArtifact(artifactId, artifactName, contents);
        await SaveArtifact(context, artifactInfo, ct);
    }

    public async Task SaveArtifact(QuestionContext context, IFormFile formFile, CancellationToken ct = default)
    {
        var (instance, _, _, propertyDefinition) = context;
        var artifactId = S3ArtifactService.ToArtifactId(instance.Id, propertyDefinition.Name);
        var artifactInfo = await artifactService.SaveArtifact(artifactId, formFile);

        await SaveArtifact(context, artifactInfo, ct);
    }

    private async Task SaveArtifact(QuestionContext context, ArtifactInfo artifactInfo, CancellationToken ct = default)
    {
        var (instance, _, form, question) = context;
        ArtifactInfo? oldArtifact = null;

        var value = instance.GetProperty(form.PropertyName, question.Name);

        if (question.IsArray)
        {
            var array = value as BsonArray ?? [];
            array.Add(artifactInfo.ToBsonDocument());
            instance.SetProperty(array, form.PropertyName, question.Name);
        }
        else
        {
            instance.SetProperty(artifactInfo.ToBsonDocument(), form.PropertyName, question.Name);
            oldArtifact = ArtifactInfo.FromBson(value);
        }

        await instanceService.SaveValue(instance, form.PropertyName, question.Name, ct);

        // The file we just replaced only gets removed if it wasn't part of a submitted version.
        await DeleteArtifactIfNotVersioned(instance, form, oldArtifact, ct);
    }

    public async Task DeleteArtifact(QuestionContext context, string artifactId, CancellationToken ct)
    {
        var (instance, _, form, question) = context;

        var value = instance.GetProperty(form.PropertyName, question.Name);
        if (value == null)
            throw new EntityNotFoundException("Artifact", "Artifact not found");

        // We always remove the reference below. The file itself stays in storage if it was part of a
        // submitted version.
        if (question.IsArray)
        {
            var array = value as BsonArray ?? [];
            var artifactRef = array.FirstOrDefault(a => ArtifactInfo.FromBson(a)?.ArtifactId == artifactId);
            if (artifactRef == null)
            {
                Log.Error("Artifact {ArtifactId} not found in array", artifactId);
                throw new EntityNotFoundException("Artifact", "Artifact not found");
            }

            await DeleteArtifactIfNotVersioned(instance, form, ArtifactInfo.FromBson(artifactRef), ct);
            array.Remove(artifactRef);
            instance.SetProperty(array, form.PropertyName, question.Name);
            await instanceService.SaveValue(instance, form.PropertyName, question.Name, ct);
        }
        else
        {
            var info = ArtifactInfo.FromBson(value);
            if (info?.ArtifactId != artifactId)
            {
                Log.Error("Artifact {ArtifactId} not found in object or data store", artifactId);
                throw new EntityNotFoundException("Artifact", "Artifact not found");
            }

            await instanceService.UnsetValue(instance, form.PropertyName, question.Name, ct);
            instance.ClearProperty(question.Name);
            await DeleteArtifactIfNotVersioned(instance, form, info, ct);
        }
    }
}