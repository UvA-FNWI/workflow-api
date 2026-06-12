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

            var wasSubmitted = await WasFormEverSubmitted(instance.Id, form, ct);

            // Only delete the old file if the form was never submitted; otherwise it is
            // part of a stored version and must be retained.
            if (!wasSubmitted && currentAnswer != null && newAnswer.IsBsonNull
                && question.DataType == DataType.File)
            {
                var info = ArtifactInfo.FromBson(currentAnswer);
                if (info != null)
                    await artifactService.TryDeleteArtifact(info.ArtifactId, ct);
            }

            // if the form is submitted, then log the change
            if (wasSubmitted)
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
        string? oldArtifactId = null;

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
            oldArtifactId = ArtifactInfo.FromBson(value)?.ArtifactId;
        }

        await instanceService.SaveValue(instance, form.PropertyName, question.Name, ct);

        // Retain the replaced file if the form was ever submitted (it is part of a stored version).
        if (oldArtifactId != null && !await WasFormEverSubmitted(instance.Id, form, ct))
        {
            await artifactService.TryDeleteArtifact(oldArtifactId, ct);
        }
    }

    public async Task DeleteArtifact(QuestionContext context, string artifactId, CancellationToken ct)
    {
        var (instance, _, form, question) = context;

        var value = instance.GetProperty(form.PropertyName, question.Name);
        if (value == null)
            throw new EntityNotFoundException("Artifact", "Artifact not found");

        // Retain the file if the form was ever submitted (it is part of a stored version);
        // either way the reference is removed from the current instance properties below.
        var wasSubmitted = await WasFormEverSubmitted(instance.Id, form, ct);

        if (question.IsArray)
        {
            var array = value as BsonArray ?? [];
            var artifactRef = array.FirstOrDefault(a => ArtifactInfo.FromBson(a)?.ArtifactId == artifactId);
            if (artifactRef == null)
            {
                Log.Error("Artifact {ArtifactId} not found in array", artifactId);
                throw new EntityNotFoundException("Artifact", "Artifact not found");
            }

            if (!wasSubmitted)
                await artifactService.TryDeleteArtifact(artifactId, ct);
            array.Remove(artifactRef);
            instance.SetProperty(array, form.PropertyName, question.Name);
            await instanceService.SaveValue(instance, form.PropertyName, question.Name, ct);
        }
        else
        {
            var oid = ArtifactInfo.FromBson(value)?.ArtifactId;
            if (oid != artifactId)
            {
                Log.Error("Artifact {ArtifactId} not found in object or data store", artifactId);
                throw new EntityNotFoundException("Artifact", "Artifact not found");
            }

            await instanceService.UnsetValue(instance, form.PropertyName, question.Name, ct);
            instance.ClearProperty(question.Name);
            if (!wasSubmitted)
                await artifactService.TryDeleteArtifact(artifactId, ct);
        }
    }
}