using Microsoft.AspNetCore.Authentication;

namespace UvA.Workflow.Api.Authentication;

public class SurfConextOptions : AuthenticationSchemeOptions
{
    public static string Section = "SurfConext";
    public string? BaseUrl { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}