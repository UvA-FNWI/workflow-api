using UvA.Workflow.Users.DataNose;

namespace UvA.Workflow.Tests.Users;

public class DataNoseLoginMethodClassifierTests
{
    private readonly DataNoseLoginMethodClassifier _classifier = new();

    [Theory]
    [InlineData("1234567", "Uva")]
    [InlineData("emp12345", "Uva")]
    [InlineData("6f1e7c3a-1b2d-4e5f-8a9b-0c1d2e3f4a5b", "AmsterdamUmc")]
    public void Classify_RecognisesInternalUidShapes(string userName, string expected)
        => Assert.Equal(expected, _classifier.ClassifyAsync(userName).GetAwaiter().GetResult());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("person@uva.nl")]
    [InlineData("user_gmail.com#EXT#@tenant.onmicrosoft.com")]
    public void Classify_ReturnsNull_ForExternalOrUnknownNames(string? userName)
        => Assert.Null(_classifier.ClassifyAsync(userName).GetAwaiter().GetResult());
}