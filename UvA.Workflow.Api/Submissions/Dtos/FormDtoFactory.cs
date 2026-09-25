namespace UvA.Workflow.Api.Submissions.Dtos;

/// <summary>
/// Creates form DTOs with the instance data required by their elements.
/// </summary>
public class FormDtoFactory(ModelService modelService, InstanceService instanceService)
{
    public Task<FormDto> Create(WorkflowInstance instance, string formName, CancellationToken ct)
        => Create(instance, modelService.GetForm(instance, formName), ct);

    public async Task<FormDto> Create(WorkflowInstance instance, Form form, CancellationToken ct)
    {
        var context = modelService.CreateContext(instance);
        var lookups = form.ActualForm.Lookups.Distinct().ToArray();

        if (lookups.Length > 0)
        {
            await instanceService.Enrich(
                modelService.WorkflowDefinitions[instance.WorkflowDefinition],
                [context],
                lookups,
                ct);
        }

        return FormDto.Create(form, context);
    }
}