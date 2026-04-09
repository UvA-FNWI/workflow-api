namespace UvA.Workflow.Files.S3;

public class S3Config
{
    public const string S3 = nameof(S3);

    public string ServiceUrl { get; set; } = null!;
    public string AuthenticationRegion { get; set; } = null!;
    public string AccessKey { get; set; } = null!;
    public string SecretKey { get; set; } = null!;
    public string SigningKey { get; set; } = null!;
}