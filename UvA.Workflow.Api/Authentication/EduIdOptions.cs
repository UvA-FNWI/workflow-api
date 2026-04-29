namespace UvA.Workflow.Api.Authentication;

public class EduIdOptions
{
    public const string Section = "EduId";

    public string Authority { get; set; } = "";
    public string InvitationApiUrl { get; set; } = "";
    public string InvitationApiToken { get; set; } = "";
    public int RoleIdentifier { get; set; } = 7040;
    public int InvitationExpiryDays { get; set; } = 30;
    public int RoleExpiryDays { get; set; } = 365;
    public string[] InternalEmailDomains { get; set; } = [];
}