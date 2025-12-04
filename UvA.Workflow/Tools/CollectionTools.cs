using System.Diagnostics.CodeAnalysis;

namespace UvA.Workflow.Tools;

public static class CollectionTools
{
    /// <summary>
    /// Combines multiple collections into a single enumerable sequence.
    /// Null collections are ignored during the merge process.
    /// </summary>
    /// <typeparam name="T">The type of the elements in the collections.</typeparam>
    /// <param name="collections">An array of collections to merge. Null values are permitted and will be ignored.</param>
    /// <returns>An <see cref="IEnumerable{T}"/> that contains all elements from the non-null collections.</returns>
    public static IEnumerable<T> Merge<T>(params IEnumerable<T>?[] collections)
        => collections.SelectMany(t => t ?? []);


    /// <summary>
    /// Attempts to find the first element in the collection that matches the specified predicate.
    /// </summary>
    /// <typeparam name="T">The type of the elements in the collection.</typeparam>
    /// <param name="collection">The collection to search through.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <param name="result">
    /// When this method returns, contains the first element that meets the condition,
    /// or the default value of type <typeparamref name="T"/> if no such element is found.
    /// </param>
    /// <returns>
    /// <c>true</c> if an element that meets the condition is found;
    /// otherwise, <c>false</c>.
    /// </returns>
    public static bool TryFirst<T>(this IEnumerable<T> collection,
        Func<T, bool> predicate,
        [NotNullWhen(true)] out T? result)
    {
        result = collection.FirstOrDefault(predicate);
        return result != null;
    }


    /// <summary>
    /// Attempts to retrieve an element from the collection that matches the specified name.
    /// </summary>
    /// <typeparam name="T">The type of the elements in the collection, which must implement <see cref="INamed"/>.</typeparam>
    /// <param name="collection">The list of elements to search through.</param>
    /// <param name="name">The name to match against the <see cref="INamed.Name"/> property of the elements.</param>
    /// <param name="result">
    /// When this method returns, contains the element with a matching name,
    /// or <c>null</c> if no such element is found.
    /// </param>
    /// <returns>
    /// <c>true</c> if an element with a matching name is found;
    /// otherwise, <c>false</c>.
    /// </returns>
    public static bool TryGetValue<T>(this IEnumerable<T> collection,
        string name,
        [NotNullWhen(true)] out T? result) where T : class, INamed
        => collection.TryFirst(c => c.Name == name, out result);

    public static T Get<T>(this IEnumerable<T> collection, string name) where T : class, INamed
        => collection.First(c => c.Name == name);

    public static T? GetOrDefault<T>(this IEnumerable<T> collection, string name) where T : class, INamed
        => collection.FirstOrDefault(c => c.Name == name);

    /// <summary>
    /// Determines whether the collection contains an element with the specified name.
    /// </summary>
    /// <typeparam name="T">The type of the elements in the collection, which must implement the <see cref="INamed"/> interface.</typeparam>
    /// <param name="collection">The collection to search for the element.</param>
    /// <param name="name">The name of the element to search for.</param>
    /// <returns>
    /// <c>true</c> if an element with the specified name exists in the collection; otherwise, <c>false</c>.
    /// </returns>
    public static bool Contains<T>(this List<T> collection, string name) where T : class, INamed
        => collection.Any(c => c.Name == name);
}