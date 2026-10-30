using MongoDB.Bson;
using Moq;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Tests;

public class ComputedRuleDependencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Permissions_LoadChainedReferenceInputs(bool blocked)
    {
        var (model, repository, instance, target) = Create();
        target["Blocked"] = blocked;
        var rights = CreateRights(model, repository);

        Assert.Equal(!blocked, await rights.Can(instance, RoleAction.Edit));
        Assert.Equal(!blocked,
            (await rights.GetAllowedActionsForStep(instance, "Review", RoleAction.Edit)).Length > 0);
        Assert.Equal("target", instance.Properties["Target"].AsString);

        target["Blocked"] = !blocked;
        Assert.Equal(blocked, await rights.Can(instance, RoleAction.Edit));
    }

    [Theory]
    [InlineData(true, false, "Review")]
    [InlineData(false, false, "Next")]
    [InlineData(true, true, "Next")]
    public async Task StepProgression_LoadsReferenceConditions(bool ready, bool finished, string expected)
    {
        var (model, repository, instance, target) = Create();
        target["Ready"] = ready;
        target["Finished"] = finished;
        var service = new InstanceService(repository.Object, model, null!, null!, null!, null!);

        await service.UpdateCurrentStep(instance, default);

        Assert.Equal(expected, instance.CurrentStep);
        var rights = CreateRights(model, repository);
        var actions = await rights.GetAllowedActions(instance, RoleAction.Execute);
        Assert.Equal(expected, Assert.Single(actions).Name);
    }

    [Theory]
    [InlineData(2000, false)]
    [InlineData(2999, true)]
    public async Task HardDeadline_LoadsReferenceDateBeforeCheckingPermissions(int year, bool allowed)
    {
        var (model, repository, instance, target) = Create();
        target["DueDate"] = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rights = CreateRights(model, repository);

        Assert.Equal(allowed, await rights.Can(instance, RoleAction.Execute));
        Assert.True(await rights.Can(instance, RoleAction.View));
        var perRole = await rights.GetAllowedActionsPerTargetRole(instance, RoleAction.Execute);
        Assert.Equal(allowed, perRole.SelectMany(role => role.Actions).Any());
    }

    [Theory]
    [InlineData(true, false, "Review", "Review")]
    [InlineData(true, false, "Review", "Next")]
    [InlineData(false, false, "Next", "Review")]
    [InlineData(true, true, "Next", "Review")]
    public async Task InstanceResponse_UsesSameReferenceInputsForStepsAndActions(
        bool ready, bool finished, string expected, string savedStep)
    {
        var (model, repository, instance, target) = Create();
        target["Ready"] = ready;
        target["Finished"] = finished;
        instance.CurrentStep = savedStep;
        var factory = StepHeaderStatusTests.CreateWorkflowInstanceDtoFactory(model, repository);

        var dto = await factory.Create(instance, default);

        Assert.Equal(expected, Assert.Single(dto.Actions).Name);
        Assert.Equal(expected, dto.CurrentStep);
        Assert.Equal(savedStep, instance.CurrentStep);
    }

    [Fact]
    public async Task MissingReference_DoesNotGrantNegatedPermission()
    {
        var (model, repository, instance, _) = Create();
        repository.Setup(r => r.GetAllById(It.IsAny<string[]>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var rights = CreateRights(model, repository);

        await Assert.ThrowsAsync<InvalidWorkflowStateException>(() => rights.Can(instance, RoleAction.Edit));
    }

    [Fact]
    public async Task HardDeadline_RejectsAnUnresolvedDateInsteadOfLeavingActionsOpen()
    {
        var (model, repository, instance, _) = Create();
        var definition = model.WorkflowDefinitions["Project"];
        foreach (var action in definition.Roles.SelectMany(role => role.Actions))
            action.Condition = null;
        var review = definition.AllSteps.Single(step => step.Name == "Review");
        review.Condition = null;
        review.Ends = null;
        repository.Setup(r => r.GetAllById(It.IsAny<string[]>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await Assert.ThrowsAsync<InvalidWorkflowStateException>(() =>
            CreateRights(model, repository).Can(instance, RoleAction.Execute));
    }

    [Fact]
    public async Task Permissions_PreserveReferenceIdentityAlongsideLoadedFields()
    {
        var (model, repository, instance, _) = Create();
        var role = model.WorkflowDefinitions["Project"].Roles.Single(r => r.Name == "Registered");
        role.Actions.Single(action => action.Type == RoleAction.Edit).Condition = new Condition
        {
            Not = true, Value = new Value { Property = "Target", Equal = "=target" }
        };
        var rights = CreateRights(model, repository);

        Assert.False(await rights.Can(instance, RoleAction.Edit));
        var context = await new InstanceService(repository.Object, model, null!, null!, null!, null!)
            .CreateContext(instance, model.WorkflowDefinitions["Project"].PermissionAndStepLookups);
        Assert.True(context.Get("MatchesTarget") as bool?);
        Assert.False(context.Get("AliasBlocked") as bool?);
    }

    [Fact]
    public async Task InstanceResponse_PreservesReferenceComparisonsInStepConditions()
    {
        var (model, repository, instance, _) = Create();
        model.WorkflowDefinitions["Project"].AllSteps.Single(step => step.Name == "Review").Condition = new()
        {
            Logical = new Logical
            {
                Operator = LogicalOperator.And,
                Children =
                [
                    new() { Value = new Value { Property = "Ready", Equal = "true" } },
                    new() { Value = new Value { Property = "Target", Equal = "=target" } }
                ]
            }
        };
        var factory = StepHeaderStatusTests.CreateWorkflowInstanceDtoFactory(model, repository);

        var dto = await factory.Create(instance, default);

        Assert.Contains(dto.Steps, step => step.Id == "Review");
        Assert.Equal("Review", Assert.Single(dto.Actions).Name);
    }

    [Theory]
    [InlineData("Review", true, "Review", "Review project", "Project beoordelen")]
    [InlineData("Review", false, "Next", "Next step", "Volgende stap")]
    [InlineData("Next", true, "Review", "Review project", "Project beoordelen")]
    public async Task InstanceResponse_UsesResolvedStepForLocalizedFieldsAndCards(
        string savedStep, bool ready, string expected, string expectedEn, string expectedNl)
    {
        var (model, repository, instance, target) = Create();
        instance.CurrentStep = savedStep;
        target["Ready"] = ready;
        var definition = model.WorkflowDefinitions["Project"];
        definition.AllSteps.Single(step => step.Name == "Review").Title = new("Review project", "Project beoordelen");
        definition.AllSteps.Single(step => step.Name == "Next").Title = new("Next step", "Volgende stap");
        definition.InstanceTitle = "{{ StepName }}";
        definition.Fields = [new() { CurrentStep = true }];
        definition.InfoCards =
        [
            new()
            {
                Name = "Status", Type = InfoCardType.User, User = "Student", Title = "Status",
                Fields = [new() { CurrentStep = true }]
            }
        ];
        var factory = StepHeaderStatusTests.CreateWorkflowInstanceDtoFactory(model, repository);

        var dto = await factory.Create(instance, default);

        Assert.Equal(expected, dto.CurrentStep);
        Assert.Equal(expected, dto.Title);
        var title = Assert.IsType<BilingualString>(Assert.Single(dto.Fields).Value);
        Assert.Equal(expectedEn, title.En);
        Assert.Equal(expectedNl, title.Nl);
        var cardTitle = Assert.IsType<BilingualString>(Assert.Single(Assert.Single(dto.InfoCards).Fields!).Value);
        Assert.Equal(expectedEn, cardTitle.En);
        Assert.Equal(expectedNl, cardTitle.Nl);
        Assert.Equal(savedStep, instance.CurrentStep);
    }

    [Fact]
    public async Task InstanceResponse_LoadsSharedRuleAndFieldReferencesOnce()
    {
        var (model, repository, instance, target) = Create();
        var definition = model.WorkflowDefinitions["Project"];
        var referenced = model.WorkflowDefinitions["Target"];
        referenced.Properties.Add(new StringPropertyDefinition { Name = "Name", Type = "String" });
        target["Name"] = "Course name";
        definition.Fields = [new() { Property = "Target.Name" }];
        var factory = StepHeaderStatusTests.CreateWorkflowInstanceDtoFactory(model, repository);

        var dto = await factory.Create(instance, default);

        Assert.Equal("Review", Assert.Single(dto.Actions).Name);
        Assert.Equal("Course name", Assert.Single(dto.Fields).Value);
        repository.Verify(r => r.GetAllById(It.IsAny<string[]>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task History_RecalculatesStepWithReferenceInputsWithoutChangingLiveInstance()
    {
        var (model, repository, instance, target) = Create();
        instance.CurrentStep = "Review";
        target["Ready"] = false;
        var service = new WorkflowInstanceService(model, repository.Object, null!, null!, null!, null!);

        var snapshot = await service.GetAsOfTimestamp(instance, DateTime.UtcNow,
            new WorkflowInstanceHistory(null, []));

        Assert.Equal("Next", snapshot.CurrentStep);
        Assert.Equal("Review", instance.CurrentStep);
        Assert.Equal("target", snapshot.Properties["Target"].AsString);
    }

    [Theory]
    [InlineData("contains(Targets.Blocked, true)", false)]
    [InlineData("contains(Targets.Blocked, true)", true)]
    [InlineData("contains(select(Targets, =Blocked), true)", false)]
    [InlineData("contains(select(Targets, =Blocked), true)", true)]
    [InlineData("contains(select(except(Targets, =first), =Blocked), true)", false)]
    [InlineData("contains(select(except(Targets, =first), =Blocked), true)", true)]
    public async Task Permissions_LoadReferenceArrays(string expression, bool blocked)
    {
        var model = DerivedPropertyTests.Parse($"""
                                                name: Project
                                                properties:
                                                  - name: Targets
                                                    type: '[Target]'
                                                  - name: Blocked
                                                    type: Check
                                                    expression: {expression}
                                                  - name: HasSecond
                                                    type: Check
                                                    expression: contains(Targets, =second)
                                                """, new()
        {
            ["Target/Entity.yaml"] = "name: Target\nproperties:\n  - name: Blocked\n    type: Check",
            ["Project/Roles/Registered.yaml"] = """
                                                name: Registered
                                                actions:
                                                  - type: Edit
                                                    condition:
                                                      not: true
                                                      value:
                                                        property: Blocked
                                                        equal: true
                                                """
        });
        var repository = new Mock<IWorkflowInstanceRepository>();
        repository.Setup(r => r.GetAllById(It.IsAny<string[]>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new() { ["_id"] = "first", ["Blocked"] = false },
                new() { ["_id"] = "second", ["Blocked"] = blocked }
            ]);
        var instance = new WorkflowInstance
        {
            WorkflowDefinition = "Project", Properties = new() { ["Targets"] = new BsonArray { "first", "second" } },
            Events = []
        };

        var rights = CreateRights(model, repository);
        Assert.Equal(!blocked, await rights.Can(instance, RoleAction.Edit));
        var context = await new InstanceService(repository.Object, model, null!, null!, null!, null!)
            .CreateContext(instance, model.WorkflowDefinitions["Project"].PermissionAndStepLookups);
        Assert.True(context.Get("HasSecond") as bool?);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Impersonation_LoadsReferenceInputsForEachRole(bool blocked)
    {
        var (model, repository, instance, target) = Create();
        target["Blocked"] = blocked;
        var role = model.WorkflowDefinitions["Project"].Roles.Single(r => r.Name == "Registered");
        role.Actions.Single(action => action.Type == RoleAction.View).Condition =
            role.Actions.Single(action => action.Type == RoleAction.Edit).Condition;
        var rights = CreateRights(model, repository);

        Assert.Equal(!blocked, (await rights.GetImpersonationTargetRoles(instance)).Length > 0);
        Assert.Equal(!blocked, await rights.NormalizeImpersonationTargetRole(instance, "Registered") != null);
        Assert.Equal(!blocked,
            (await rights.GetAllowedActionsPerTargetRole(instance, RoleAction.Edit)).Length > 0);
    }

    [Fact]
    public void Parser_RejectsAssessmentInputsHiddenBehindReference()
    {
        Assert.ThrowsAny<Exception>(() => DerivedPropertyTests.Parse("""
                                                                     name: Project
                                                                     properties:
                                                                       - name: Target
                                                                         type: Target
                                                                       - name: Grade
                                                                         type: Double
                                                                         expression: Target.Grade
                                                                     """, new()
        {
            ["Target/Entity.yaml"] = """
                                     name: Target
                                     properties:
                                       - name: Grade
                                         type: Double
                                         expression: Assessment.FinalGrade
                                     """,
            ["Project/Roles/Registered.yaml"] = """
                                                name: Registered
                                                actions:
                                                  - type: Edit
                                                    condition:
                                                      value:
                                                        property: Grade
                                                        equal: 10
                                                """
        }));
    }

    private static RightsService CreateRights(ModelService model, Mock<IWorkflowInstanceRepository> repository)
    {
        var user = new Mock<IUserService>();
        user.Setup(u => u.GetRolesOfCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        return new RightsService(model, user.Object, repository.Object);
    }

    private static (ModelService, Mock<IWorkflowInstanceRepository>, WorkflowInstance,
        Dictionary<string, BsonValue>) Create()
    {
        var model = DerivedPropertyTests.Parse("""
                                               name: Project
                                               steps: [Review, Next]
                                               properties:
                                                 - name: StepName
                                                   type: String
                                                   expression: CurrentStep
                                                 - name: Target
                                                   type: Target
                                                 - name: Blocked
                                                   type: Check
                                                   expression: Target.Blocked
                                                 - name: IsBlocked
                                                   type: Check
                                                   expression: Blocked
                                                 - name: Alias
                                                   type: Target
                                                   expression: Target
                                                 - name: MatchesTarget
                                                   type: Check
                                                   expression: Alias == =target
                                                 - name: AliasBlocked
                                                   type: Check
                                                   expression: Alias.Blocked
                                                 - name: Ready
                                                   type: Check
                                                   expression: Target.Ready
                                                 - name: Finished
                                                   type: Check
                                                   expression: Target.Finished
                                                 - name: DueDate
                                                   type: Date
                                                   expression: Target.DueDate
                                               """, new()
        {
            ["Target/Entity.yaml"] = """
                                     name: Target
                                     properties:
                                       - name: Blocked
                                         type: Check
                                       - name: Ready
                                         type: Check
                                       - name: Finished
                                         type: Check
                                       - name: DueDate
                                         type: Date
                                     """,
            ["Project/Roles/Registered.yaml"] = """
                                                name: Registered
                                                actions:
                                                  - type: Edit
                                                    condition:
                                                      not: true
                                                      value:
                                                        property: IsBlocked
                                                        equal: true
                                                  - type: View
                                                """,
            ["Project/Steps/Review.yaml"] = """
                                            name: Review
                                            condition:
                                              value:
                                                property: Ready
                                                equal: true
                                            ends:
                                              value:
                                                property: Finished
                                                equal: true
                                            deadline:
                                              date: DueDate
                                              type: Hard
                                            actions:
                                              - name: Review
                                                type: Execute
                                                roles: [Registered]
                                            """,
            ["Project/Steps/Next.yaml"] = """
                                          name: Next
                                          actions:
                                            - name: Next
                                              type: Execute
                                              roles: [Registered]
                                          """
        });
        var target = new Dictionary<string, BsonValue>
        {
            ["_id"] = "target", ["Blocked"] = false, ["Ready"] = true, ["Finished"] = false,
            ["DueDate"] = new DateTime(2999, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var repository = new Mock<IWorkflowInstanceRepository>();
        repository.Setup(r => r.GetAllById(It.Is<string[]>(ids => ids.SequenceEqual(new[] { "target" })),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [target]);
        var instance = new WorkflowInstance
        {
            Id = ObjectId.GenerateNewId().ToString(), WorkflowDefinition = "Project",
            Properties = new() { ["Target"] = "target" },
            Events = []
        };
        return (model, repository, instance, target);
    }
}