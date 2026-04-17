using MongoDB.Driver.GridFS;
using Serilog;

namespace UvA.Workflow.Persistence;

public record ArtifactInfo(ObjectId Id, string Name);

public record Artifact(ArtifactInfo Info, byte[] Content);

public interface IArtifactService
{
    Task<ArtifactInfo?> GetArtifactInfo(ObjectId id, CancellationToken ct);
    Task<ArtifactInfo> SaveArtifact(string artifactName, byte[] contents);
    Task<ArtifactInfo> SaveArtifact(string artifactName, Stream stream);
    Task<Artifact?> GetArtifact(ObjectId id, CancellationToken ct);
    Task DeleteArtifact(ObjectId id, CancellationToken ct = default);

    /// Attempts to delete an artifact from the storage system using the specified identifier.
    /// The deletion process internally calls the DeleteArtifact method and logs any exception
    /// that occurs during the operation. If an error is encountered, the method returns false.
    /// <param name="id">The identifier of the artifact to be deleted.</param>
    /// <param name="ct">An optional cancellation token to cancel the operation if required.</param>
    /// <returns>True if the artifact is successfully deleted; otherwise, false.</returns>
    Task<bool> TryDeleteArtifact(ObjectId id, CancellationToken ct = default);
}