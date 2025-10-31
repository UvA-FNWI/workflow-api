using UvA.Workflow.Api.Infrastructure;

namespace UvA.Workflow.Api.Versions;

public class VersionsController(ModelServiceResolver modelServiceResolver) : ApiControllerBase
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
            return BadRequest(ex.Message);
        }
        modelServiceResolver.AddOrUpdate(version, parser);
        return Ok();
    }
    
    [HttpGet]
    public ActionResult<IEnumerable<string>> GetVersions() => Ok(modelServiceResolver.Versions);
}