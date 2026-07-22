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
    IReadOnlyCollection<QuestionContext> EditableContexts);

public class ExternalUserEmailUpdateService(
    RightsService rightsService,
    AnswerService answerService,
    ModelService modelService)
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
            return new ExternalUserEmailAnswerUpdatePlan(
                ExternalUserEmailAnswerUpdateResult.UserNotInAnswer,
                []);

        var editableContexts = new List<QuestionContext>();
        foreach (var context in contexts)
        {
            if (await CanEdit(context))
                editableContexts.Add(context);
        }

        if (editableContexts.Count == 0)
            return new ExternalUserEmailAnswerUpdatePlan(
                ExternalUserEmailAnswerUpdateResult.Forbidden,
                []);

        return new ExternalUserEmailAnswerUpdatePlan(
            ExternalUserEmailAnswerUpdateResult.Updated,
            editableContexts);
    }

    public async Task UpdateAnswerReferences(
        ExternalUserEmailAnswerUpdatePlan plan,
        User user,
        CancellationToken ct)
    {
        foreach (var context in plan.EditableContexts)
        {
            if (!TryCreateUpdatedUserAnswerValue(context, user, out var updatedAnswerValue))
                continue;

            await answerService.SaveAnswer(context, updatedAnswerValue, ct);
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
}