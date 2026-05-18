using Microsoft.AspNetCore.Http;
using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Persistence;

[BsonIgnoreExtraElements]
public record ArtifactInfo(
    string ArtifactId,
    string Name,
    string ContentType = "application/octet-stream",
    long Length = 0L,
    DateTime CreatedOn = default)
{
    public static ArtifactInfo? FromBson(BsonValue? value)
    {
        if (value == null || value.IsBsonNull)
            return null;

        return new ArtifactInfo(
            value["ArtifactId"].ToString()!,
            value["Name"].AsString,
            value["ContentType"].AsString,
            value["Length"].AsInt64,
            value["CreatedOn"].AsUniversalTime);
    }
}

public record Artifact(ArtifactInfo Info, byte[] Content);

public interface IArtifactService
{
    Task<ArtifactInfo> SaveArtifact(string artifactId, string artifactName, byte[] contents);
    Task<ArtifactInfo> SaveArtifact(string artifactId, string artifactName, Stream stream);
    Task<ArtifactInfo> SaveArtifact(string artifactId, IFormFile file);
    Task<Artifact?> GetArtifact(string artifactId, CancellationToken ct);

    Task DeleteArtifact(string artifactId, CancellationToken ct = default);

    /// Attempts to delete an artifact from the storage system using the specified identifier.
    /// The deletion process internally calls the DeleteArtifact method and logs any exception
    /// that occurs during the operation. If an error is encountered, the method returns false.
    /// <param name="artifactId">The identifier of the artifact to be deleted.</param>
    /// <param name="ct">An optional cancellation token to cancel the operation if required.</param>
    /// <returns>True if the artifact is successfully deleted; otherwise, false.</returns>
    Task<bool> TryDeleteArtifact(string artifactId, CancellationToken ct = default);

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