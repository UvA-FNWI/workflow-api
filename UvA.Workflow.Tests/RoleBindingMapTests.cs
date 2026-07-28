using UvA.Workflow.Tests.Helpers;

namespace UvA.Workflow.Tests;

public class RoleBindingMapTests
{
    private readonly RoleBindingMap roleBindings = UnitTestsHelpers.CreateModelParser().RoleBindings;

    [Fact]
    public void CompilesScalarUserPropertiesAsDirectBindings()
    {
        var binding = Assert.Single(roleBindings.GetBindings("Project", "Student"));

        Assert.Equal(RoleBindingSource.Direct, binding.Source);
        Assert.Equal("Student", binding.PropertyName);
        Assert.False(binding.IsArray);
        Assert.Equal("Properties.Student", binding.PropertyPath);
        Assert.Equal("Properties.Student._id", binding.UserIdPath);
        Assert.Null(binding.ReferencedWorkflowDefinition);
        Assert.Null(binding.ReferencedUserIdPath);
    }

    [Fact]
    public void CompilesUserArraysAsDirectBindings()
    {
        var binding = Assert.Single(roleBindings.GetBindings("Context", "Coordinator"));

        Assert.Equal(RoleBindingSource.Direct, binding.Source);
        Assert.True(binding.IsArray);
        Assert.Equal("Properties.Coordinator._id", binding.UserIdPath);
    }

    [Fact]
    public void CompilesRolesInheritedThroughReferences()
    {
        var binding = Assert.Single(roleBindings.GetBindings("Project", "Coordinator"));

        Assert.Equal(RoleBindingSource.Inherited, binding.Source);
        Assert.Equal("Course", binding.PropertyName);
        Assert.False(binding.IsArray);
        Assert.Equal("Properties.Course", binding.PropertyPath);
        Assert.Equal("Context", binding.ReferencedWorkflowDefinition);
        Assert.Equal("Coordinator", binding.ReferencedRolePropertyName);
        Assert.True(binding.ReferencedRoleIsArray);
        Assert.Equal("Properties.Coordinator._id", binding.ReferencedUserIdPath);
        Assert.Null(binding.UserIdPath);
    }

    [Fact]
    public void LookupIsCaseInsensitive()
    {
        var binding = Assert.Single(roleBindings.GetBindings("project", "student"));

        Assert.Equal("Project", binding.WorkflowDefinition);
        Assert.Equal("Student", binding.Role);
    }

    [Fact]
    public void CanLookupOneRoleAcrossWorkflowDefinitions()
    {
        var bindings = roleBindings.GetBindingsForRole("student");

        Assert.Contains(bindings,
            binding => binding.WorkflowDefinition == "Project" && binding.Source == RoleBindingSource.Direct);
        Assert.Contains(bindings,
            binding => binding.WorkflowDefinition == "Project-PP" && binding.Source == RoleBindingSource.Direct);
    }

    [Fact]
    public void NonUserPropertiesAreNotDirectRoleBindings()
    {
        Assert.DoesNotContain(roleBindings.GetBindings("Project"),
            binding => binding.Source == RoleBindingSource.Direct && binding.PropertyName == "Title");
    }

    [Fact]
    public void AllRolesHaveEnglishAndDutchTitles()
    {
        var roles = UnitTestsHelpers.CreateModelParser().Roles;

        Assert.All(roles, role =>
        {
            Assert.NotNull(role.Title);
            Assert.False(string.IsNullOrWhiteSpace(role.Title.En), $"{role.Name} has no English title");
            Assert.False(string.IsNullOrWhiteSpace(role.Title.Nl), $"{role.Name} has no Dutch title");
        });
    }
}