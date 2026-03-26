using System.Text.Json.Nodes;
using MongoDB.Bson.Serialization;
using UvA.Workflow.Annotations;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;

namespace UvA.Workflow.Api.Submissions;

[Route("Annotations")]
public class AnnotationsController(IAnnotationRepository annotationRepository) : ApiControllerBase
{
    [HttpGet("{instanceId}/{submissionId}/{questionName}/{artifactId}")]
    public async Task<ActionResult<AnnotationDto[]>> GetAnnotations(
        string instanceId, string submissionId, string questionName, string artifactId,
        CancellationToken ct)
    {
        var annotations = await annotationRepository.GetByArtifact(artifactId, ct);
        return Ok(annotations.Select(ToDto).ToArray());
    }

    [HttpPost("{instanceId}/{submissionId}/{questionName}/{artifactId}")]
    public async Task<ActionResult<AnnotationDto>> CreateAnnotation(
        string instanceId, string submissionId, string questionName, string artifactId,
        [FromBody] CreateAnnotationDto dto,
        CancellationToken ct)
    {
        var positionBson = dto.Position is not null
            ? BsonSerializer.Deserialize<BsonDocument>(dto.Position.ToJsonString())
            : new BsonDocument();

        var annotation = new Annotation
        {
            InstanceId = instanceId,
            SubmissionId = submissionId,
            ArtifactId = artifactId,
            HighlightedText = dto.HighlightedText,
            Comment = dto.Comment,
            Position = positionBson,
        };

        var saved = await annotationRepository.Save(annotation, ct);
        return Ok(ToDto(saved));
    }

    private static AnnotationDto ToDto(Annotation a) => new(
        Id: a.Id.ToString(),
        HighlightedText: a.HighlightedText,
        Comment: a.Comment,
        Position: a.Position is not null
            ? JsonNode.Parse(a.Position.ToJson())
            : null
    );
}