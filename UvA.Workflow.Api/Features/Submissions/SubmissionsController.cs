using UvA.Workflow.Api.Exceptions;
using UvA.Workflow.Api.Extensions;
using UvA.Workflow.Infrastructure.Persistence;

namespace UvA.Workflow.Api.Features.Submissions;

public class SubmissionsController(
    WorkflowInstanceService workflowInstanceService,
    ModelService modelService,
    FileService fileService,
    ContextService contextService,
    TriggerService triggerService,
    InstanceService instanceService,
    RightsService rightsService,
    FileClient fileClient,
    AnswerConversionService answerConversionService) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SubmissionDto>> GetSubmission(string instanceId, string formName)
    {
        // Get the instance
        var inst = await workflowInstanceService.GetByIdAsync(instanceId);
        if (inst == null)
            return ErrorCode.WorkflowInstancesNotFound;

        var formModel = modelService.GetForm(inst, formName);
        var sub = inst.Events.GetValueOrDefault(formName);

        return SubmissionDto.FromEntity(inst, formModel, sub, modelService.GetQuestionStatus(inst, formModel, true),
            fileService
        );
    }

    [HttpPost]
    public async Task<ActionResult<SubmitSubmissionResult>> SubmitSubmission(string instanceId, string formName)
    {
        // Get the instance
        var instance = await workflowInstanceService.GetByIdAsync(instanceId);
        if (instance == null)
            return ErrorCode.WorkflowInstancesNotFound;

        var sub = instance.Events.GetValueOrDefault(formName);

        // Check if already submitted
        if (sub?.Date != null)
            return ErrorCode.SubmissionsAlreadySubmitted;

        var form = modelService.GetForm(instance, formName);
        var context = modelService.CreateContext(instance);

        // Validate required fields
        var missing = form.Questions
            .Where(q => q.IsRequired && !instance.HasAnswer(q.Name)
                                     && q.Condition.IsMet(context))
            .Select(q => new InvalidQuestion(q.Name, new BilingualString("Required field", "Verplicht veld")))
            .ToArray();

        // Validate field validation rules
        var invalid = form.Questions
            .Where(q => instance.HasAnswer(q.Name) && !q.Validation.IsMet(context))
            .Select(q => new InvalidQuestion(
                q.Name,
                q.Validation!.Message ?? new BilingualString("Invalid value", "Ongeldige waarde")
            ));

        var validationErrors = missing.Concat(invalid).ToArray();

        if (validationErrors.Any())
        {
            var submissionDto = SubmissionDto.FromEntity(instance, form, sub,
                modelService.GetQuestionStatus(instance, form, true), fileService);
            return Ok(new SubmitSubmissionResult(submissionDto, null, validationErrors, false));
        }

        await triggerService.RunTriggers(instance, [new Trigger { Event = formName }, ..form.OnSubmit]);

        // Save the updated instance
        await contextService.UpdateCurrentStep(instance);

        var finalSubmissionDto = SubmissionDto.FromEntity(instance, form, instance.Events[formName],
            modelService.GetQuestionStatus(instance, form, true), fileService);
        var updatedInstanceDto = WorkflowInstanceDto.From(instance);

        return Ok(new SubmitSubmissionResult(finalSubmissionDto, updatedInstanceDto));
    }

    [HttpPost]
    public async Task<ActionResult<SaveAnswerResponse>> SaveAnswer([FromBody] SaveAnswerRequest request)
    {
        // Get the workflow instance
        var instance = await instanceService.Get(request.InstanceId);
        if (instance == null)
            return ErrorCode.WorkflowInstancesNotFound;

        // Get the submission
        var submission = instance.Events.GetValueOrDefault(request.SubmissionId);
        var form = modelService.GetForm(instance, request.SubmissionId);

        // Check authorization
        if (!await rightsService.Can(instance, submission?.Date == null ? RoleAction.Edit : RoleAction.Submit,
                form.Name))
            return ErrorCode.GeneralForbidden;

        // Get the question
        var question = modelService.GetQuestion(instance, form.Property, request.Answer.QuestionName);
        if (question == null)
            return ErrorCode.SubmissionsQuestionNotFound;

        // Get current answer
        var currentAnswer = instance.GetProperty(form.Property, request.Answer.QuestionName);

        // Convert new answer to BsonValue
        var newAnswer = await answerConversionService.ConvertToValueAsync(request.Answer, question);

        // Handle file upload if present
        if (request.Answer.File != null)
        {
            var fileInfo = await GetFileInfo(request.Answer.File);
            var fileId = await fileClient.StoreFile(fileInfo.FileName, fileInfo.Content);
            newAnswer = new StoredFile(fileInfo.FileName, fileId.ToString()).ToBsonDocument();
        }

        // Save if value changed
        if (newAnswer != currentAnswer)
        {
            instance.SetProperty(newAnswer, form.Property, request.Answer.QuestionName);
            await instanceService.SaveValue(instance, form.Property, question.Name);
        }

        // Get questions to update (including dependent questions)
        var questionsToUpdate = question.DependentQuestions.Append(question).Distinct().ToArray();

        // Check if user can view hidden fields
        var canViewHidden = await rightsService.Can(instance, RoleAction.ViewHidden, form.Name);
        var updates = modelService.GetQuestionStatus(instance, form, canViewHidden, questionsToUpdate);

        // Build response
        var answers = Answer.FromEntities(instance, form.TargetForm ?? form, updates);
        var updatedSubmission = SubmissionDto.FromEntity(instance, form, submission);

        return Ok(new SaveAnswerResponse(true, answers, updatedSubmission));
    }

    private static async Task<FileInfo> GetFileInfo(IFormFile file)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        return new FileInfo(file.FileName, ms.ToArray());
    }

    private record FileInfo(string FileName, byte[] Content);
}