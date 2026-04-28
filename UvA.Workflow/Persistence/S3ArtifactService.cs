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
        objectStat.MetaData.TryGetValue("filename", out var filename);
        objectStat.MetaData.TryGetValue("type", out var contentType);

        return new ArtifactInfo(key,
            filename ?? new ObjectId().ToString(),
            contentType ?? "application/octet-stream");
    }

    public async Task<ArtifactInfo> SaveArtifact(string key, string artifactName,
        byte[] contents)
        => await SaveArtifact(key, artifactName, new MemoryStream(contents));

    public async Task<ArtifactInfo> SaveArtifact(string key, string artifactName,
        Stream stream)
    {
        const string contentType = "application/pdf";
        await UploadFileAsync(
            Buckets.Milestones,
            key,
            stream,
            contentType,
            new Dictionary<string, string>
            {
                ["filename"] = artifactName,
                ["type"] = contentType
            });

        return new ArtifactInfo(key,
            artifactName,
            contentType,
            stream.Length,
            DateTime.UtcNow);
    }

    public async Task<ArtifactInfo> SaveArtifact(string key, IFormFile formFile)
    {
        await UploadFileAsync(
            Buckets.Milestones,
            key,
            formFile.OpenReadStream(),
            formFile.ContentType,
            new Dictionary<string, string>
            {
                ["filename"] = formFile.FileName,
                ["type"] = formFile.ContentType
            });

        return new ArtifactInfo(key,
            formFile.FileName,
            formFile.ContentType,
            formFile.Length,
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