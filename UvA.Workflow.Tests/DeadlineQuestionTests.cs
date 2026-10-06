using UvA.Workflow.WorkflowModel.Conditions;
using UvA.Workflow.Tools;

namespace UvA.Workflow.Tests;

public class DeadlineQuestionTests
{
    private static Dictionary<string, string> Configuration() => new()
    {
        ["Common/Roles/Coordinator.yaml"] = "name: Coordinator",
        ["Project/Entity.yaml"] = """
                                  name: Project
                                  steps: [Phase]
                                  properties:
                                    - name: Deadline
                                      type: DateTime
                                      text: Submission deadline
                                    - name: UnusedDate
                                      type: Date
                                    - name: DateList
                                      type: '[Date]'
                                    - name: PostponementType
                                      type: String
                                    - name: PostponementDays
                                      type: Int
                                    - name: PostponementDate
                                      type: Date
                                      description:
                                        en: 'Existing date: {{ dateLong($deadline) }}'
                                        nl: 'Bestaande datum: {{ dateLong($deadline) }}'
                                      layout:
                                        width: 300
                                      condition:
                                        value:
                                          property: PostponementType
                                          equal: =Individual
                                    - name: Allowed
                                      type: Check
                                  globalActions:
                                    - name: Postpone
                                      type: Submit
                                      form: Postpone
                                      roles: [Coordinator]
                                      condition:
                                        value:
                                          property: Allowed
                                          equal: 'true'
                                  """,
        ["Project/Steps/Phase.yaml"] =
            "name: Phase\nchildren: [Submit, Duplicate, Calculated, List]\ndeadline:\n  date: Deadline",
        ["Project/Steps/Submit.yaml"] = "name: Submit\ndeadline:\n  date: Deadline",
        ["Project/Steps/Duplicate.yaml"] = "name: Duplicate\ndeadline:\n  date: Deadline",
        ["Project/Steps/Calculated.yaml"] = "name: Calculated\ndeadline:\n  date: addDays(Deadline, 1)",
        ["Project/Steps/List.yaml"] = "name: List\ndeadline:\n  date: DateList",
        ["Project/Steps/Unused.yaml"] = "name: Unused\ndeadline:\n  date: UnusedDate",
        ["Project/Forms/Postpone.yaml"] = """
                                          name: Postpone
                                          pages:
                                            - name: Dates
                                              elements:
                                                - text: Before
                                                - deadlines: true
                                                - text: After
                                          onSubmit:
                                            - sendMail:
                                                template: DeadlinesPostponed
                                          """
    };

    [Fact]
    public void CollectsOnlyEffectiveScalarDeadlinesOnceUsingTheTemplate()
    {
        var files = Configuration();
        files["Project/Forms/Other.yaml"] = "name: Other\npages:\n  - elements:\n      - deadlines: true";
        var definition = new ModelParser(new DictionaryProvider(files)).WorkflowDefinitions["Project"];
        var question = Assert.Single(definition.Properties, p => p.Name.StartsWith("New"));
        Assert.Equal("NewDeadline", question.Name);
        Assert.Equal("Submission deadline", question.DisplayName.En);
        Assert.Equal("Existing date: {{ dateLong(Deadline) }}", question.Description!.En);
        Assert.Equal("Bestaande datum: {{ dateLong(Deadline) }}", question.Description.Nl);
        Assert.Equal(definition.Properties.Single(p => p.Name == "PostponementDate").Layout, question.Layout);
        var form = definition.Forms.Single(f => f.Name == "Postpone");
        var elements = Assert.Single(form.Pages).PageElements;
        Assert.Equal("Before", elements[0].Text!.En);
        Assert.Same(question, elements[1].QuestionDefinition);
        Assert.Equal("After", elements[2].Text!.En);
        Assert.Equal("Deadline", form.OnSubmit[0].SetProperty!.Property);
        Assert.Equal("DeadlinesPostponed", form.OnSubmit[1].SendMail!.TemplateKey);
        Assert.Single(definition.Forms.Single(f => f.Name == "Other").OnSubmit);
    }

    [Fact]
    public void QuestionsAndActionsRequireInitializedDeadlinesAndPreserveValidation()
    {
        var definition = new ModelParser(new DictionaryProvider(Configuration())).WorkflowDefinitions["Project"];
        var question = definition.Properties.Single(p => p.Name == "NewDeadline");
        var action = Assert.Single(definition.GlobalActions);
        var values = new Dictionary<Lookup, object?> { ["Allowed"] = true, ["PostponementType"] = "Individual" };
        var context = new ObjectContext(values);
        Assert.False(question.Condition.IsMet(context));
        Assert.False(action.Condition.IsMet(context));
        values["Deadline"] = new DateTime(2027, 1, 15, 12, 0, 0);
        Assert.True(question.Condition.IsMet(context));
        Assert.True(action.Condition.IsMet(context));
        values["NewDeadline"] = new DateTime(2027, 1, 14);
        Assert.False(question.Validation.IsMet(context));
        values["NewDeadline"] = new DateTime(2027, 1, 16);
        Assert.True(question.Validation.IsMet(context));
        var effect = definition.Forms.Single().OnSubmit[0].SetProperty!;
        Assert.Equal(new DateTime(2027, 1, 16, 12, 0, 0), effect.ValueExpression.Execute(context));
        values["PostponementType"] = "All";
        values["PostponementDays"] = 3;
        Assert.False(question.Condition.IsMet(context));
        Assert.Equal(new DateTime(2027, 1, 18, 12, 0, 0), effect.ValueExpression.Execute(context));
        values["Allowed"] = false;
        Assert.False(action.Condition.IsMet(context));
    }

    [Fact]
    public void InheritedFormsCollectEachChildsDeadlinesWithoutLeakingDependencies()
    {
        var files = Configuration();
        files["Child/Entity.yaml"] = "name: Child\ninheritsFrom: Project";
        files["Child/Steps/Submit.yaml"] = "name: Submit\ndeadline:\n  date: UnusedDate";
        files["Sibling/Entity.yaml"] = "name: Sibling\ninheritsFrom: Project";
        var definitions = new ModelParser(new DictionaryProvider(files)).WorkflowDefinitions;
        Assert.Equal(new[] { "NewDeadline", "NewUnusedDate" }, definitions["Child"].Forms.Single().Pages.Single()
            .PageElements.Where(e => e.Question != null).Select(e => e.Question));
        foreach (var name in new[] { "Project", "Sibling" })
        {
            Assert.DoesNotContain(definitions[name].Properties, p => p.Name == "NewUnusedDate");
            Assert.DoesNotContain(definitions[name].Properties.Single(p => p.Name == "UnusedDate").DependentQuestions,
                p => p.Name == "NewUnusedDate");
        }
    }
}