namespace UvA.Workflow.Infrastructure.Database;

public class MongoOptions
{
    public string Host { get; set; } = null!;
    public string Database { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string Password { get; set;} = null!;
    public int Port { get; set; } = 27017;
    
    public string ConnectionString => $"mongodb://{Username}:{Password}@{Host}:{Port}/{(Host != "localhost" ? "?tls=true" : "")}";
}