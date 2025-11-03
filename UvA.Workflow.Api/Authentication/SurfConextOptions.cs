using Microsoft.AspNetCore.Authentication;

namespace UvA.Workflow.Api.Authentication;

public class SurfConextOptions : AuthenticationSchemeOptions
{
    public static string Section = "SurfConext";
    public string? IntrospectUrl { get; set; }
    public string? Authorization { get; set; }
    public string? ClientId { get; set; }
    
    public bool IsValid() => 
        !string.IsNullOrEmpty(IntrospectUrl) && !string.IsNullOrEmpty(Authorization) && !string.IsNullOrEmpty(ClientId);
}