using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests;

public class PostponementConfigurationTests
{
    [Fact]
    public void ExecuteActionReusesTypedFormAndSharedChoiceConfiguration()
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
                                          type: Execute
                                          form: ExtensionDialog
                                          roles: [Coordinator]
                                          onAction:
                                            - sendMail:
                                                template: DeadlinesPostponed
                                      """,
            ["Project/Forms/ExtensionDialog.yaml"] = """
                                                     name: ExtensionDialog
                                                     title: Extend deadlines
                                                     type: PostponeDeadline
                                                     layout: Modal
                                                     pages:
                                                       - name: Reason
                                                         fields: [Reason, Explanation]
                                                     """
        })));
        var definition = model.WorkflowDefinitions["Project"];
        var action = Assert.Single(definition.GlobalActions);
        var instance = new WorkflowInstance { WorkflowDefinition = "Project", Properties = new(), Events = new() };
        var form = model.GetForm(instance, action.Form!);
        var dto = FormDto.Create(form, model.CreateContext(instance), availableChoicesOnly: true);
        Assert.Equal(FormType.PostponeDeadline, dto.Type);
        Assert.Equal(FormLayout.Modal, dto.Layout);
        Assert.Equal("Other", Assert.Single(dto.Pages[0].Questions[0].Choices!).Name);
        Assert.Equal("DeadlinesPostponed", Assert.Single(action.OnAction).SendMail!.TemplateKey);
        Assert.Empty(instance.Properties);
    }
}