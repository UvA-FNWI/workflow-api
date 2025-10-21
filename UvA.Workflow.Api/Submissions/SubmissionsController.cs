using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowInstances;
using UvA.Workflow.Infrastructure.Persistence;
using ZstdSharp.Unsafe;

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

    [HttpPost("{instanceId}/{submissionId}/{questionName}")]
    public async Task<ActionResult<SaveAnswerResponse>> SaveAnswer(string instanceId, string submissionId,string questionName,
        [FromBody] AnswerInput input, CancellationToken ct)
    {
        var (instance, submission, form, question, error) = await GetQuestionContext(instanceId, submissionId, questionName, ct);
        if (error is not null) return error;
        
        // Get current answer
        var currentAnswer = instance!.GetProperty(form!.Property, questionName);

        // Convert new answer to BsonValue
        var newAnswer = await answerConversionService.ConvertToValue(input, question!, ct);

        // Save if value changed
        if (newAnswer != currentAnswer)
        {
            instance.SetProperty(newAnswer, form.Property, questionName);
            await instanceService.SaveValue(instance, form.Property, question!.Name, ct);
        }

        // Get questions to update (including dependent questions)
        var questionsToUpdate = question!.DependentQuestions.Append(question).Distinct().ToArray();

        // Check if user can view hidden fields
        var canViewHidden = await rightsService.Can(instance, RoleAction.ViewHidden, form.Name);
        var updates = modelService.GetQuestionStatus(instance, form, canViewHidden, questionsToUpdate);

        // Build response
        var answers = Answer.Create(instance, form.TargetForm ?? form, updates);
        var updatedSubmission = SubmissionDto.FromEntity(instance, form, submission);

        return Ok(new SaveAnswerResponse(true, answers, updatedSubmission));
        
    }

    [HttpPost("{instanceId}/{submissionId}/{questionName}/files")]
    [Consumes("multipart/form-data")]
    [Produces("application/json")]
    public async Task<ActionResult<SaveAnswerResponse>> SaveAnswerFile(string instanceId, string submissionId,string questionName,
        [FromForm] IFormFile file, CancellationToken ct)
    {
        var (instance, _, form, question, error) = await GetQuestionContext(instanceId, submissionId, questionName, ct);
        if (error is not null) return error;
        
        var fileInfo = SaveToObjectStore(file);
    
        var value = instance!.GetProperty(form!.Property, questionName);
        if (question!.IsArray)
        {
            var array = value as BsonArray ?? [];
            array.Add(fileInfo.ToBsonDocument());
            value = array;
        }
        else
        {
            value = fileInfo.ToBsonDocument();
        }
        instance.SetProperty(value, form.Property, questionName);
    
        await instanceService.SaveValue(instance, form.Property, question.Name, ct);
        return Ok(new SaveAnswerFileResponse(true));
        
    }
    
    [HttpDelete("{instanceId}/{submissionId}/{questionName}/files/{fileId}")]
    public async Task<IActionResult> DeleteAnswerFile(string instanceId, string submissionId,string questionName, string fileId, CancellationToken ct)
    {
        var (instance, _, form, question, error) = await GetQuestionContext(instanceId, submissionId, questionName, ct);
        if (error is not null) return error;
        
        var value = instance!.GetProperty(form!.Property, questionName);
        if (value == null) return NotFound();
        if (question!.IsArray)
        {
            var array = value as BsonArray ?? [];
            foreach(var file in array)
                if (file["Id"].AsString == fileId)
                {
                    await fileClient.DeleteFile(fileId);
                    array.Remove(file);
                    break;
                }
        }
        else
        {
            await fileClient.DeleteFile(value["Id"].AsString);
            instance.ClearProperty(questionName);
        }
        await instanceService.SaveValue(instance, form.Property, question.Name, ct);
        return Ok(new SaveAnswerFileResponse(true));
    }


    /// <summary>
    /// Retrieves the workflow instance, submission, form, and question based on the provided identifiers
    /// and validates their existence and authorization while checking for potential errors.
    /// </summary>
    /// <param name="instanceId">The identifier of the workflow instance.</param>
    /// <param name="submissionId">The identifier of the specific submission.</param>
    /// <param name="questionName">The name of the question to retrieve within the form.</param>
    /// <param name="ct">A token used to cancel the operation.</param>
    /// <returns>
    /// A tuple containing the workflow instance, submission, form, and question if successful, or an error result
    /// if any of the entities cannot be retrieved or authorization fails.
    /// </returns>
    private async Task<(WorkflowInstance? instance, InstanceEvent? submission, Form? form, Question? question, ActionResult? error)> GetQuestionContext(string instanceId, string submissionId, string questionName, CancellationToken ct)
    {
        // Get the workflow instance
        var instance = await workflowInstanceRepository.GetById(instanceId, ct);
        if (instance == null)
            return Err(WorkflowInstanceNotFound);

        // Get the submission
        var submission = instance.Events.GetValueOrDefault(submissionId);
        var form = modelService.GetForm(instance, submissionId);
        // Check authorization
        if (!await rightsService.Can(instance, submission?.Date == null ? RoleAction.Submit : RoleAction.Edit, form.Name))
            return Err(Forbidden());

        // Get the question
        var question = modelService.GetQuestion(instance, form.Property, questionName);
        if (question == null)
            return Err(NotFound("SubmissionsQuestionNotFound", "Question not found"));

        return (instance, submission, form, question, null);

        (WorkflowInstance? instance, InstanceEvent? submission, Form? form, Question? question, ActionResult? error) Err(ActionResult err) => (null, null, null, null, err);
    }

    private async Task<StoredFileInfo> SaveToObjectStore(IFormFile file)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var fileId = await fileClient.StoreFile(file.FileName, ms.ToArray());
        return new StoredFileInfo(file.FileName, fileId.ToString());
    }
}