using MongoDB.Driver.GridFS;
using UvA.Workflow.Infrastructure.Database;

namespace UvA.Workflow.Infrastructure.Persistence;

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
        => _bucket.DownloadAsBytesAsync(new ObjectId(fileId));
    
    public Task DeleteFile(string fileId)
        => _bucket.DeleteAsync(new ObjectId(fileId));
}