using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Jobs;

public enum JobStatus
{
    Pending,
    Completed,
    Failed
}

public class Job
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonRepresentation(BsonType.ObjectId)]
    public string InstanceId { get; set; } = null!;

    public string Action { get; set; } = null!;

    public DateTime StartOn { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string? CreatedBy { get; set; }

    public DateTime? ExecutedOn { get; set; }

    public JobStatus Status { get; set; }

    public List<JobStep> Steps { get; set; } = new();

    public JobInput? Input { get; set; }
}

public class JobStep
{
    public string Identifier { get; set; } = null!;
    public string? Message { get; set; }
    public Dictionary<string, object>? Outputs { get; set; }
}