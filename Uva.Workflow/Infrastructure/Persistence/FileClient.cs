using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using UvA.Workflow.Api.Infrastructure.Database;

namespace UvA.Workflow.Api.Infrastructure.Persistence;

public class FileClient
{
    private readonly GridFSBucket _bucket;
    
    public FileClient(IOptions<MongoOptions> options)
    {
        var mongoClient = new MongoClient(options.Value.ConnectionString);
        var database = mongoClient.GetDatabase(options.Value.DatabaseName);
        _bucket = new GridFSBucket(database);
    }

    public Task<ObjectId> StoreFile(string fileName, byte[] contents)
        => _bucket.UploadFromBytesAsync(fileName, contents);

    public Task<byte[]> GetFile(string fileId)
        => _bucket.DownloadAsBytesAsync( new ObjectId(fileId));
}