using UvA.Workflow.Submissions;

namespace UvA.Workflow.Tests;

public class FormSubmissionSemanticsTests
{
    [Fact]
    public void ModelParser_AppliesBackwardCompatibleFormSubmissionDefaults()
    {
        var parser = new ModelParser(new SubmissionSemanticsContentProvider("""
                                                                            name: TestWorkflow
                                                                            titlePlural: Test Workflows
                                                                            properties:
                                                                              - name: Title
                                                                                type: String
                                                                            """, """
            name: Start
            """));

        var form = parser.WorkflowDefinitions["TestWorkflow"].Forms.Single();

        Assert.True(form.EmitFormSubmitEvent);
        Assert.Null(form.SubmittedWhenEvents);
        Assert.Equal(["Start"], FormSubmissionState.GetSubmissionEventIds(form));
    }

    [Fact]
    public void ModelParser_AllowsSubmittedWhenReferencingOnSubmitEvent()
    {
        var parser = new ModelParser(new SubmissionSemanticsContentProvider("""
                                                                            name: TestWorkflow
                                                                            titlePlural: Test Workflows
                                                                            properties:
                                                                              - name: Title
                                                                                type: String
                                                                            """, """
            name: Start
            emitFormSubmitEvent: false
            submittedWhenEvents: [SubmittedByEffect]
            onSubmit:
              - event: SubmittedByEffect
            """));

        var workflowDef = parser.WorkflowDefinitions["TestWorkflow"];
        var form = workflowDef.Forms.Single();

        Assert.NotNull(form.SubmittedWhenEvents);
        Assert.Equal(["SubmittedByEffect"], form.SubmittedWhenEvents);
        Assert.Contains(workflowDef.Events, e => e.Name == "SubmittedByEffect");
    }

    [Fact]
    public void ModelParser_RejectsUnknownSubmittedWhenEvent()
    {
        var exception = Assert.Throws<Exception>(() => new ModelParser(new SubmissionSemanticsContentProvider("""
            name: TestWorkflow
            titlePlural: Test Workflows
            properties:
              - name: Title
                type: String
            """, """
                 name: Start
                 submittedWhenEvents: [UnknownEvent]
                 """)));

        Assert.Contains("unknown submittedWhenEvents event", exception.Message);
    }

    [Fact]
    public void FormSubmissionState_UsesConfiguredSubmissionEvents()
    {
        var workflowDef = new WorkflowDefinition
        {
            Name = "TestWorkflow",
            Events =
            [
                new EventDefinition { Name = "Start" },
                new EventDefinition { Name = "SubmittedByEffect" }
            ]
        };
        var form = new Form
        {
            Name = "Start",
            SubmittedWhenEvents = ["SubmittedByEffect"]
        };
        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("TestWorkflow")
            .WithCurrentStep("Start")
            .WithEvent("SubmittedByEffect", DateTime.UtcNow.AddMinutes(-1))
            .Build();

        var state = FormSubmissionState.Resolve(instance, form, workflowDef);

        Assert.True(state.IsSubmitted);
        Assert.Equal(instance.Events["SubmittedByEffect"].Date, state.DateSubmitted);
        Assert.Equal(["SubmittedByEffect"], state.ActiveSubmissionEventIds);
    }

    private sealed class SubmissionSemanticsContentProvider(string entityYaml, string formYaml) : IContentProvider
    {
        public IEnumerable<string> GetFolders(string? directory = null)
            => directory == null ? ["TestWorkflow"] : Array.Empty<string>();

        public IEnumerable<string> GetFiles(string directory) => directory switch
        {
            "TestWorkflow" => ["TestWorkflow/Entity.yaml"],
            "TestWorkflow/Forms" => ["TestWorkflow/Forms/Start.yaml"],
            _ => Array.Empty<string>()
        };

        public string GetFile(string file) => file switch
        {
            "TestWorkflow/Entity.yaml" => entityYaml,
            "TestWorkflow/Forms/Start.yaml" => formYaml,
            _ => ""
        };
    }
}