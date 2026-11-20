using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using UvA.Workflow.Api.Submissions;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Controllers;

public class FormPreviewsControllerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MvcAcceptsEmptyPreviewAnswersButRejectsMissingAnswers(bool hasAnswers)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        using var provider = services.BuildServiceProvider();
        var action = new ActionContext
        {
            HttpContext = new DefaultHttpContext { RequestServices = provider },
            RouteData = new(),
            ActionDescriptor = new()
        };
        var request = new FormPreviewRequest(hasAnswers ? FormPreviewTests.Values("{}") : null!);

        provider.GetRequiredService<IObjectModelValidator>().Validate(action, null, "", request);

        Assert.Equal(hasAnswers, action.ModelState.IsValid);
    }

    [Theory]
    [InlineData(false, 401)]
    [InlineData(true, 404)]
    public async Task MissingUserOrInstanceCannotOpenPreview(bool loggedIn, int status)
    {
        var (service, instance, _) = FormPreviewTests.Create();
        var users = new Mock<IUserService>();
        users.Setup(u => u.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(loggedIn ? new User() : null);
        var controller = new FormPreviewsController(Mock.Of<IWorkflowInstanceRepository>(), users.Object, service)
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var result = await controller.Evaluate(instance.Id, "Review", new(FormPreviewTests.Values("{}")), default);
        Assert.Equal(status,
            (result.Result as ObjectResult)?.StatusCode ?? (result.Result as StatusCodeResult)?.StatusCode);
    }

    [Fact]
    public async Task PreviewReturnsAnIndependentDtoWithNoSavedAnswersOrArtifactTokens()
    {
        var (service, instance, _) = FormPreviewTests.Create();
        instance.Properties["Score"] = 9;
        var repository = new Mock<IWorkflowInstanceRepository>(MockBehavior.Strict);
        repository.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        var users = new Mock<IUserService>();
        users.Setup(u => u.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { UserName = "viewer" });
        var controller = new FormPreviewsController(repository.Object, users.Object, service)
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

        var action = await controller.Evaluate(instance.Id, "Review", new(FormPreviewTests.Values("{}")), default);

        var dto = Assert.IsType<FormPreviewDto>(Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.Equal("Review", dto.FormName);
        Assert.Null(dto.Answers.Single(a => a.QuestionName == "Score").Value);
        Assert.All(dto.Answers, answer => Assert.Null(answer.Files));
        Assert.Equal("no-store", controller.Response.Headers.CacheControl);
        Assert.Single(repository.Invocations);
    }

    [Fact]
    public async Task DirectRequestWithoutAPreviewGrantIsForbidden()
    {
        var (service, instance, model) = FormPreviewTests.Create();
        var definition = model.WorkflowDefinitions["Project"];
        foreach (var role in definition.Roles)
            role.Actions.RemoveAll(action => action.Type == RoleAction.Preview);
        definition.GlobalActions.Clear();
        await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
            service.Evaluate(instance, "Review", FormPreviewTests.Values("{}"), default));
    }
}