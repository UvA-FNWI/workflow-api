using MongoDB.Driver.GridFS;
using Serilog;
using UvA.Workflow.Infrastructure.Database;

namespace UvA.Workflow.Persistence;

public class GridFsArtifactService : IArtifactService
{
    private readonly GridFSBucket _bucket;

    public GridFsArtifactService(IOptions<MongoOptions> options)
    {
        var mongoClient = new MongoClient(options.Value.ConnectionString);
        var database = mongoClient.GetDatabase(options.Value.Database);
        _bucket = new GridFSBucket(database);
    }

    public async Task<ArtifactInfo?> GetArtifactInfo(ObjectId id, CancellationToken ct)
    {
        var filter = Builders<GridFSFileInfo>.Filter.Eq(info => info.Id, id);
        using var cursor = await _bucket.FindAsync(filter, cancellationToken: ct);
        var info = await cursor.FirstOrDefaultAsync(ct);
        if (info is null) return null;
        return new ArtifactInfo(info.Id, info.Filename);
    }

    public async Task<ArtifactInfo> SaveArtifact(string artifactName, byte[] contents)
    {
        var id = await _bucket.UploadFromBytesAsync(artifactName, contents);
        return new ArtifactInfo(id, artifactName);
    }

    public async Task<ArtifactInfo> SaveArtifact(string artifactName, Stream stream)
        => await SaveArtifact(artifactName, await IArtifactService.ToByteArray(stream));

    public async Task<Artifact?> GetArtifact(ObjectId id, CancellationToken ct)
    {
        var info = await GetArtifactInfo(id, ct);
        if (info is null) return null;

        var bytes = await _bucket.DownloadAsBytesAsync(id, cancellationToken: ct);
        return new Artifact(info, bytes);
    }

    public async Task DeleteArtifact(ObjectId id, CancellationToken ct = default)
    {
        await _bucket.DeleteAsync(id, ct);
    }

    /// Attempts to delete an artifact from the storage system using the specified identifier.
    /// The deletion process internally calls the DeleteArtifact method and logs any exception
    /// that occurs during the operation. If an error is encountered, the method returns false.
    /// <param name="id">The identifier of the artifact to be deleted.</param>
    /// <param name="ct">An optional cancellation token to cancel the operation if required.</param>
    /// <returns>True if the artifact is successfully deleted; otherwise, false.</returns>
    public async Task<bool> TryDeleteArtifact(ObjectId id, CancellationToken ct = default)
    {
        try
        {
            await DeleteArtifact(id, ct);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting artifact {ArtifactId}", id);
            return false;
        }
    }
}