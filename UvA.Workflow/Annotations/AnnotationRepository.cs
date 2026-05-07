namespace UvA.Workflow.Annotations;

public interface IAnnotationRepository
{
    Task<IEnumerable<Annotation>> GetByArtifact(string artifactId, CancellationToken ct);
    Task<Annotation> Save(Annotation annotation, CancellationToken ct);
}

public class AnnotationRepository(IMongoDatabase database) : IAnnotationRepository
{
    private readonly IMongoCollection<Annotation> collection =
        database.GetCollection<Annotation>("annotations");

    public async Task<IEnumerable<Annotation>> GetByArtifact(string artifactId, CancellationToken ct)
    {
        var filter = Builders<Annotation>.Filter.Eq(a => a.ArtifactId, artifactId);
        return await collection.Find(filter).ToListAsync(ct);
    }

    public async Task<Annotation> Save(Annotation annotation, CancellationToken ct)
    {
        await collection.InsertOneAsync(annotation, cancellationToken: ct);
        return annotation;
    }
}