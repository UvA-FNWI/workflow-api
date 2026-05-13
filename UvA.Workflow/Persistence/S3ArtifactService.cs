using Microsoft.AspNetCore.Http;
using Minio;
using Minio.DataModel.Args;
using Serilog;
using UvA.Workflow.Infrastructure.S3;

namespace UvA.Workflow.Persistence;

public class S3ArtifactService : IArtifactService
{
    private readonly IMinioClient _minioClient;

    private readonly record struct SaveArtifactRequest(
        string ArtifactId,
        Stream Stream,
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

    public async Task<ArtifactInfo?> GetArtifactInfo(string artifactId, CancellationToken ct)
    {
        // First, get the object metadata
        var statObjectArgs = new StatObjectArgs()
            .WithBucket(Buckets.Milestones)
            .WithObject(artifactId);

        var objectStat = await _minioClient.StatObjectAsync(statObjectArgs, ct);
        objectStat.MetaData.TryGetValue("filename", out var filename);
        objectStat.MetaData.TryGetValue("type", out var contentType);

        return new ArtifactInfo(
            artifactId,
            filename ?? artifactId,
            contentType ?? "application/octet-stream");
    }

    public async Task<ArtifactInfo> SaveArtifact(string artifactId, string artifactName,
        byte[] contents)
        => await SaveArtifact(artifactId, artifactName, new MemoryStream(contents));

    public async Task<ArtifactInfo> SaveArtifact(string artifactId, string artifactName,
        Stream stream)
    {
        return await SaveArtifact(new SaveArtifactRequest
        {
            ArtifactId = artifactId,
            Stream = stream,
            FileName = artifactName,
            ContentType = "application/pdf",
            FileSize = stream.Length
        });
    }

    public async Task<ArtifactInfo> SaveArtifact(string artifactId, IFormFile formFile)
    {
        return await SaveArtifact(new SaveArtifactRequest
        {
            ArtifactId = artifactId,
            Stream = formFile.OpenReadStream(),
            FileName = formFile.FileName,
            ContentType = formFile.ContentType,
            FileSize = formFile.Length
        });
    }

    private async Task<ArtifactInfo> SaveArtifact(SaveArtifactRequest request)
    {
        await UploadFileAsync(
            Buckets.Milestones,
            request.ArtifactId,
            request.Stream,
            request.ContentType,
            new Dictionary<string, string>
            {
                ["filename"] = request.FileName,
                ["type"] = request.ContentType
            });

        return new ArtifactInfo(
            request.ArtifactId,
            request.FileName,
            request.ContentType,
            request.FileSize,
            DateTime.UtcNow);
    }

    public async Task<Artifact?> GetArtifact(string artifactId, CancellationToken ct)
    {
        var info = await GetArtifactInfo(artifactId, ct);
        if (info is null) return null;

        return await GetArtifactAsync(artifactId, info, ct);
    }

    private async Task<Artifact?> GetArtifactAsync(string artifactId, ArtifactInfo info, CancellationToken ct)
    {
        var ms = new MemoryStream();
        await _minioClient.GetObjectAsync(
            new GetObjectArgs()
                .WithBucket(Buckets.Milestones)
                .WithObject(artifactId)
                .WithCallbackStream(stream => stream.CopyTo(ms)),
            ct);
        ms.Position = 0;

        return new Artifact(info, await IArtifactService.ToByteArray(ms));
    }

    public async Task DeleteArtifact(string artifactId, CancellationToken ct)
    {
        await _minioClient.RemoveObjectAsync(
            new RemoveObjectArgs()
                .WithBucket(Buckets.Milestones)
                .WithObject(artifactId),
            ct);
    }

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

    private async Task UploadFileAsync(
        string bucketName,
        string artifactId,
        Stream fileStream,
        string contentType,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureBucketExistsAsync(bucketName, cancellationToken);

        var putObjectArgs = new PutObjectArgs()
            .WithBucket(bucketName)
            .WithObject(artifactId)
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

    public static string ToArtifactId(string instanceId, string? propertyName, ObjectId? id = null)
        => $"{instanceId}_{propertyName ?? "global"}_{id ?? ObjectId.GenerateNewId()}";
}