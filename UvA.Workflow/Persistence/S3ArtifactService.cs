using Microsoft.AspNetCore.Http;
using Minio;
using Minio.DataModel.Args;
using Minio.DataModel;
using Serilog;
using UvA.Workflow.Infrastructure.S3;

namespace UvA.Workflow.Persistence;

public class S3ArtifactService : IArtifactService
{
    private readonly IMinioClient _minioClient;

    private readonly record struct SaveArtifactRequest(
        ObjectId Id,
        Stream Stream,
        string Key,
        string FileName,
        string ContentType,
        long FileSize);

    public S3ArtifactService(IOptions<S3Config> config)
    {
        var s3Config = config.Value;
        var uri = new Uri(s3Config.ServiceUrl);

        _minioClient = new MinioClient()
            .WithEndpoint(uri.Host)
            .WithCredentials(s3Config.AccessKey, s3Config.SecretKey)
            .WithRegion(s3Config.AuthenticationRegion)
            .WithSSL(uri.Scheme == "https")
            .Build();
    }

    public async Task<ArtifactInfo?> GetArtifactInfo(string key, CancellationToken ct)
    {
        // First, get the object metadata
        var statObjectArgs = new StatObjectArgs()
            .WithBucket(Buckets.Milestones)
            .WithObject(key);

        var objectStat = await _minioClient.StatObjectAsync(statObjectArgs, ct);
        objectStat.MetaData.TryGetValue("id", out var objectId);
        objectStat.MetaData.TryGetValue("filename", out var filename);
        objectStat.MetaData.TryGetValue("type", out var contentType);

        return new ArtifactInfo(
            ObjectId.Parse(objectId),
            filename ?? key,
            contentType ?? "application/octet-stream");
    }

    public async Task<ArtifactInfo> SaveArtifact(string instanceId, string propertyName, string artifactName,
        byte[] contents)
        => await SaveArtifact(instanceId, propertyName, artifactName, new MemoryStream(contents));

    public async Task<ArtifactInfo> SaveArtifact(string instanceId, string propertyName, string artifactName,
        Stream stream)
    {
        var id = ObjectId.GenerateNewId();
        return await SaveArtifact(new SaveArtifactRequest
        {
            Id = id,
            Stream = stream,
            Key = IArtifactService.ToObjectKey(instanceId, propertyName, id),
            FileName = artifactName,
            ContentType = "application/pdf",
            FileSize = stream.Length
        });
    }

    public async Task<ArtifactInfo> SaveArtifact(string instanceId, string propertyName, IFormFile formFile)
    {
        var id = ObjectId.GenerateNewId();
        return await SaveArtifact(new SaveArtifactRequest
        {
            Id = id,
            Stream = formFile.OpenReadStream(),
            Key = IArtifactService.ToObjectKey(instanceId, propertyName, id),
            FileName = formFile.FileName,
            ContentType = formFile.ContentType,
            FileSize = formFile.Length
        });
    }

    private async Task<ArtifactInfo> SaveArtifact(SaveArtifactRequest request)
    {
        await UploadFileAsync(
            Buckets.Milestones,
            request.Key,
            request.Stream,
            request.ContentType,
            new Dictionary<string, string>
            {
                ["id"] = request.Id.ToString(),
                ["filename"] = request.FileName,
                ["type"] = request.ContentType
            });

        return new ArtifactInfo(
            request.Id,
            request.FileName,
            request.Key,
            request.ContentType,
            request.FileSize,
            DateTime.UtcNow);
    }

    public async Task<Artifact?> GetArtifact(string key, CancellationToken ct)
    {
        var info = await GetArtifactInfo(key, ct);
        if (info is null) return null;

        return await GetArtifactAsync(key, info, ct);
    }

    private async Task<Artifact?> GetArtifactAsync(string key, ArtifactInfo info, CancellationToken ct)
    {
        var ms = new MemoryStream();
        await _minioClient.GetObjectAsync(
            new GetObjectArgs()
                .WithBucket(Buckets.Milestones)
                .WithObject(key)
                .WithCallbackStream(stream => stream.CopyTo(ms)),
            ct);
        ms.Position = 0;

        return new Artifact(info, await IArtifactService.ToByteArray(ms));
    }

    public async Task DeleteArtifact(string key, CancellationToken ct)
    {
        await _minioClient.RemoveObjectAsync(
            new RemoveObjectArgs()
                .WithBucket(Buckets.Milestones)
                .WithObject(key),
            ct);
    }

    /// Attempts to delete an artifact from the storage system using the specified identifier.
    /// The deletion process internally calls the DeleteArtifact method and logs any exception
    /// that occurs during the operation. If an error is encountered, the method returns false.
    /// <param name="key">The identifier of the instance that is used for the artifact key.</param>
    /// <param name="ct">An optional cancellation token to cancel the operation if required.</param>
    /// <returns>True if the artifact is successfully deleted; otherwise, false.</returns>
    public async Task<bool> TryDeleteArtifact(string key, CancellationToken ct = default)
    {
        try
        {
            await DeleteArtifact(key, ct);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting artifact {ArtifactId}", key);
            return false;
        }
    }

    private async Task UploadFileAsync(
        string bucketName,
        string key,
        Stream fileStream,
        string contentType,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureBucketExistsAsync(bucketName, cancellationToken);

        var putObjectArgs = new PutObjectArgs()
            .WithBucket(bucketName)
            .WithObject(key)
            .WithStreamData(fileStream)
            .WithObjectSize(fileStream.Length)
            .WithContentType(contentType);

        if (metadata != null)
        {
            foreach (var kv in metadata)
            {
                putObjectArgs.WithHeaders(new Dictionary<string, string> { { $"x-amz-meta-{kv.Key}", kv.Value } });
            }
        }

        await _minioClient.PutObjectAsync(putObjectArgs, cancellationToken);
    }

    private async Task EnsureBucketExistsAsync(string bucketName, CancellationToken cancellationToken)
    {
        bool found =
            await _minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucketName), cancellationToken);
        if (!found)
            throw new InvalidOperationException($"Bucket {bucketName} does not exist");
    }
}