using MongoDB.Bson;
using Moq;
using UvA.Workflow.Jobs;
using UvA.Workflow.Notifications;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UvA.Workflow.Tests;

public class SendAccessMailTests : ControllerTestsBase
{
    [Fact]
    public async Task SendAccessMail_PersonalizesArrayAndProvisionsOnlyEduIdUsers()
    {
        var workflow = _modelService.WorkflowDefinitions["Project"];
        workflow.Properties.Single(p => p.Name == "Supervisor").Type = "[User]!";
        var uva = User("uva@example.nl", "1234567", "UvA user");
        var external = User("external@example.org", "external@example.org", "External user");
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .WithProperties(("Supervisor", p => p.Array(uva.ToBsonDocument(), external.ToBsonDocument())))
            .Build();
        var sent = new List<MailMessage>();

        _loginMethodClassifierMock.Setup(c => c.Classify("1234567")).Returns(LoginMethod.Uva);
        _externalUserServiceMock
            .Setup(s => s.PrepareAccess("external@example.org", "External user", _ct))
            .ReturnsAsync(new ExternalUserAccessResult(
                new User
                {
                    Id = external.Id,
                    Email = external.Email,
                    UserName = external.UserName,
                    DisplayName = external.DisplayName,
                    ProviderKey = "edu-id",
                    InvitationState = UserInvitationState.Pending
                }, "https://invite.example/external", "EduId"));
        _mailServiceMock.Setup(m => m.Send(It.IsAny<MailMessage>(), _ct))
            .Callback<MailMessage, CancellationToken>((mail, _) => sent.Add(mail))
            .ReturnsAsync((MailMessage mail, CancellationToken _) =>
                new MailDispatchResult(mail.To, mail.Cc ?? [], mail.Bcc ?? []));

        var effect = new Effect
        {
            SendAccessMail = new SendMessage
            {
                To = "Supervisor",
                Subject = "Access for {{ AccessRecipient.DisplayName }}",
                Body = "{{ Access.LoginMethod }} {{ Access.InvitationUrl }}"
            }
        };
        var job = JobFor(effect);

        await _effectService.RunEffect(job, instance, effect, UnitTestsHelpers.AdminUser,
            _modelService.CreateContext(instance), _ct);

        Assert.Collection(sent,
            mail =>
            {
                Assert.Equal("uva@example.nl", Assert.Single(mail.To).MailAddress);
                Assert.Contains("Access for UvA user", mail.Subject);
                Assert.Contains("Uva", mail.Body);
                Assert.DoesNotContain("invite.example", mail.Body);
            },
            mail =>
            {
                Assert.Equal("external@example.org", Assert.Single(mail.To).MailAddress);
                Assert.Contains("Access for External user", mail.Subject);
                Assert.Contains("EduId", mail.Body);
                Assert.Contains("https://invite.example/external", mail.Body);
            });
        _externalUserServiceMock.VerifyAll();
    }

    [Fact]
    public async Task SendAccessMail_ContinuesAfterFailureAndRetriesOnlyFailedRecipient()
    {
        var workflow = _modelService.WorkflowDefinitions["Project"];
        workflow.Properties.Single(p => p.Name == "Supervisor").Type = "[User]!";
        var first = User("first@example.org", "first@example.org", "First user");
        var second = User("second@example.org", "second@example.org", "Second user");
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .WithProperties(("Supervisor", p => p.Array(first.ToBsonDocument(), second.ToBsonDocument())))
            .Build();
        var attempts = new List<string>();
        var failSecond = true;
        _externalUserServiceMock.Setup(s => s.PrepareAccess(It.IsAny<string>(), It.IsAny<string>(), _ct))
            .ReturnsAsync((string email, string name, CancellationToken _) =>
                new ExternalUserAccessResult(
                    new User
                    {
                        Id = ObjectId.GenerateNewId().ToString(),
                        Email = email,
                        UserName = email,
                        DisplayName = name
                    },
                    $"https://invite.example/{name}", "EduId"));
        _mailServiceMock.Setup(m => m.Send(It.IsAny<MailMessage>(), _ct))
            .Returns((MailMessage mail, CancellationToken _) =>
            {
                var address = Assert.Single(mail.To).MailAddress;
                attempts.Add(address);
                if (address == second.Email && failSecond)
                    throw new HttpRequestException("mail provider unavailable");
                return Task.FromResult(new MailDispatchResult(mail.To, [], []));
            });
        var effect = new Effect
        {
            SendAccessMail = new SendMessage
                { To = "Supervisor", Subject = "Access", Body = "{{ Access.InvitationUrl }}" }
        };
        var job = JobFor(effect);
        var checkpoints = new List<string[]>();

        await Assert.ThrowsAsync<AggregateException>(() => _effectService.RunEffect(job, instance, effect,
            UnitTestsHelpers.AdminUser, _modelService.CreateContext(instance), _ct,
            () =>
            {
                checkpoints.Add(CompletedRecipients(job));
                return Task.CompletedTask;
            }));
        failSecond = false;
        var retry = JobFor(effect);
        retry.Steps[0].Outputs = job.Steps[0].Outputs?.ToDictionary(output => output.Key, output => output.Value);
        await _effectService.RunEffect(retry, instance, effect, UnitTestsHelpers.AdminUser,
            _modelService.CreateContext(instance), _ct);

        Assert.Equal([first.Email, second.Email, second.Email], attempts);
        Assert.Equal([first.Email], Assert.Single(checkpoints));
    }

    [Fact]
    public void SendAccessMail_DeserializesFromWorkflowYaml()
    {
        var effect = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build()
            .Deserialize<Effect>("""
                                 sendAccessMail:
                                   template: SupervisorWelcome
                                   to: Supervisor
                                 """);

        Assert.Equal("SupervisorWelcome", effect.SendAccessMail!.TemplateKey);
        Assert.Equal("Supervisor", Assert.Single(effect.SendAccessMail.To!));
    }

    private static InstanceUser User(string email, string userName, string displayName) => new()
    {
        Id = ObjectId.GenerateNewId().ToString(),
        Email = email,
        UserName = userName,
        DisplayName = displayName
    };

    private static Job JobFor(Effect effect) => new()
    {
        Id = ObjectId.GenerateNewId().ToString(),
        Steps = [new JobStep { Identifier = effect.Identifier }]
    };

    private static string[] CompletedRecipients(Job job) =>
        Assert.IsType<string[]>(job.Steps[0].Outputs!["CompletedRecipients"]);
}