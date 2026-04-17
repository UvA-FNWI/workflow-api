using MongoDB.Driver.GridFS;
using Serilog;

namespace UvA.Workflow.Persistence.Mongo;

public class ArtifactService : IArtifactService
{
    private readonly GridFSBucket bucket;

    public ArtifactService(IOptions<MongoOptions> options)
    {
        var mongoClient = new MongoClient(options.Value.ConnectionString);
        var database = mongoClient.GetDatabase(options.Value.Database);
        bucket = new GridFSBucket(database);
    }

    public async Task<ArtifactInfo?> GetArtifactInfo(ObjectId id, CancellationToken ct)
    {
        var filter = Builders<GridFSFileInfo>.Filter.Eq(info => info.Id, id);
        using var cursor = await bucket.FindAsync(filter, cancellationToken: ct);
        var info = await cursor.FirstOrDefaultAsync(ct);
        if (info is null) return null;
        return new ArtifactInfo(info.Id, info.Filename);
    }

    public async Task<ArtifactInfo> SaveArtifact(string artifactName, byte[] contents)
    {
        var id = await bucket.UploadFromBytesAsync(artifactName, contents);
        return new ArtifactInfo(id, artifactName);
    }

    public async Task<ArtifactInfo> SaveArtifact(string artifactName, Stream stream)
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

        return await SaveArtifact(artifactName, contents);
    }

    public async Task<Artifact?> GetArtifact(ObjectId id, CancellationToken ct)
    {
        var info = await GetArtifactInfo(id, ct);
        if (info is null) return null;

        var bytes = await bucket.DownloadAsBytesAsync(id, cancellationToken: ct);
        return new Artifact(info, bytes);
    }

    public async Task DeleteArtifact(ObjectId id, CancellationToken ct = default)
    {
        await bucket.DeleteAsync(id, ct);
    }

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