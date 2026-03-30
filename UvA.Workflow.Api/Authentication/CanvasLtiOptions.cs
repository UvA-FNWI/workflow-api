using System.ComponentModel.DataAnnotations;

namespace UvA.Workflow.Api.Authentication;

public class CanvasLtiOptions
{
    public const string Section = "CanvasLti";

    [Required] public string Key { get; set; } = "";

    [Required] public string ClientId { get; set; } = "";

    [Required] public string AuthenticateUrl { get; set; } = "";

    [Required] public string JwksUrl { get; set; } = "";

    [Required] public string TeacherTarget { get; set; } = "";

    public int TokenLifetime { get; set; } = 240;
    public string? TestRedirectUrl { get; set; }
    public string FallbackTarget { get; set; } = "/";
}