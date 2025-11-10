using UvA.Workflow.Api.Infrastructure;

namespace UvA.Workflow.Api.Versions;

public class VersionsController(ModelServiceResolver modelServiceResolver, ILogger<VersionsController> logger) : ApiControllerBase
{
    [HttpPost("{version}")]
    public ActionResult CreateVersion(string version, [FromBody] Dictionary<string, string> files)
    {
        ModelParser parser;
        try
        {
            parser = new ModelParser(new DictionaryProvider(files));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing model for version {Version}", version);
            return BadRequest(ex.Message);
        }
        modelServiceResolver.AddOrUpdate(version, parser);
        return Ok();
    }
    
    [HttpGet]
    public ActionResult<IEnumerable<string>> GetVersions() => Ok(modelServiceResolver.Versions);
}