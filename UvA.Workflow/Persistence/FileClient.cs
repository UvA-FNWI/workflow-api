using MongoDB.Driver.GridFS;
using Serilog;
using UvA.Workflow.Infrastructure.Database;

namespace UvA.Workflow.Infrastructure.Persistence;

public class FileClient
{
    private readonly GridFSBucket _bucket;

    public FileClient(IOptions<MongoOptions> options)
    {
        var mongoClient = new MongoClient(options.Value.ConnectionString);
        var database = mongoClient.GetDatabase(options.Value.Database);
        _bucket = new GridFSBucket(database);
    }

    public Task<ObjectId> StoreFile(string fileName, byte[] contents)
        => _bucket.UploadFromBytesAsync(fileName, contents);

    public Task<byte[]> GetFile(string fileId)
        => _bucket.DownloadAsBytesAsync(new ObjectId(fileId));
    
    public Task DeleteFile(string fileId)
        => _bucket.DeleteAsync(new ObjectId(fileId));

    /// Tries to delete a file with the specified file identifier from the GridFS storage.
    /// If the file is not found, or an error occurs during the deletion process, the method logs the error and returns false.
    /// <param name="fileId">The identifier of the file to be deleted.</param>
    /// <returns>True if the file was successfully deleted; otherwise, false.</returns>
    public async Task<bool> TryDeleteFile(string fileId)
    {
        try
        {
            await DeleteFile(fileId);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting file {FileId}", fileId);
            return false;
        }
    }
}