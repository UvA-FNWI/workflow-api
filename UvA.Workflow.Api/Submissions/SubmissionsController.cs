using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowInstances;
using UvA.Workflow.Infrastructure.Persistence;

namespace UvA.Workflow.Api.Submissions;

public class SubmissionsController(
    IWorkflowInstanceRepository workflowInstanceRepository,
    ModelService modelService,
    FileService fileService,
    ContextService contextService,
    TriggerService triggerService,
    InstanceService instanceService,
    RightsService rightsService,
    FileClient fileClient,
    WorkflowInstanceDtoService dtoService,
    AnswerConversionService answerConversionService) : ApiControllerBase
{
    [HttpGet("{instanceId}/{submissionId}")]
    public async Task<ActionResult<SubmissionDto>> GetSubmission(string instanceId, string submissionId, CancellationToken ct)
    {
        // Get the instance
        var inst = await workflowInstanceRepository.GetById(instanceId, ct);
        if (inst == null)
            return WorkflowInstanceNotFound;

        var formModel = modelService.GetForm(inst, submissionId);
        var sub = inst.Events.GetValueOrDefault(submissionId);

        return SubmissionDto.FromEntity(inst, formModel, sub, modelService.GetQuestionStatus(inst, formModel, true),
            fileService
        );
    }

    [HttpPost("{instanceId}/{submissionId}")]
    public async Task<ActionResult<SubmitSubmissionResult>> SubmitSubmission(string instanceId, string submissionId, CancellationToken ct)
    {
        // Get the instance
        var instance = await workflowInstanceRepository.GetById(instanceId, ct);
        if (instance == null)
            return WorkflowInstanceNotFound;

        var sub = instance.Events.GetValueOrDefault(submissionId);

        // Check if already submitted
        if (sub?.Date != null)
            return BadRequest("SubmissionsAlreadySubmitted", "Submission already submitted");
    
        var form = modelService.GetForm(instance, submissionId);
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

        await triggerService.RunTriggers(instance, [new Trigger { Event = submissionId }, ..form.OnSubmit], ct);

        // Save the updated instance
        await contextService.UpdateCurrentStep(instance, ct);

        var finalSubmissionDto = SubmissionDto.FromEntity(instance, form, instance.Events[submissionId],
            modelService.GetQuestionStatus(instance, form, true), fileService);
        var updatedInstanceDto = await dtoService.Create(instance, ct);

        return Ok(new SubmitSubmissionResult(finalSubmissionDto, updatedInstanceDto));
    }

    [HttpPatch("{instanceId}/{submissionId}")]
    public async Task<ActionResult<SaveAnswerResponse>> SaveAnswer(string instanceId, string submissionId,
        [FromBody] AnswerInput input, CancellationToken ct)
    {
        // Get the workflow instance
        var instance = await workflowInstanceRepository.GetById(instanceId, ct);
        if (instance == null)
            return WorkflowInstanceNotFound;

        // Get the submission
        var submission = instance.Events.GetValueOrDefault(submissionId);
        var form = modelService.GetForm(instance, submissionId);

        // Check authorization
        if (!await rightsService.Can(instance, submission?.Date == null ? RoleAction.Submit : RoleAction.Edit,
                form.Name))
            return Forbidden();

        // Get the question
        var question = modelService.GetQuestion(instance, form.Property, input.QuestionName);
        if (question == null)
            return NotFound("SubmissionsQuestionNotFound", "Question not found");

        // Get current answer
        var currentAnswer = instance.GetProperty(form.Property, input.QuestionName);

        // Convert new answer to BsonValue
        var newAnswer = await answerConversionService.ConvertToValue(input, question, ct);

        // Handle file upload if present
        if (input.File != null)
        {
            var fileInfo = await GetFileInfo(input.File);
            var fileId = await fileClient.StoreFile(fileInfo.FileName, fileInfo.Content);
            newAnswer = new StoredFile(fileInfo.FileName, fileId.ToString()).ToBsonDocument();
        }

        // Save if value changed
        if (newAnswer != currentAnswer)
        {
            instance.SetProperty(newAnswer, form.Property, input.QuestionName);
            await instanceService.SaveValue(instance, form.Property, question.Name, ct);
        }

        // Get questions to update (including dependent questions)
        var questionsToUpdate = question.DependentQuestions.Append(question).Distinct().ToArray();

        // Check if user can view hidden fields
        var canViewHidden = await rightsService.Can(instance, RoleAction.ViewHidden, form.Name);
        var updates = modelService.GetQuestionStatus(instance, form, canViewHidden, questionsToUpdate);

        // Build response
        var answers = Answer.Create(instance, form.TargetForm ?? form, updates);
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