using System.Text.Json;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Users;

public enum ExternalUserEmailAnswerUpdateResult
{
    Updated,
    UserNotInAnswer,
    Forbidden
}

public record ExternalUserEmailAnswerUpdatePlan(
    ExternalUserEmailAnswerUpdateResult Result,
    IReadOnlyCollection<QuestionContext> EditableContexts,
    IReadOnlyCollection<PropertyDefinition> EditableProperties);

public class ExternalUserEmailUpdateService(
    RightsService rightsService,
    AnswerService answerService,
    ModelService modelService,
    InstanceService instanceService)
{
    public static bool CanUpdateExternalUserEmail(User user)
        => UserProviderKeys.IsExternal(user.ProviderKey) &&
           user.InvitationState == UserInvitationState.Required;

    public async Task<ExternalUserEmailAnswerUpdatePlan> PrepareAnswerReferenceUpdate(
        WorkflowInstance instance,
        User user,
        CancellationToken ct)
    {
        var contexts = GetMatchingUserQuestionContexts(instance, user.Id).ToArray();

        if (contexts.Length == 0)
        {
            var instanceProperties = GetMatchingInstanceOnlyProperties(instance, user.Id).ToArray();

            if (instanceProperties.Length == 0)
                return new ExternalUserEmailAnswerUpdatePlan(
                    ExternalUserEmailAnswerUpdateResult.UserNotInAnswer,
                    [], []);

            return new ExternalUserEmailAnswerUpdatePlan(
                ExternalUserEmailAnswerUpdateResult.Updated,
                [], instanceProperties);
        }


        var editableContexts = new List<QuestionContext>();
        foreach (var context in contexts)
        {
            if (await CanEdit(context))
                editableContexts.Add(context);
        }

        if (editableContexts.Count == 0)
            return new ExternalUserEmailAnswerUpdatePlan(
                ExternalUserEmailAnswerUpdateResult.Forbidden,
                [], []);

        return new ExternalUserEmailAnswerUpdatePlan(
            ExternalUserEmailAnswerUpdateResult.Updated,
            editableContexts, []);
    }

    public async Task UpdateAnswerReferences(
        ExternalUserEmailAnswerUpdatePlan plan,
        User user,
        WorkflowInstance instance,
        CancellationToken ct)
    {
        foreach (var context in plan.EditableContexts)
        {
            if (!TryCreateUpdatedUserAnswerValue(context, user, out var updatedAnswerValue))
                continue;

            await answerService.SaveAnswer(context, updatedAnswerValue, ct);
        }

        foreach (var property in plan.EditableProperties)
        {
            UpdateInstanceUserProperty(instance, property, user);
            await instanceService.SaveValue(instance, null, property.Name, ct);
        }
    }

    private IEnumerable<QuestionContext> GetMatchingUserQuestionContexts(WorkflowInstance instance, string userId)
    {
        var workflowDefinition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        foreach (var form in workflowDefinition.Forms)
        {
            var submissionState = FormSubmissionState.Resolve(instance, form, workflowDefinition);
            foreach (var question in form.PropertyDefinitions.Where(q => q.DataType == DataType.User))
            {
                var context = new QuestionContext(instance, submissionState, form, question);
                if (ContainsUserAnswerValue(context, userId))
                    yield return context;
            }
        }
    }

    /// <summary>
    /// For user properties that are not part of a form
    /// </summary>
    private IEnumerable<PropertyDefinition> GetMatchingInstanceOnlyProperties(
        WorkflowInstance instance, string userId)
    {
        var workflowDefinition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];

        // Properties that appear in at least one form are already handled by GetMatchingUserQuestionContexts
        var propertiesInForms = workflowDefinition.Forms
            .SelectMany(f => f.PropertyDefinitions)
            .Select(p => p.Name)
            .ToHashSet();

        return workflowDefinition.Properties
            .Where(p => p.DataType == DataType.User && !propertiesInForms.Contains(p.Name))
            .Where(property =>
            {
                var rawValue = instance.GetProperty(property.Name);
                if (rawValue == null || rawValue.IsBsonNull) return false;

                if (property.IsArray)
                {
                    var users = ObjectContext.GetValue(rawValue, property) as InstanceUser[];
                    return users?.Any(u => u.Id == userId) == true;
                }

                var user = ObjectContext.GetValue(rawValue, property) as InstanceUser;
                return user?.Id == userId;
            });
    }


    private static bool ContainsUserAnswerValue(QuestionContext context, string userId)
    {
        var currentAnswer = context.Instance.GetProperty(context.Form.PropertyName, context.PropertyDefinition.Name);
        if (currentAnswer == null || currentAnswer.IsBsonNull)
            return false;

        if (context.PropertyDefinition.IsArray)
        {
            var users = ObjectContext.GetValue(currentAnswer, context.PropertyDefinition) as InstanceUser[];
            return users?.Any(u => u.Id == userId) == true;
        }

        var answerUser = ObjectContext.GetValue(currentAnswer, context.PropertyDefinition) as InstanceUser;
        return answerUser?.Id == userId;
    }

    private static bool TryCreateUpdatedUserAnswerValue(
        QuestionContext context,
        User user,
        out JsonElement? value)
    {
        var currentAnswer = context.Instance.GetProperty(context.Form.PropertyName, context.PropertyDefinition.Name);
        value = null;
        if (currentAnswer == null || currentAnswer.IsBsonNull)
            return false;

        var updatedUser = InstanceUser.FromUser(user);
        if (context.PropertyDefinition.IsArray)
        {
            var users = ObjectContext.GetValue(currentAnswer, context.PropertyDefinition) as InstanceUser[];
            if (users == null || users.All(u => u.Id != user.Id))
                return false;

            value = JsonSerializer.SerializeToElement(
                users.Select(u => u.Id == user.Id ? updatedUser : u).ToArray(),
                AnswerConversionService.Options);
            return true;
        }

        var answerUser = ObjectContext.GetValue(currentAnswer, context.PropertyDefinition) as InstanceUser;
        if (answerUser?.Id != user.Id)
            return false;

        value = JsonSerializer.SerializeToElement(updatedUser, AnswerConversionService.Options);
        return true;
    }

    private async Task<bool> CanEdit(QuestionContext context) =>
        await rightsService.Can(context.Instance,
            [context.SubmissionState.IsSubmitted ? RoleAction.Edit : RoleAction.Submit],
            RightsEvaluationMode.RequestContext,
            context.Form.Name);

    private static void UpdateInstanceUserProperty(WorkflowInstance instance, PropertyDefinition property, User user)
    {
        var rawValue = instance.GetProperty(property.Name);
        if (rawValue == null || rawValue.IsBsonNull) return;

        var updatedUser = InstanceUser.FromUser(user);
        if (property.IsArray)
        {
            var users = ObjectContext.GetValue(rawValue, property) as InstanceUser[];
            if (users == null) return;
            instance.Properties[property.Name] = new BsonArray(
                users.Select(u => u.Id == user.Id ? updatedUser.ToBsonDocument() : u.ToBsonDocument()));
        }
        else
        {
            instance.Properties[property.Name] = updatedUser.ToBsonDocument();
        }
    }
}