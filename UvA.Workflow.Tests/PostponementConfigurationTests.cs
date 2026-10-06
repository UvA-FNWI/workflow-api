using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests;

public class PostponementConfigurationTests
{
    [Fact]
    public void PostponementActionReusesFormAndSharedChoiceConfiguration()
    {
        var model = new ModelService(new ModelParser(new DictionaryProvider(new()
        {
            ["Common/Roles/Coordinator.yaml"] = "name: Coordinator",
            ["Common/ValueSets/DelayReasons.yaml"] = "name: DelayReasons\nvalues:\n  - name: Other\n    text: Other",
            ["Project/Entity.yaml"] = """
                                      name: Project
                                      properties:
                                        - name: Reason
                                          type: DelayReasons!
                                        - name: Explanation
                                          type: String
                                      globalActions:
                                        - name: GrantExtension
                                          type: Submit
                                          form: ExtensionDialog
                                          roles: [Coordinator]
                                      """,
            ["Project/Forms/ExtensionDialog.yaml"] = """
                                                     name: ExtensionDialog
                                                     title: Extend deadlines
                                                     layout: Modal
                                                     onSubmit:
                                                       - sendMail:
                                                           template: DeadlinesPostponed
                                                     pages:
                                                       - name: Reason
                                                         elements:
                                                           - text:
                                                               en: Choose deadlines to extend.
                                                               nl: Kies deadlines om uit te stellen.
                                                           - callout:
                                                               variant: Info
                                                               title: Contact the board
                                                               text: The board can adjust the date.
                                                           - question: Reason
                                                           - question: Explanation
                                                     """
        })));
        var definition = model.WorkflowDefinitions["Project"];
        var action = Assert.Single(definition.GlobalActions);
        var instance = new WorkflowInstance { WorkflowDefinition = "Project", Properties = new(), Events = new() };
        var form = model.GetForm(instance, action.Form!);
        var dto = FormDto.Create(form, model.CreateContext(instance));
        Assert.Equal(RoleAction.Submit, action.Type);
        Assert.Null(form.Step);
        Assert.Equal(FormLayout.Modal, dto.Layout);
        Assert.Equal("Choose deadlines to extend.", dto.Pages[0].Elements[0].Text!.En);
        Assert.Equal("Contact the board", dto.Pages[0].Elements[1].Callout!.Title!.En);
        Assert.Equal("Other", Assert.Single(dto.Pages[0].Elements[2].Question!.Choices!).Name);
        Assert.Equal("DeadlinesPostponed", Assert.Single(form.OnSubmit).SendMail!.TemplateKey);
        Assert.Empty(instance.Properties);
    }

    [Theory]
    [InlineData("Date", 0, true)]
    [InlineData("Date!", 28, true)]
    [InlineData("DateTime", 14, true)]
    [InlineData("Date", -1, false)]
    [InlineData("[Date]", 14, false)]
    [InlineData("String", 14, false)]
    public void DeadlineMaximumRequiresANonNegativeDirectScalarDate(string type, int days, bool valid)
    {
        var provider = new DictionaryProvider(new()
        {
            ["Project/Entity.yaml"] =
                $"name: Project\nsteps: [Start]\nproperties:\n  - name: Deadline\n    type: '{type}'",
            ["Project/Steps/Start.yaml"] = $"name: Start\ndeadline:\n  date: Deadline\n  maxPostponementDays: {days}"
        });
        if (valid)
            Assert.Equal(days, new ModelService(new ModelParser(provider)).WorkflowDefinitions["Project"]
                .AllSteps.Single().Deadline!.MaxPostponementDays);
        else
            Assert.Contains("maxPostponementDays",
                Assert.ThrowsAny<Exception>(() => new ModelService(new ModelParser(provider))).Message);
    }
}