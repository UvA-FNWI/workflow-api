using System.Text.Json;
using Serilog;
using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Journaling;
using UvA.Workflow.Persistence;

namespace UvA.Workflow.Submissions;

public record QuestionContext(
    WorkflowInstance Instance,
    InstanceEvent? Submission,
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
        var (instance, submission, form, _) =
            await submissionService.GetSubmissionContext(instanceId, submissionId, null, ct);

        // Get the propertyDefinition
        var question = modelService.GetQuestion(instance, form.PropertyName, questionName);
        if (question == null)
            throw new EntityNotFoundException("PropertyDefinition", questionName);

        return new QuestionContext(instance, submission, form, question);
    }

    public async Task<Answer[]> SaveAnswer(QuestionContext context, JsonElement? value, User user, CancellationToken ct)
    {
        var (instance, submission, form, question) = context;

        // Get current answer
        var currentAnswer = instance.GetProperty(form!.PropertyName, question.Name);

        // Convert new answer to BsonValue
        var newAnswer = await answerConversionService.ConvertToValue(new AnswerInput(value), question!, ct);

        // Save if value changed
        if (newAnswer != currentAnswer)
        {
            instance.SetProperty(newAnswer, form.PropertyName, question.Name);
            await instanceService.SaveValue(instance, form.PropertyName, question!.Name, ct);

            // if the form is submitted, then log the change
            if (await instanceEventService.WasEventEverTriggered(instance.Id, form.Name, ct))
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
        return Answer.Create(instance, form.TargetForm ?? form, updates);
    }

    public async Task<Artifact?> GetArtifact(QuestionContext context, string artifactId, CancellationToken ct)
    {
        var artifactObjectId = new ObjectId(artifactId);

        var (instance, _, form, question) = context;

        var value = instance!.GetProperty(form!.PropertyName, question.Name);
        if (value == null) return null;
        if (question.IsArray)
        {
            var array = value as BsonArray ?? [];
            if (array.All(a => a["_id"].AsString != artifactId)) return null;
        }
        else
        {
            if (value["_id"].AsObjectId != artifactObjectId) return null;
        }

        return await artifactService.GetArtifact(artifactObjectId, ct);
    }

    public async Task SaveArtifact(QuestionContext context, string artifactName, Stream contents, CancellationToken ct)
    {
        var (instance, _, form, question) = context;

        var artifactInfo = await artifactService.SaveArtifact(artifactName, contents);
        ObjectId? oidOldArtifact = null;

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
            oidOldArtifact = value?["_id"].AsObjectId;
        }

        await instanceService.SaveValue(instance, form.PropertyName, question.Name, ct);

        if (oidOldArtifact != null)
        {
            await artifactService.TryDeleteArtifact(oidOldArtifact.Value, ct);
        }
    }

    public async Task DeleteArtifact(QuestionContext context, string artifactId, CancellationToken ct)
    {
        var artifactObjectId = new ObjectId(artifactId);
        var (instance, _, form, question) = context;

        var value = instance!.GetProperty(form.PropertyName, question.Name);
        if (value == null)
            throw new EntityNotFoundException("Artifact", "Artifact not found");

        if (question.IsArray)
        {
            var array = value as BsonArray ?? [];
            var artifactRef = array.FirstOrDefault(a => a["_id"].AsString == artifactId);
            if (artifactRef == null)
            {
                Log.Error("Artifact {ArtifactId} not found in array", artifactId);
                throw new EntityNotFoundException("Artifact", "Artifact not found");
            }

            await artifactService.TryDeleteArtifact(new ObjectId(artifactId), ct);
            array.Remove(artifactRef);
            instance.SetProperty(array, form.PropertyName, question.Name);
            await instanceService.SaveValue(instance, form.PropertyName, question.Name, ct);
        }
        else
        {
            var oid = value["_id"].AsObjectId;
            if (oid != artifactObjectId)
            {
                Log.Error("Artifact {ArtifactId} not found in object or data store", artifactId);
                throw new EntityNotFoundException("Artifact", "Artifact not found");
            }

            await instanceService.UnsetValue(instance, form.PropertyName, question.Name, ct);
            instance.ClearProperty(question.Name);
            await artifactService.TryDeleteArtifact(new ObjectId(artifactId), ct);
        }
    }
}