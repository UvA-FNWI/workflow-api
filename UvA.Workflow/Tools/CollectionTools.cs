namespace UvA.Workflow.Tools;

public static class CollectionTools
{
    public static IEnumerable<T> Merge<T>(params IEnumerable<T>?[] collections)
        => collections.SelectMany(t => t ?? []);
}