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
    IInstanceJournalService instanceJournalService,
    IUserService userService,
    IExternalUserService externalUserService)
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

    private async Task SaveAndLogAnswer(QuestionContext context, BsonValue? currentAnswer, BsonValue newAnswer,
        CancellationToken ct)
    {
        var (instance, _, form, question) = context;
        if (newAnswer != currentAnswer)
        {
            var user = await userService.GetCurrentUser(ct);
            if (user == null)
                throw new Exception("User not logged in, should not get here");

            instance.SetProperty(newAnswer, form.PropertyName, question.Name);
            await instanceService.SaveValue(instance, form.PropertyName, question.Name, ct);

            // if the form was ever submitted, then log the change
            var wasSubmitted = await WasFormEverSubmitted(instance.Id, form, ct);
            var isReplaced = !wasSubmitted; // if the form was not submitted, the old value is not stored
            if (wasSubmitted)
            {
                // if the journal entry was replaced, the old value isn't stored either
                isReplaced = await instanceJournalService.LogPropertyChange(instance.Id,
                    PropertyChangeEntry.Create(context.PropertyDefinition, currentAnswer, user), ct);
            }

            // delete any replaced file
            if (isReplaced && question.DataType == DataType.File)
            {
                var oldArtifact = currentAnswer is BsonDocument ? ArtifactInfo.FromBson(currentAnswer) : null;
                if (currentAnswer is BsonArray array)
                {
                    var newArray = (newAnswer as BsonArray)?.Select(ArtifactInfo.FromBson) ?? [];
                    oldArtifact = array
                        .Select(ArtifactInfo.FromBson)
                        .FirstOrDefault(a => newArray.All(b => b?.ArtifactId != a?.ArtifactId));
                }

                if (oldArtifact != null)
                    await artifactService.TryDeleteArtifact(oldArtifact.ArtifactId, ct);
            }
        }
    }

    public async Task<Answer[]> SaveAnswer(QuestionContext context, JsonElement? value, CancellationToken ct)
    {
        var (instance, _, form, question) = context;

        // Get current answer
        var currentAnswer = instance.GetProperty(form.PropertyName, question.Name);

        // Convert new answer to BsonValue
        var newAnswer = await answerConversionService.ConvertToValue(value, question, ct);

        // Save if value changed
        await SaveAndLogAnswer(context, currentAnswer, newAnswer, ct);

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
    /// TODO: do we want to replace the existing logic based on WasFormEverSubmitted by something based on this?
    /// (See DN-3796)
    /// 
    /// When the form was last submitted, taken from its submission event(s). We read the event date
    /// ourselves rather than using FormSubmissionState.DateSubmitted because a rejection suppresses the
    /// submission event instead of removing it, and we still want to keep files from a rejected submission.
    /// Null if the form was never submitted.
    /// </summary>
    private static DateTime? GetLastSubmissionDate(WorkflowInstance instance, Form form)
        => FormSubmissionState.GetSubmissionEventIds(form).Select(instance.GetEventDate).Max();

    public async Task<Artifact?> GetArtifact(QuestionContext context, string artifactId, CancellationToken ct)
    {
        var (instance, _, form, question) = context;

        var value = instance.GetProperty(form.PropertyName, question.Name);
        if (value == null || value is BsonNull || ArtifactInfo.FromBson(value)?.ArtifactId != artifactId)
        {
            var journal = await instanceJournalService.GetInstanceJournal(context.Instance.Id, false, ct);
            value = journal?.PropertyChanges.FirstOrDefault(p =>
                p.Path == question.Name && ArtifactInfo.FromBson(p.OldValue)?.ArtifactId == artifactId)?.OldValue;
        }

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

        var currentAnswer = instance.GetProperty(form.PropertyName, question.Name);
        BsonValue newAnswer;

        if (question.IsArray)
        {
            var array = currentAnswer as BsonArray ?? [];
            array.Add(artifactInfo.ToBsonDocument());
            newAnswer = array;
        }
        else
            newAnswer = artifactInfo.ToBsonDocument();

        await SaveAndLogAnswer(context, currentAnswer, newAnswer, ct);
    }

    public async Task DeleteArtifact(QuestionContext context, string artifactId, CancellationToken ct)
    {
        var (instance, _, form, question) = context;

        var currentAnswer = instance.GetProperty(form.PropertyName, question.Name);
        if (currentAnswer == null)
            throw new EntityNotFoundException("Artifact", "Artifact not found");
        BsonValue newAnswer;

        // We always remove the reference below. The file itself stays in storage if it was part of a
        // submitted version.
        if (question.IsArray)
        {
            var array = currentAnswer as BsonArray ?? [];
            var artifactRef = array.FirstOrDefault(a => ArtifactInfo.FromBson(a)?.ArtifactId == artifactId);
            if (artifactRef == null)
            {
                Log.Error("Artifact {ArtifactId} not found in array", artifactId);
                throw new EntityNotFoundException("Artifact", "Artifact not found");
            }

            array.Remove(artifactRef);
            newAnswer = array;
        }
        else
        {
            var info = ArtifactInfo.FromBson(currentAnswer);
            if (info?.ArtifactId != artifactId)
            {
                Log.Error("Artifact {ArtifactId} not found in object or data store", artifactId);
                throw new EntityNotFoundException("Artifact", "Artifact not found");
            }

            newAnswer = BsonNull.Value;
        }

        await SaveAndLogAnswer(context, currentAnswer, newAnswer, ct);
    }

    public async Task<(JsonElement? Value, UserSearchResult? CreatedUser)> ValidateAndResolveValue(
        PropertyDefinition propertyDefinition,
        JsonElement? value,
        ExternalUserInput? externalUser,
        CancellationToken ct)
    {
        UserSearchResult? createdUser = null;

        if (externalUser != null)
        {
            if (propertyDefinition.DataType != DataType.User)
                throw new ExternalUserCreationException(
                    ExternalUserCreationFailureReason.InvalidQuestionType, "InvalidQuestionType");

            if (propertyDefinition.AllowsExternalUsers != true)
                throw new ExternalUserCreationException(ExternalUserCreationFailureReason.ExternalUsersNotAllowed,
                    "ExternalUsersNotAllowed");

            createdUser = await externalUserService.CreateOrUpdateExternalUser(
                externalUser.DisplayName, externalUser.Email, externalUser.Organization, ct);
            value = JsonSerializer.SerializeToElement(createdUser, AnswerConversionService.Options);
        }

        if (propertyDefinition.DataType == DataType.User &&
            propertyDefinition.AllowsExternalUsers != true &&
            value is JsonElement userValue &&
            await answerConversionService.ContainsExternalUserSelection(userValue, propertyDefinition.IsArray, ct))
            throw new ExternalUserCreationException(ExternalUserCreationFailureReason.ExternalUsersNotAllowed,
                "ExternalUsersNotAllowed");

        if (propertyDefinition.DataType == DataType.Choice && value is JsonElement choiceValue &&
            AnswerConversionService.FindInvalidChoice(choiceValue, propertyDefinition) is { } invalidChoice)
            throw new InvalidWorkflowStateException(propertyDefinition.Name, "InvalidChoiceValue",
                $"'{invalidChoice}' is not a valid value");

        return (value, createdUser);
    }
}