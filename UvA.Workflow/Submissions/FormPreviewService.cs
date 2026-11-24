using System.Text.Json;
using UvA.Workflow.Assessments;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Submissions;

public class PreviewPageResult : PageResult
{
    public int AnsweredCount { get; set; }
    public int QuestionCount { get; set; }
}

public record FormPreview(
    Form Form,
    ObjectContext Context,
    Answer[] Answers,
    PreviewPageResult[] PageResults,
    Dictionary<string, Choice[]> ReferenceChoices);

public partial class FormPreviewService(
    ModelService modelService,
    RightsService rightsService,
    IWorkflowInstanceRepository repository)
{
    /// <summary>Checks the form grant and evaluates temporary answers without changing the instance.</summary>
    public async Task<FormPreview> Evaluate(WorkflowInstance instance, string formName,
        IReadOnlyDictionary<string, JsonElement?> answers, CancellationToken ct)
    {
        var original = modelService.TryGetForm(instance, formName)
                       ?? throw new EntityNotFoundException("Form", formName);
        if (!(await rightsService.GetPreviewableForms(instance, ct)).Any(preview => preview.Form == original))
            throw new ForbiddenWorkflowActionException(instance.Id, RoleAction.Preview, formName);

        var form = original.Clone();
        form.WorkflowDefinition = original.WorkflowDefinition;
        form.TargetForm = original.TargetForm?.Clone();
        if (form.TargetForm != null)
            form.TargetForm.WorkflowDefinition = original.ActualForm.WorkflowDefinition;
        form.ActualForm.Pages = original.ActualForm.Pages.Where(page => original.TargetForm == null ||
                                                                        page.Sources == null ||
                                                                        page.Sources.Contains(original.PropertyName!))
            .Select(page => page.Clone()).ToList();
        var questions = form.ActualForm.PropertyDefinitions.ToArray();
        var hidden = (await rightsService.GetAllowedActions(instance, RoleAction.ViewHidden))
            .Where(action => action.MatchesForm(form.Name) ||
                             action.AllForms.Length == 0 && action.PropertyDefinition != null).ToArray();
        var (snapshot, readableContext) = await CreateSnapshot(instance, original, ct);
        foreach (var (name, value) in answers)
        {
            var question = questions.FirstOrDefault(q => q.Name == name)
                           ?? throw new ArgumentException($"Question '{name}' is not in this preview.");
            if (!CanAccess(question))
                throw new ArgumentException($"Question '{name}' is not available.");
            snapshot.SetProperty(ConvertInput(value, question), form.PropertyName, name);
        }

        var selectedReferences = new Dictionary<Lookup, object?>();
        var referenceChoices = new Dictionary<string, Choice[]>();
        var context = RebuildContext();
        await RefreshReferences();
        foreach (var question in questions.Where(q => q.DefaultExpression != null &&
                                                      !answers.ContainsKey(q.Name) && q.DataType != DataType.File &&
                                                      CanAccess(q)))
        {
            var value = question.DefaultExpression!.Execute(form.GetContext(context));
            snapshot.SetProperty(
                ConvertInput(
                    value == null ? null : JsonSerializer.SerializeToElement(value, AnswerConversionService.Options),
                    question),
                form.PropertyName, question.Name);
            context = RebuildContext();
            if (question is ReferencePropertyDefinition)
                await RefreshReferences();
        }

        if (questions.Any(question => question.DefaultExpression != null))
            await RefreshReferences();
        var statuses = modelService.GetQuestionStatus(snapshot, form, hidden, questions, context,
            includeSourceFields: true);
        var results = AssessmentHelpers.CalculateSourceResult(form, context, null).PageResults
            .Where(result => form.ActualForm.Pages.Any(page => page.Name == result.Name &&
                                                               page.Condition.IsMet(form.GetContext(context))))
            .Select(result =>
            {
                var visibleQuestions = result.QuestionResults.Where(question => statuses[question.Name].IsVisible)
                    .ToList();
                var calculationQuestions = visibleQuestions.Where(question => question.Weight > 0).ToArray();
                var answered = calculationQuestions.Count(question =>
                    form.GetContext(context).Get(question.Name) is not null &&
                    statuses[question.Name].ValidationError == null);
                return new PreviewPageResult
                {
                    Name = result.Name, Weight = result.Weight, Sum = result.Sum,
                    QuestionResults = visibleQuestions,
                    QuestionCount = calculationQuestions.Length, AnsweredCount = answered,
                    WeightedAverage = answered == calculationQuestions.Length ? result.WeightedAverage : null
                };
            }).ToArray();
        foreach (var page in form.ActualForm.Pages)
            page.PageElements = page.PageElements
                .Where(element => element.Question == null || statuses[element.Question].IsVisible).ToArray();
        return new FormPreview(form, context, Answer.Create(snapshot, form, statuses), results, referenceChoices);

        bool CanAccess(PropertyDefinition question) => question.Visibility != PropertyVisibility.Hidden ||
                                                       hidden.Any(action =>
                                                           action.PropertyDefinition == null ||
                                                           action.PropertyDefinition == question.Name);

        // Evaluate filters and labels against each candidate's readable snapshot.
        async Task RefreshReferences()
        {
            foreach (var key in selectedReferences.Keys)
                context.LoadedReferenceValues.Remove(key);
            selectedReferences.Clear();
            foreach (var question in questions.OfType<ReferencePropertyDefinition>().Where(CanAccess))
            {
                var value = snapshot.GetProperty(form.PropertyName, question.Name);
                var values = value switch
                {
                    BsonArray array => array.ToArray(), null or BsonNull => [], _ => new[] { value }
                };
                var targetDefinition = question.WorkflowDefinition!;
                var candidates = await repository.GetByWorkflowDefinition(targetDefinition.Name,
                    FilterDefinition<WorkflowInstance>.Empty, ct);
                var available = new List<Choice>();
                var selectedContexts = new Dictionary<string, Dictionary<Lookup, object?>>();
                var scoped = form.GetContext(context);
                foreach (var candidate in candidates)
                {
                    if (!await rightsService.Can(candidate, RoleAction.View)) continue;
                    var (_, candidateContext) = await CreateSnapshot(candidate, original, ct, instance.Id);
                    var filterContext = scoped.WithValues(new Dictionary<Lookup, object?>
                        { [targetDefinition.Name] = candidateContext.Values });
                    if (!question.Filter.IsMet(filterContext)) continue;
                    available.Add(new Choice
                    {
                        Name = candidate.Id,
                        Text = targetDefinition.InstanceTitleTemplate?.Apply(candidateContext) ?? candidate.Id
                    });
                    if (values.Any(value => value is BsonString id && id.AsString == candidate.Id))
                        selectedContexts[candidate.Id] = candidateContext.Values;
                }

                referenceChoices[question.Name] = available.ToArray();
                if (values.Any(value =>
                        value is not BsonNull && !available.Any(choice => choice.Name == value.AsString)))
                    throw new ArgumentException($"Unavailable reference for '{question.Name}'.");
                if (form.TargetForm == null)
                {
                    var selected = values.Select(value => value is BsonString id
                        ? selectedContexts.GetValueOrDefault(id.AsString)
                        : null).ToArray();
                    selectedReferences[question.Name] = value is BsonArray ? selected : selected.FirstOrDefault();
                }
            }

            foreach (var (key, value) in selectedReferences)
                context.LoadedReferenceValues[key] = value;
            context.ComputeProperties(form.WorkflowDefinition);
        }

        // Recompute derived values after changing a temporary answer.
        ObjectContext RebuildContext()
        {
            var result = modelService.CreateContext(snapshot);
            foreach (var (key, value) in readableContext.LoadedReferenceValues)
                result.LoadedReferenceValues[key] = value;
            foreach (var (key, value) in selectedReferences)
                result.LoadedReferenceValues[key] = value;
            result.ComputeProperties(form.WorkflowDefinition);
            return result;
        }
    }
}