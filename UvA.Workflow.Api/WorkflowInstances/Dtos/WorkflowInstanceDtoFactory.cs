using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowDefinitions.Dtos;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public class WorkflowInstanceDtoFactory(
    InstanceService instanceService,
    ModelService modelService,
    SubmissionDtoFactory submissionDtoFactory,
    IWorkflowInstanceRepository repository,
    RightsService rightsService)
{
    /// <summary>
    /// Creates a WorkflowInstanceDto from a WorkflowInstance domain entity
    /// </summary>
    public async Task<WorkflowInstanceDto> Create(WorkflowInstance instance, CancellationToken ct)
    {
        var actions = await instanceService.GetAllowedActions(instance, ct);
        var submissions = await instanceService.GetAllowedSubmissions(instance, ct);
        var workflowDefinition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var permissions = await rightsService.GetAllowedActions(instance, RoleAction.ViewAdminTools, RoleAction.Edit);

        var x = new WorkflowInstanceDto(
            instance.Id,
            workflowDefinition.InstanceTitleTemplate?.Apply(modelService.CreateContext(instance)),
            WorkflowDefinitionDto.Create(modelService.WorkflowDefinitions[instance.WorkflowDefinition]),
            instance.CurrentStep,
            instance.ParentId,
            actions.Select(ActionDto.Create).ToArray(),
            CreateFields(workflowDefinition, instance.Id, ct).Result ?? [],
            workflowDefinition.Steps.Select(s => StepDto.Create(s, instance, modelService)).ToArray(),
            submissions
                .Select(s => submissionDtoFactory.Create(instance, s.Form, s.Event, s.QuestionStatus,
                    permissions.Where(p => p.MatchesForm(s.Form.Name)).Select(p => p.Type).ToArray()))
                .ToArray(),
            permissions.Where(a => a.AllForms.Length == 0).Select(a => a.Type).Distinct().ToArray()
        );
        return x;
    }

    private async Task<FieldDto[]> CreateFields(WorkflowDefinition workflowDefinition, string instanceId,
        CancellationToken ct)
    {
        var result = new List<FieldDto>();
        var instance = await repository.GetById(instanceId, ct);
        if (instance is not null)
        {
            var context = ObjectContext.Create(instance, modelService);
            await instanceService.Enrich(workflowDefinition, context,
                workflowDefinition.HeaderFields.SelectMany(f => f.Properties), ct);
            foreach (var field in workflowDefinition.HeaderFields)
            {
                var obj = ProcessColumnValue(context, field);
                result.Add(new FieldDto(field.DisplayTitle, obj));
            }
        }

        return result.ToArray();
    }

    private object? ProcessColumnValue(
        ObjectContext context,
        Field column
    )
    {
        if (column.CurrentStep)
        {
            // Return current step value or default
            return context.Values["CurrentStep"] ?? column.Default ?? "Draft";
        }

        if (column.ValueTemplate != null)
        {
            return column.ValueTemplate.Execute(context);
        }

        if (!string.IsNullOrEmpty(column.Property))
        {
            // Get property value from raw data
            return GetNestedPropertyValue(context, column.Property);
        }

        return column.Default;
    }

    private static object? GetNestedPropertyValue(ObjectContext context, string propertyPath)
    {
        var parts = propertyPath.Split('.');

        if (!context.Values.TryGetValue(parts[0], out var rootValue) || rootValue == null)
            return null;

        // If only one part, return the root value
        if (parts.Length == 1) return rootValue;

        var propName = parts[1];
        if (rootValue is not System.Collections.IEnumerable list) return GetValue(rootValue);
        var values = (from object? l in list select GetValue(l)).ToList();
        return string.Join(", ", values);

        object? GetValue(object obj) => obj switch
        {
            WorkflowInstance instance => instance.Properties[propName].AsString,
            _ => obj.GetType().GetProperty(propName)?.GetValue(obj, null) // Fallback to reflection
        };
    }
}