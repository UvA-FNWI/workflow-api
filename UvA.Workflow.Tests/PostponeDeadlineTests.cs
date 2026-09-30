using MongoDB.Bson;
using Moq;
using UvA.Workflow.Expressions;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Journaling;
using UvA.Workflow.Notifications;
using UvA.Workflow.Submissions;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel.Conditions;
using Action = UvA.Workflow.WorkflowModel.Action;

namespace UvA.Workflow.Tests;

public class PostponeDeadlineTests : ControllerTestsBase
{
    private readonly WorkflowInstance _instance;
    private readonly WorkflowDefinition _definition;
    private readonly Form _form;
    private readonly SubmissionService _service;
    private static readonly DateTime Original = new(2000, 1, 1, 11, 0, 0, DateTimeKind.Utc);

    public PostponeDeadlineTests()
    {
        _definition = _modelService.WorkflowDefinitions["Project"];
        var questions = new[]
        {
            new PropertyDefinition
            {
                Name = "Days", Type = "Int!", Validation = new()
                    { Value = new() { Property = "Days", GreaterThan = "0" } }
            },
            new PropertyDefinition
            {
                Name = "PostponementReason", Type = "Choice!", Values =
                [
                    new() { Name = "Other", Text = new("Other", "Overig") },
                    new() { Name = "Research", Text = new("Research", "Onderzoek") }
                ]
            },
            new PropertyDefinition
            {
                Name = "PostponementExplanation", Type = "String!", Condition = new()
                    { Value = new() { Property = "PostponementReason", Equal = "=Other" } }
            }
        };
        foreach (var question in questions) question.ParentType = _definition;
        _definition.Properties.AddRange(questions);
        _definition.Properties.Add(
            new PropertyDefinition { Name = "Deadline", Type = "Date", ParentType = _definition });
        _definition.AllSteps.Single(s => s.Name == "Start").Deadline = new()
            { Date = "Deadline", Type = DeadlineType.Hard };
        _form = new Form
        {
            Name = "PostponeDeadlines", Layout = FormLayout.Modal,
            WorkflowDefinition = _definition,
            Pages =
            [
                new Page
                {
                    Name = "Postpone", PageElements = questions.Select(q => new PageElement
                        { Question = q.Name, QuestionDefinition = q }).ToArray()
                }
            ],
            OnSubmit =
            [
                new() { SetProperty = new() { Property = "Deadline", Value = "addDays(Deadline, Days)" } },
                new() { SendMail = new() { TemplateKey = "DeadlinesPostponed" } }
            ]
        };
        _definition.Forms.Add(_form);
        var action = new Action
        {
            Name = "Postpone", Type = RoleAction.Submit, Form = _form.Name,
            WorkflowDefinition = _definition.Name, Roles = ["Coordinator"]
        };
        _definition.GlobalActions.Add(action);
        _definition.Roles.Single(r => r.Name == "Coordinator").Actions.Add(action);
        _definition.Emails.Add(new TemplateMessage
        {
            Name = "DeadlinesPostponed", To = "student@example.org",
            Subject = "Extended", Body = "Deadline: {{ Deadline }}"
        });
        _instance = new WorkflowInstance
        {
            Id = "507f1f77bcf86cd799439011", WorkflowDefinition = "Project",
            CurrentStep = "Start", Properties = new() { ["Deadline"] = Original }, Events = new()
        };
        MockInstance(_instance);
        MockEmptyRelatedInstanceLookups();
        MockCurrentUser("Coordinator");
        _service = new(_modelService, _instanceService, _instanceJournalServiceMock.Object, _jobService,
            _effectService);
    }

    private void Draft(int days, string reason = "Research", string? explanation = null)
    {
        _instance.Properties["Days"] = days;
        _instance.Properties["PostponementReason"] = reason;
        if (explanation != null) _instance.Properties["PostponementExplanation"] = explanation;
    }

    private Task<SubmissionResult> Submit() => _service.SubmitSubmission(new(_instance,
            FormSubmissionState.Resolve(_instance, _form, _definition), _form, _form.Name), UnitTestsHelpers.AdminUser,
        _ct);

    [Fact]
    public async Task GlobalFormCanBeSubmittedMoreThanOnceAndUpdatesTheLiveDeadline()
    {
        Draft(7, "Other", "Fieldwork delayed");
        var first = await Submit();
        Assert.True(first.Success);
        Assert.Equal(Original.AddDays(7), _instance.GetProperty("Deadline")!.ToUniversalTime());
        Assert.True(first.SubmissionState.IsSubmitted);

        Draft(3, "Research");
        var second = await Submit();
        Assert.True(second.Success);
        Assert.Equal(Original.AddDays(10), _instance.GetProperty("Deadline")!.ToUniversalTime());
        _mailServiceMock.Verify(m => m.Send(It.IsAny<MailMessage>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Contains(await _instanceService.GetAllowedActions(_instance, _ct), a => a.Form == _form);
    }

    [Fact]
    public async Task InvalidDraftDoesNotUpdateTheDeadlineOrSendMail()
    {
        Draft(-1, "Other");
        var result = await Submit();
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.QuestionName == "Days");
        Assert.Contains(result.Errors, e => e.QuestionName == "PostponementExplanation");
        Assert.Equal(Original, _instance.GetProperty("Deadline")!.ToUniversalTime());
        _mailServiceMock.Verify(m => m.Send(It.IsAny<MailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MaximumExtensionUsesTheOriginalDeadlineFromTheJournal()
    {
        _definition.AllSteps.Single(s => s.Name == "Start").Deadline!.MaxPostponementDays = 10;
        _instanceJournalServiceMock.Setup(j => j.GetInstanceJournal(_instance.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InstanceJournalEntry
            {
                PropertyChanges = [PropertyChangeEntry.Create("Deadline", Original, UnitTestsHelpers.AdminUser)]
            });
        Draft(11);
        var result = await Submit();
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.ValidationMessage.En == "MaximumExtensionExceeded");
        Assert.Equal(Original, _instance.GetProperty("Deadline")!.ToUniversalTime());
    }

    [Fact]
    public async Task FormQuestionsCannotWriteTheLiveDeadline()
    {
        var question = await _answerService.GetQuestionContext(_instance.Id, _form.Name, "Deadline", _ct);
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            _answerService.SaveAnswer(question, System.Text.Json.JsonSerializer.SerializeToElement("2100-01-01"), _ct));
    }

    [Fact]
    public void IndividualDatePreservesDeadlineTime()
    {
        var previous = new DateTime(2027, 3, 27, 11, 0, 0, DateTimeKind.Utc);
        var selected = new DateTime(2027, 4, 3, 22, 0, 0, DateTimeKind.Utc);
        var context = new ObjectContext(new()
            { ["Previous"] = previous.ToLocalTime(), ["Selected"] = selected.ToLocalTime() });
        var result = Assert.IsType<DateTime>(ExpressionParser.Parse("withDate(Previous, Selected)").Execute(context));
        Assert.Equal(new DateTime(2027, 4, 4, 10, 0, 0, DateTimeKind.Utc), result.ToUniversalTime());
    }

    [Theory]
    [InlineData("2027-03-27T11:00:00Z", "2027-03-28T10:00:00Z")]
    [InlineData("2027-10-30T10:00:00Z", "2027-10-31T11:00:00Z")]
    public void CalendarExtensionsPreserveAmsterdamTime(string previous, string expected)
    {
        var context = new ObjectContext(new() { ["Deadline"] = DateTime.Parse(previous).ToLocalTime() });
        var result = Assert.IsType<DateTime>(ExpressionParser.Parse("addDays(Deadline, 1)").Execute(context));
        Assert.Equal(DateTime.Parse(expected).ToUniversalTime(), result.ToUniversalTime());
    }
}