using Microsoft.AspNetCore.Http;
using Minio;
using Minio.DataModel.Args;
using Serilog;
using UvA.Workflow.Infrastructure.S3;

namespace UvA.Workflow.Persistence;

public class S3ArtifactService : IArtifactService
{
    private readonly IMinioClient _minioClient;

    public S3ArtifactService(IOptions<S3Config> config)
    {
        var config1 = config.Value;

        var uri = new Uri(config1.ServiceUrl);
        var useSSL = uri.Scheme == "https";

        _minioClient = new MinioClient()
            .WithEndpoint(uri.Host)
            .WithCredentials(config1.AccessKey, config1.SecretKey)
            .WithRegion(config1.AuthenticationRegion)
            .WithSSL(useSSL)
            .Build();
    }

    public async Task<ArtifactInfo?> GetArtifactInfo(ObjectId id, CancellationToken ct)
    {
        // First, get the object metadata
        var statObjectArgs = new StatObjectArgs()
            .WithBucket(Buckets.Resumes)
            .WithObject(id.ToString());

        var objectStat = await _minioClient.StatObjectAsync(statObjectArgs, ct);
        var filename = objectStat.MetaData.TryGetValue("filename", out var value)
            ? value
            : id.ToString();

        return new ArtifactInfo(id, filename);
    }

    public async Task<ArtifactInfo> SaveArtifact(string artifactName, byte[] contents)
        => await SaveArtifact(artifactName, new MemoryStream(contents));

    public async Task<ArtifactInfo> SaveArtifact(string artifactName, Stream stream)
    {
        const string contentType = "application/pdf";
        var id = ObjectId.GenerateNewId();

        await UploadFileAsync(
            Buckets.Resumes,
            id.ToString(),
            stream,
            contentType);

        return new ArtifactInfo(id,
            artifactName,
            contentType,
            stream.Length,
            DateTime.Now);
    }

    public async Task<ArtifactInfo> SaveArtifact(IFormFile formFile)
    {
        var id = ObjectId.GenerateNewId();
        await UploadFileAsync(
            Buckets.Resumes,
            id.ToString(),
            formFile.OpenReadStream(),
            formFile.ContentType);

        return new ArtifactInfo(id,
            formFile.FileName,
            formFile.ContentType,
            formFile.Length,
            DateTime.Now);
    }


    public async Task<Artifact?> GetArtifact(ObjectId id, CancellationToken ct)
    {
        var info = await GetArtifactInfo(id, ct);
        if (info is null) return null;


        // Then, get the object content
        var ms = new MemoryStream();
        await _minioClient.GetObjectAsync(
            new GetObjectArgs()
                .WithBucket(Buckets.Resumes)
                .WithObject(id.ToString())
                .WithCallbackStream(stream => stream.CopyTo(ms)),
            ct);
        ms.Position = 0;

        return new Artifact(info, await IArtifactService.ToByteArray(ms));
    }

    public async Task DeleteArtifact(ObjectId id, CancellationToken ct = default)
    {
        await _minioClient.RemoveObjectAsync(
            new RemoveObjectArgs()
                .WithBucket(Buckets.Resumes)
                .WithObject(id.ToString()),
            ct);
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


    private Dictionary<string, string> ExtractMetadata(IDictionary<string, string> metadata)
    {
        var userMetadata = new Dictionary<string, string>();
        foreach (var kvp in metadata)
        {
            if (kvp.Key.StartsWith("x-amz-meta-", StringComparison.OrdinalIgnoreCase))
            {
                var userKey = kvp.Key.Substring("x-amz-meta-".Length);
                userMetadata[userKey] = kvp.Value;
            }
            else
            {
                // Include other relevant metadata
                userMetadata[kvp.Key] = kvp.Value;
            }
        }

        return userMetadata;
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