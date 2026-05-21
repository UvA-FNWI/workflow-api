using Microsoft.AspNetCore.Http;
using MongoDB.Driver.GridFS;
using Serilog;

namespace UvA.Workflow.Persistence.Mongo;

public class GridFsArtifactService : IArtifactService
{
    private readonly GridFSBucket _bucket;

    public GridFsArtifactService(IOptions<MongoOptions> options)
    {
        var mongoClient = new MongoClient(options.Value.ConnectionString);
        var database = mongoClient.GetDatabase(options.Value.Database);
        _bucket = new GridFSBucket(database);
    }

    public async Task<ArtifactInfo?> GetArtifactInfo(string artifactId, CancellationToken ct)
    {
        var filter = Builders<GridFSFileInfo>.Filter.Eq(info => info.Id, ObjectId.Parse(artifactId));
        using var cursor = await _bucket.FindAsync(filter, cancellationToken: ct);
        var info = await cursor.FirstOrDefaultAsync(ct);
        if (info is null)
            return null;

        return new ArtifactInfo(info.Id.ToString(), info.Filename);
    }

    public async Task<ArtifactInfo> SaveArtifact(string artifactName, byte[] contents)
    {
        var id = await _bucket.UploadFromBytesAsync(artifactName, contents);
        return new ArtifactInfo(id.ToString(), artifactName);
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

    public async Task<ArtifactInfo> SaveArtifact(string artifactId, string artifactName, byte[] contents)
        => await SaveArtifact(artifactName, contents);

    public async Task<ArtifactInfo> SaveArtifact(string artifactId, string artifactName, Stream stream)
        => await SaveArtifact(artifactName, stream);

    public async Task<ArtifactInfo> SaveArtifact(string artifactId, IFormFile formFile)
        => await SaveArtifact(artifactId, formFile.OpenReadStream());

    public async Task<Artifact?> GetArtifact(string artifactId, CancellationToken ct)
    {
        var info = await GetArtifactInfo(artifactId, ct);
        if (info is null) return null;

        var bytes = await _bucket.DownloadAsBytesAsync(artifactId, cancellationToken: ct);
        return new Artifact(info, bytes);
    }

    public async Task DeleteArtifact(string artifactId, CancellationToken ct = default)
        => await _bucket.DeleteAsync(artifactId, ct);

    public async Task<bool> TryDeleteArtifact(string artifactId, CancellationToken ct = default)
    {
        try
        {
            await DeleteArtifact(artifactId, ct);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting artifact {ArtifactId}", artifactId);
            return false;
        }
    }
}