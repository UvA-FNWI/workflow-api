using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Tools;

namespace UvA.Workflow.Tests;

public class ValueSetSortingTests
{
    private static ModelParser ParseWithValueSet(string valueSetYaml)
        => new(new DictionaryProvider(new Dictionary<string, string>
        {
            ["Common/ValueSets/Test.yaml"] = valueSetYaml
        }));

    [Fact]
    public void Sorting_IsPropagatedFromValueSetToConsumingProperty()
    {
        var parser = UnitTestsHelpers.CreateModelParser();
        var country = parser.WorkflowDefinitions["Project-PA"].Properties.Get("Country");

        Assert.NotNull(country.Sorting);
        Assert.Equal(ChoiceSortField.Text, country.Sorting!.Field);
        Assert.Equal(SortDirection.Ascending, country.Sorting.Direction);
    }

    [Fact]
    public void Sorting_OnFieldPresentOnAllValues_Parses()
    {
        var parser = ParseWithValueSet("""
                                       name: Test
                                       sorting:
                                         field: Value
                                         direction: Descending
                                       values:
                                         - name: A
                                           value: 1
                                         - name: B
                                           value: 2
                                       """);

        Assert.NotNull(parser);
    }

    [Fact]
    public void Sorting_OnFieldMissingFromSomeValues_Throws()
    {
        var exception = Assert.Throws<Exception>(() => ParseWithValueSet("""
                                                                         name: Test
                                                                         sorting:
                                                                           field: Value
                                                                           direction: Ascending
                                                                         values:
                                                                           - name: A
                                                                             value: 1
                                                                           - name: B
                                                                         """));

        Assert.Contains("not present on all values", exception.Message);
        Assert.Contains("B", exception.Message);
    }

    [Fact]
    public void Sorting_OnUnknownField_Throws()
    {
        var exception = Assert.Throws<Exception>(() => ParseWithValueSet("""
                                                                         name: Test
                                                                         sorting:
                                                                           field: Bogus
                                                                           direction: Ascending
                                                                         values:
                                                                           - name: A
                                                                         """));

        Assert.Contains("Test.yaml", exception.Message);
    }
}