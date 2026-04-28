using System.Net.Mime;
using Microsoft.AspNetCore.Http;

namespace UvA.Workflow.Persistence;

public record ArtifactInfo(
    ObjectId Id,
    string Name,
    string Key,
    string ContentType = "application/octet-stream",
    long Length = 0L,
    DateTime CreatedOn = default);

public record Artifact(ArtifactInfo Info, byte[] Content);

public interface IArtifactService
{
    Task<ArtifactInfo> SaveArtifact(string instanceId, string propertyName, string artifactName, byte[] contents);
    Task<ArtifactInfo> SaveArtifact(string instanceId, string propertyName, string artifactName, Stream stream);
    Task<ArtifactInfo> SaveArtifact(string instanceId, string propertyName, IFormFile file);
    Task<Artifact?> GetArtifact(string key, CancellationToken ct);

    Task DeleteArtifact(string key, CancellationToken ct = default);

    /// Attempts to delete an artifact from the storage system using the specified identifier.
    /// The deletion process internally calls the DeleteArtifact method and logs any exception
    /// that occurs during the operation. If an error is encountered, the method returns false.
    /// <param name="key">The identifier of the artifact to be deleted.</param>
    /// <param name="ct">An optional cancellation token to cancel the operation if required.</param>
    /// <returns>True if the artifact is successfully deleted; otherwise, false.</returns>
    Task<bool> TryDeleteArtifact(string key, CancellationToken ct = default);

    public static string ToObjectKey(string instanceId, string? propertyName, ObjectId? id = null)
        => $"{instanceId}_{propertyName ?? "global"}_{id ?? ObjectId.GenerateNewId()}";

    public static async Task<byte[]> ToByteArray(Stream stream)
    {
        byte[] contents;
        if (stream is MemoryStream mem)
            contents = mem.ToArray();
        else
        {
            var ms = new MemoryStream((int)stream.Length);
            await stream.CopyToAsync(ms);
            contents = ms.ToArray();
        }

        return contents;
    }
}