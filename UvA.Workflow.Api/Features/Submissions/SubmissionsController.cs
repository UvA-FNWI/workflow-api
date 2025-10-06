namespace UvA.Workflow.Api.Features.Submissions;

[ApiController]
[Route("api/submissions")]
public class SubmissionsController(
    WorkflowInstanceService workflowInstanceService,
    ModelService modelService,
    FileService fileService,
    ContextService contextService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SubmissionDto>> GetSubmission(string instanceId, string formName)
    {
        // Get the instance
        var inst = await workflowInstanceService.GetByIdAsync(instanceId);
        if (inst == null)
            return NotFound(new { error = $"WorkflowInstance with ID '{instanceId}' not found" });
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
            return NotFound(new { error = $"WorkflowInstance with ID '{instanceId}' not found" });

        var sub = instance.Events.GetValueOrDefault(formName);

        // Check if already submitted
        if (sub?.Date != null)
            return BadRequest(new { error = "Already submitted" });

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

        // Record submission event
        instance.RecordEvent(formName);

        // TODO: Run triggers when trigger service is available
        // await triggerService.RunTriggers(instance, [new Trigger { Event = formName }, ..form.OnSubmit]);

        // Save the updated instance
        await contextService.UpdateCurrentStep(instance);

        var finalSubmissionDto = SubmissionDto.FromEntity(instance, form, instance.Events[formName],
            modelService.GetQuestionStatus(instance, form, true), fileService);
        var updatedInstanceDto = WorkflowInstanceDto.From(instance);

        return Ok(new SubmitSubmissionResult(finalSubmissionDto, updatedInstanceDto));
    }
}