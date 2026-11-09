using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.WorkflowInstances;

public partial class InstanceService
{
    /// <summary>Loads the form's expression inputs into the containing workflow instance's context.</summary>
    public async Task<ObjectContext> CreateFormContext(WorkflowInstance instance, Form form, CancellationToken ct,
        IEnumerable<PropertyDefinition>? questions = null)
    {
        var context = modelService.CreateContext(instance);
        await Enrich(modelService.WorkflowDefinitions[instance.WorkflowDefinition], [context],
            GetFormLookups(form, questions), ct, replaceStep: false);
        return context;
    }

    public static IEnumerable<Lookup> GetFormLookups(Form form, IEnumerable<PropertyDefinition>? questions = null)
    {
        var lookups = form.ActualForm.Lookups.Concat((questions ?? form.ActualForm.PropertyDefinitions)
            .SelectMany(question => question.Conditions).SelectMany(condition => condition.Properties));
        return form.TargetForm != null ? lookups.SelectMany(ScopeLookup) : lookups;

        IEnumerable<Lookup> ScopeLookup(Lookup lookup) => lookup switch
        {
            PropertyLookup property when form.ActualForm.WorkflowDefinition.Properties.Contains(property.Parts[0]) =>
                [new PropertyLookup($"{form.PropertyName}.{property.Property}")],
            ComplexLookup complex => complex.Arguments.SelectMany(argument => argument.Properties)
                .SelectMany(ScopeLookup),
            _ => [lookup]
        };
    }

    public async Task<Dictionary<string, QuestionStatus>> GetQuestionStatus(WorkflowInstance instance, Form form,
        bool canViewHidden, CancellationToken ct, IEnumerable<PropertyDefinition>? questions = null)
    {
        var context = await CreateFormContext(instance, form, ct, questions);
        return modelService.GetQuestionStatus(instance, form, canViewHidden, questions, context);
    }

    public async Task<Dictionary<string, QuestionStatus>> GetQuestionStatus(WorkflowInstance instance, Form form,
        CancellationToken ct, IEnumerable<PropertyDefinition>? questions = null)
    {
        var context = await CreateFormContext(instance, form, ct, questions);
        return await rightsService.GetQuestionStatus(instance, form, questions, context);
    }
}