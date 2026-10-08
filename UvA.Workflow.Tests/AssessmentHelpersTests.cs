using MongoDB.Bson;
using UvA.Workflow.Assessments;
using UvA.Workflow.Tools;

namespace UvA.Workflow.Tests;

public class AssessmentHelpersTests
{
    private static QuestionResult[] Page(params (int weight, double answer)[] fields) =>
        fields.Select(f => new QuestionResult { Weight = f.weight, Answer = f.answer }).ToArray();

    private static SourceResult Source(string name, params (string pageName, QuestionResult[] questions)[] pages)
    {
        var pageResults = pages.Select(p =>
        {
            var weight = p.questions.Sum(q => q.Weight!.Value);
            var avg = weight == 0
                ? 0
                : Math.Round(
                    p.questions.Sum(q => (decimal)q.Answer * q.Weight!.Value) / weight,
                    2, MidpointRounding.AwayFromZero);
            return new PageResult
            {
                Name = p.pageName,
                Weight = weight,
                WeightedAverage = avg,
                QuestionResults = p.questions.ToList()
            };
        }).ToList();

        var hasEmptyPage = pageResults.Any(p => p.QuestionResults.All(q => q.Answer == 0));
        var totalWeight = pageResults.Sum(p => p.Weight!.Value);
        var sourceAverage = hasEmptyPage || totalWeight == 0
            ? 0
            : Math.Round(
                pageResults.Sum(p => p.WeightedAverage!.Value * p.Weight!.Value) / totalWeight,
                2, MidpointRounding.AwayFromZero);

        return new SourceResult { Name = name, WeightedAverage = sourceAverage, PageResults = pageResults };
    }

    // Builds an AssessmentPart config with the given source names and weights
    private static AssessmentPart PartConfig(params (string name, decimal weight)[] sources)
        => new()
        {
            Name = "TestPart",
            Sources = sources.Select(s => new AssessmentSource { Name = s.name, Weight = s.weight }).ToList()
        };

    // Builds an AssessmentConfiguration with the given part names and weights
    private static AssessmentConfiguration Config(params (string name, decimal weight)[] parts)
        => new()
        {
            Parts = parts.Select(p => new AssessmentPart { Name = p.name, Weight = p.weight }).ToList()
        };

    // Builds an AssessmentPartResult with a pre-computed weighted average
    private static AssessmentPartResult PartResult(string name, decimal weightedAverage)
        => new() { Name = name, Combined = new() { WeightedAverage = weightedAverage } };

    // Builds a SubmissionContext from a simple description of pages and answers.
    // Answers are stored in the instance under propertyName (nested) so that
    // GetProperty(propertyName, fieldName) resolves correctly.
    private static (Form, ObjectContext) CreateContext(
        string formName,
        string propertyName,
        params (string pageName, (string fieldName, decimal weight, double answer)[] questions)[] pages)
    {
        var form = new Form
        {
            Name = formName,
            PropertyName = propertyName,
            Pages = pages.Select(p => new Page
            {
                Name = p.pageName,
                PageElements = p.questions
                    .Select(q =>
                    {
                        var propDef = new PropertyDefinition
                        {
                            Name = q.fieldName,
                            Type = "Double!",
                            Calculation = new CalculationSettings { Weight = q.weight }
                        };
                        return new PageElement { Question = propDef.Name, QuestionDefinition = propDef };
                    })
                    .ToArray()
            }).ToList()
        };

        // Store all answers in a BsonDocument nested under propertyName
        var dict = new Dictionary<Lookup, object?>();

        var doc = new BsonDocument();
        foreach (var (_, questions) in pages)
        foreach (var (fieldName, _, answer) in questions)
            dict.Add(new PropertyLookup($"{formName}.{fieldName}"), answer);

        return (form, new ObjectContext(dict));
    }

    [Fact]
    public void CalculatePartWeightedAverage_SingleSource_FullyFilled_ReturnsSourceAverage()
    {
        var partConfig = PartConfig(("supervisor", 1));
        var sources = new[]
        {
            Source("supervisor",
                ("Report", Page((1, 3), (1, 3), (2, 4), (1, 5), (2, 4), (1, 7))), // avg = 4.25, weight = 8
                ("Process", Page((1, 7), (1, 7), (1, 7), (2, 7), (1, 7), (1, 7), (1, 7))), // avg = 7,    weight = 8
                ("Presentation", Page((1, 6), (1, 5), (2, 4)))) // avg = 4.75, weight = 4
        };
        // source avg: (4.25 * 8 + 7 * 8 + 4.75 * 4) / 20 = 5.45

        var result = AssessmentHelpers.CalculatePartWeightedAverage(partConfig, sources);

        Assert.Equal(5.45m, result);
    }

    [Fact]
    public void CalculatePartWeightedAverage_TwoSources_EqualWeights_BothFilled_AveragesBothSources()
    {
        var partConfig = PartConfig(("supervisor", 1), ("reader", 1));
        var sources = new[]
        {
            Source("supervisor",
                ("Report", Page((1, 3), (1, 3), (2, 4), (1, 5), (2, 4), (1, 7))), // avg = 4.25, weight = 8
                ("Process", Page((1, 7), (1, 7), (1, 7), (2, 7), (1, 7), (1, 7), (1, 7))), // avg = 7,    weight = 8
                ("Presentation", Page((1, 6), (1, 5), (2, 4)))), // avg = 4.75, weight = 4
            Source("reader",
                ("Report", Page((1, 6), (1, 7), (2, 7), (1, 7), (2, 7), (1, 7))), // avg = 6.88, weight = 8
                ("Process", Page((1, 5), (1, 5), (1, 5), (2, 5), (1, 5), (1, 5), (1, 5))), // avg = 5,    weight = 8
                ("Presentation", Page((1, 5), (1, 6), (2, 7)))) // avg = 6.25, weight = 4
        };
        // supervisor avg: 5.45 (weight 1), reader avg: 6.0 (weight 1)
        // part avg: (5.45 * 1 + 6.0 * 1) / 2 = 5.73

        var result = AssessmentHelpers.CalculatePartWeightedAverage(partConfig, sources);

        Assert.Equal(5.73m, Math.Round(result, 2, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void CalculatePartWeightedAverage_TwoSources_DifferentWeights_BothFilled_WeightsSourcesCorrectly()
    {
        var partConfig = PartConfig(("supervisor", 2), ("reader", 1));
        var sources = new[]
        {
            Source("supervisor",
                ("Report", Page((1, 3), (1, 3), (2, 4), (1, 5), (2, 4), (1, 7))), // avg = 4.25, weight = 8
                ("Process", Page((1, 7), (1, 7), (1, 7), (2, 7), (1, 7), (1, 7), (1, 7))), // avg = 7,    weight = 8
                ("Presentation", Page((1, 6), (1, 5), (2, 4)))), // avg = 4.75, weight = 4
            Source("reader",
                ("Report", Page((1, 6), (1, 7), (2, 7), (1, 7), (2, 7), (1, 7))), // avg = 6.88, weight = 8
                ("Process", Page((1, 5), (1, 5), (1, 5), (2, 5), (1, 5), (1, 5), (1, 5))), // avg = 5,    weight = 8
                ("Presentation", Page((1, 5), (1, 6), (2, 7)))) // avg = 6.25, weight = 4
        };
        // supervisor avg: 5.45 (weight 2), reader avg: 6.0 (weight 1)
        // part avg: (5.45 * 2 + 6.0 * 1) / 3 = 16.9 / 3 = 5.63

        var result = AssessmentHelpers.CalculatePartWeightedAverage(partConfig, sources);

        Assert.Equal(5.63m, Math.Round(result, 2, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void CalculatePartWeightedAverage_TwoSources_OneHasEmptyPage_SkipsEmptySource()
    {
        var partConfig = PartConfig(("supervisor", 1), ("reader", 1));
        var sources = new[]
        {
            Source("supervisor",
                ("Report", Page((1, 3), (1, 3), (2, 4), (1, 5), (2, 4), (1, 7))), // avg = 4.25, weight = 8
                ("Process", Page((1, 7), (1, 7), (1, 7), (2, 7), (1, 7), (1, 7), (1, 7))), // avg = 7,    weight = 8
                ("Presentation", Page((1, 6), (1, 5), (2, 4)))), // avg = 4.75, weight = 4
            Source("reader",
                ("Report", Page((1, 6), (1, 7), (2, 7), (1, 7), (2, 7), (1, 7))), // avg = 6.88, weight = 8
                ("Process", Page((1, 0), (1, 0), (1, 0), (2, 0), (1, 0), (1, 0), (1, 0))), // empty → source avg = 0
                ("Presentation", Page((1, 5), (1, 6), (2, 7)))) // avg = 6.25, weight = 4
        };
        // reader has an empty page → reader.WeightedAverage = 0 → skipped entirely
        // part avg: (supervisor avg 5.45 * 1) / 1 = 5.45

        var result = AssessmentHelpers.CalculatePartWeightedAverage(partConfig, sources);

        Assert.Equal(5.45m, result);
    }

    [Fact]
    public void CalculatePartWeightedAverage_AllSourcesHaveEmptyPage_ReturnsZero()
    {
        var partConfig = PartConfig(("supervisor", 1));
        var sources = new[]
        {
            Source("supervisor",
                ("Report", Page((1, 6), (1, 7), (2, 7), (1, 7), (2, 7), (1, 7))), // filled
                ("Process", Page((1, 0), (1, 0), (1, 0), (2, 0), (1, 0), (1, 0), (1, 0))), // empty
                ("Presentation", Page((1, 5), (1, 6), (2, 7)))) // filled
        };

        var result = AssessmentHelpers.CalculatePartWeightedAverage(partConfig, sources);

        Assert.Equal(0m, result);
    }

    [Fact]
    public void CalculatePartWeightedAverage_SourceInConfigHasNoMatchingResult_SkipsMissingSource()
    {
        var partConfig = PartConfig(("supervisor", 1), ("reader", 1));
        var sources = new[]
        {
            Source("supervisor", ("Report", Page((1, 7), (1, 7)))) // avg = 7, weight = 2
            // "reader" has no SourceResult at all in the list
        };
        // reader is null from FirstOrDefault → skipped, only supervisor counted
        // part avg: (7 * 1) / 1 = 7.0

        var result = AssessmentHelpers.CalculatePartWeightedAverage(partConfig, sources);

        Assert.Equal(7.0m, result);
    }

    // ─── CalculateFinalGrade ──────────────────────────────────────────────────

    [Fact]
    public void CalculateFinalGrade_SinglePart_Submitted_ReturnsPartAverage()
    {
        var config = Config(("WrittenReport", 1));
        var parts = new[] { PartResult("WrittenReport", 7.5m) };

        var (resultUnrounded, resultRounded) = AssessmentHelpers.CalculateFinalGrade(config, parts);

        Assert.Equal(7.5m, resultUnrounded);
        Assert.Equal(7.5f, resultRounded);
    }

    [Fact]
    public void CalculateFinalGrade_TwoParts_BothSubmitted_ReturnsWeightedAverage()
    {
        var config = Config(("WrittenReport", 60), ("Presentation", 40));
        var parts = new[]
        {
            PartResult("WrittenReport", 8.0m),
            PartResult("Presentation", 6.0m)
        };
        // (8 * 60 + 6 * 40) / (60 + 40) = 7.2

        var (resultUnrounded, resultRounded) = AssessmentHelpers.CalculateFinalGrade(config, parts);

        Assert.Equal(7.2m, resultUnrounded);
        Assert.Equal(7.2f, resultRounded);
    }

    [Fact]
    public void CalculateFinalGrade_TwoParts_OneNotSubmitted_NormalizesOnlyBySubmittedWeight()
    {
        var config = Config(("WrittenReport", 60), ("Presentation", 40));
        var parts = new[]
        {
            PartResult("WrittenReport", 8.0m),
            PartResult("Presentation", 0m) // not submitted yet
        };
        // Only WrittenReport submitted → (8 * 60) / 60 = 8.0

        var (resultUnrounded, resultRounded) = AssessmentHelpers.CalculateFinalGrade(config, parts);

        Assert.Equal(8.0m, resultUnrounded);
        Assert.Equal(8.0f, resultRounded);
    }

    [Fact]
    public void CalculateFinalGrade_NothingSubmitted_ReturnsZero()
    {
        var config = Config(("WrittenReport", 60), ("Presentation", 40));
        var parts = new[]
        {
            PartResult("WrittenReport", 0m),
            PartResult("Presentation", 0m)
        };

        var (resultUnrounded, resultRounded) = AssessmentHelpers.CalculateFinalGrade(config, parts);

        Assert.Equal(0m, resultUnrounded);
        Assert.Equal(0f, resultRounded);
    }

    [Fact]
    public void CalculateFinalGrade_ThreeParts_AllSubmitted_ReturnsWeightedAverage()
    {
        var config = Config(("Report", 60), ("Presentation", 20), ("Process", 20));
        var parts = new[]
        {
            PartResult("Report", 8.0m),
            PartResult("Presentation", 7.0m),
            PartResult("Process", 6.0m)
        };
        // (8 * 60 + 7 * 20 + 6 * 20) / 100 = 7.4

        var (resultUnrounded, resultRounded) = AssessmentHelpers.CalculateFinalGrade(config, parts);

        Assert.Equal(7.4m, resultUnrounded);
        Assert.Equal(7.4f, resultRounded);
    }

    [Fact]
    public void CalculateFinalGrade_PartInConfigHasNoMatchingResult_SkipsMissingPart()
    {
        var config = Config(("WrittenReport", 60), ("Presentation", 40));
        var parts = new[]
        {
            PartResult("WrittenReport", 8.0m)
            // "Presentation" has no AssessmentPartResult at all in the list
        };
        // Presentation is null from FirstOrDefault → filtered by pair.Result != null
        // submittedWeight = 60 → (8 * 60) / 60 = 8.0

        var (resultUnrounded, resultRounded) = AssessmentHelpers.CalculateFinalGrade(config, parts);

        Assert.Equal(8.0m, resultUnrounded);
        Assert.Equal(8.0f, resultRounded);
    }

    [Fact]
    public void CalculateFinalGrade_EmptyConfig_ReturnsZero()
    {
        var config = new AssessmentConfiguration(); // no parts
        var parts = Array.Empty<AssessmentPartResult>();

        var (resultUnrounded, resultRounded) = AssessmentHelpers.CalculateFinalGrade(config, parts);

        Assert.Equal(0m, resultUnrounded);
        Assert.Equal(0f, resultRounded);
    }

    // ─── CalculateSourceResult ────────────────────────────────────────────────

    private static (Form, ObjectContext) OralTestContext(string source, string? oralGrade)
    {
        var (form, context) = CreateContext(source, source,
            ("Content", [
                ("Setup", 1, 7), ("Execution", 1, 7), ("Organisation", 1, 7),
                ("Language", 1, 7), ("Presentation", 1, 7)
            ]),
            ("Independence", [("Independence", 1, 7)]));
        var oral = new PropertyDefinition
        {
            Name = "OralTest",
            Type = "HalfGrade",
            Calculation = new CalculationSettings { Weight = 1 },
            Values =
            [
                new Choice { Name = "10", Value = 10 },
                new Choice { Name = "8,5", Value = 8.5 }
            ]
        };
        form.Pages[1].PageElements =
            [.. form.Pages[1].PageElements, new PageElement { Question = oral.Name, QuestionDefinition = oral }];
        if (oralGrade != null)
            context.Values[new PropertyLookup($"{source}.OralTest")] = oralGrade;
        return (form, context);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("10", 10.0)]
    [InlineData("8,5", 8.5)]
    public void CalculateSourceResult_OptionalOralTest_CountsOnlyWhenAnswered(string? oralGrade, double? score)
    {
        var (form, context) = OralTestContext("supervisor", oralGrade);

        var result = AssessmentHelpers.CalculateSourceResult(form, context, pageName: null);

        var weight = score == null ? 6m : 7m;
        Assert.Equal((42m + (decimal)(score ?? 0)) / weight, result.WeightedAverage);
        Assert.Equal(weight, result.PageResults.Sum(p => p.Weight));
        var questions = result.PageResults.SelectMany(p => p.QuestionResults).ToList();
        Assert.Equal(score == null ? 6 : 7, questions.Count);
        Assert.All(questions, q => Assert.Equal(1m / weight * 100, q.Percentage));
        if (score != null)
            Assert.Equal(score.Value, questions.Single(q => q.Name == "OralTest").Answer);
    }

    [Fact]
    public void CalculateSourceResult_OptionalOnlyPageLeftBlank_DoesNotLowerAverage()
    {
        var (form, context) = OralTestContext("supervisor", null);
        var oral = form.Pages[1].PageElements[1];
        form.Pages[1].PageElements = [form.Pages[1].PageElements[0]];
        form.Pages.Add(new Page { Name = "Oral", PageElements = [oral] });

        var result = AssessmentHelpers.CalculateSourceResult(form, context, pageName: null);

        Assert.Equal(7m, result.WeightedAverage);
        Assert.Null(result.PageResults.Single(p => p.Name == "Oral").Weight);
        Assert.Null(result.PageResults.Single(p => p.Name == "Oral").WeightedAverage);
    }

    [Theory]
    [InlineData(null, 7.0)]
    [InlineData("", 7.0)]
    [InlineData("10", 8.5)]
    public void CalculateSourceResult_EmbeddedPageAverage_ExcludesClearedOralGrade(string? oralGrade, double expected)
    {
        var (form, _) = OralTestContext("supervisor", oralGrade);
        var document = new BsonDocument
        {
            { "Independence", new BsonDouble(7) },
            { "OralTest", oralGrade == null ? BsonNull.Value : new BsonString(oralGrade) }
        };
        var context = new ObjectContext(new()
        {
            [new PropertyLookup("supervisor")] = ObjectContext.GetValue(document, DataType.Object)
        });

        var result = AssessmentHelpers.CalculateSourceResult(form, context, "Independence");

        Assert.Equal((decimal)expected, Assert.Single(result.PageResults).WeightedAverage);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("10", null)]
    [InlineData(null, "10")]
    [InlineData("10", "8,5")]
    public void CalculateCombined_OptionalOralTest_AveragesOnlyAnsweredGrades(string? firstGrade, string? secondGrade)
    {
        var (firstForm, firstContext) = OralTestContext("supervisor", firstGrade);
        var (secondForm, secondContext) = OralTestContext("reader", secondGrade);
        var sources = new[]
        {
            AssessmentHelpers.CalculateSourceResult(firstForm, firstContext, null),
            AssessmentHelpers.CalculateSourceResult(secondForm, secondContext, null)
        };
        var config = PartConfig(("supervisor", 1), ("reader", 1));

        var result = AssessmentHelpers.CalculateCombined(config, sources);

        Assert.Equal(sources.Average(s => s.WeightedAverage), result.WeightedAverage);
        var oral = result.PageResults.SelectMany(p => p.QuestionResults).SingleOrDefault(q => q.Name == "OralTest");
        if (firstGrade == null && secondGrade == null)
            Assert.Null(oral);
        else
        {
            Assert.NotNull(oral);
            Assert.Equal(firstGrade != null && secondGrade != null ? 9.25 : 10, oral.Answer);
            Assert.Equal(firstGrade != null && secondGrade != null ? 1m / 7 * 100 : (1m / 7 * 100) / 2,
                oral.Percentage);
        }
    }

    [Fact]
    public void CalculateSourceResult_MissingRequiredGrade_RetainsItsWeight()
    {
        var (form, context) = OralTestContext("supervisor", null);
        context.Values.Remove(new PropertyLookup("supervisor.Setup"));

        var result = AssessmentHelpers.CalculateSourceResult(form, context, pageName: null);

        Assert.Equal(35m / 6, result.WeightedAverage);
        Assert.Equal(6m, result.PageResults.Sum(p => p.Weight));
    }

    [Fact]
    public void CalculateSourceResult_OptionalNumericZero_IsAnAnsweredGrade()
    {
        var (form, context) = CreateContext("supervisor", "supervisor",
            ("Report", [("Required", 1, 8), ("Optional", 1, 0)]));
        form.Pages[0].PageElements[1].QuestionDefinition!.Type = "Double";

        var result = AssessmentHelpers.CalculateSourceResult(form, context, pageName: null);

        Assert.Equal(4m, result.WeightedAverage);
        Assert.Equal(2m, result.PageResults[0].Weight);
    }

    [Fact]
    public void CalculateSourceResult_AllPagesFilled_ReturnsCorrectPageResultsAndName()
    {
        var (form, context) = CreateContext("supervisor", "supervisor",
            ("Report", [("Quality", 2, 8.0), ("Depth", 1, 7.0)]), // avg = (8*2+7*1)/3 = 7.67, weight = 3
            ("Process", [("Clarity", 1, 6.0)])); // avg = 6.0, weight = 1

        var result = AssessmentHelpers.CalculateSourceResult(form, context, pageName: null);

        // Name must match Form.Name, not PropertyName
        Assert.Equal("supervisor", result.Name);
        Assert.Equal(2, result.PageResults.Count);

        var report = result.PageResults.Single(p => p.Name == "Report");
        Assert.Equal(3m, report.Weight);
        Assert.Equal(7.67m,
            Math.Round(report.WeightedAverage!.Value, 2, MidpointRounding.AwayFromZero)); // (8*2 + 7*1) / 3

        var process = result.PageResults.Single(p => p.Name == "Process");
        Assert.Equal(1m, process.Weight);
        Assert.Equal(6.0m, process.WeightedAverage);
    }

    [Fact]
    public void CalculateSourceResult_AllPagesFilled_ReturnsCorrectSourceWeightedAverage()
    {
        var (form, context) = CreateContext("supervisor", "supervisor",
            ("Report", [("Quality", 2, 8.0), ("Depth", 1, 7.0)]), // avg = 7.67, weight = 3
            ("Process", [("Clarity", 1, 6.0)])); // avg = 6.0, weight = 1

        var result = AssessmentHelpers.CalculateSourceResult(form, context, pageName: null);

        // (7.67 * 3 + 6.0 * 1) / 4 = 29.01 / 4 = 7.25
        Assert.Equal(7.25m, result.WeightedAverage);
    }

    [Fact]
    public void CalculateSourceResult_WithPageNameFilter_ReturnsOnlyFilteredPage()
    {
        var (form, context) = CreateContext("supervisor", "supervisor",
            ("Report", [("Quality", 2, 8.0), ("Depth", 1, 7.0)]),
            ("Process", [("Clarity", 1, 6.0)]));

        var result = AssessmentHelpers.CalculateSourceResult(form, context, pageName: "Report");

        Assert.Single(result.PageResults);
        Assert.Equal("Report", result.PageResults[0].Name);
    }

    [Fact]
    public void CalculateSourceResult_EmptyAnswers_ReturnsZeroWeightedAverage()
    {
        // All answers are 0.0 → page is considered not filled in
        var (form, context) = CreateContext("supervisor", "supervisor",
            ("Report", [("Quality", 2, 0.0), ("Depth", 1, 0.0)]));

        var result = AssessmentHelpers.CalculateSourceResult(form, context, pageName: null);

        Assert.Equal(0m, result.WeightedAverage);
    }

    [Fact]
    public void CalculateSourceResult_Percentages_AreRelativeToAllPagesNotJustFilteredPage()
    {
        // Total weight across ALL pages = 2 + 1 + 1 = 4
        var (form, context) = CreateContext("supervisor", "supervisor",
            ("Report", [("Quality", 2, 8.0), ("Depth", 1, 7.0)]),
            ("Process", [("Clarity", 1, 6.0)]));

        var result = AssessmentHelpers.CalculateSourceResult(form, context, pageName: null);

        var allQuestions = result.PageResults.SelectMany(p => p.QuestionResults).ToList();
        Assert.Equal(50m, allQuestions.Single(q => q.Name == "Quality").Percentage); // 2/4 * 100
        Assert.Equal(25m, allQuestions.Single(q => q.Name == "Depth").Percentage); // 1/4 * 100
        Assert.Equal(25m, allQuestions.Single(q => q.Name == "Clarity").Percentage); // 1/4 * 100
    }


    public static IEnumerable<object[]> RoundingTestCases =>
    [
        [3.3m, new AssessmentConfiguration { GradingBasis = GradingBasis.Half }, 3.5f],
        [4.444m, new AssessmentConfiguration { GradingBasis = GradingBasis.Half }, 4.5f],
        [5.4999m, new AssessmentConfiguration { GradingBasis = GradingBasis.Half }, 5.0f],
        [5.5m, new AssessmentConfiguration { GradingBasis = GradingBasis.Half }, 5.5f],
        [5.6m, new AssessmentConfiguration { GradingBasis = GradingBasis.Half }, 5.5f],
        [5.8m, new AssessmentConfiguration { GradingBasis = GradingBasis.Half }, 6.0f],
        [5.5m, new AssessmentConfiguration { GradingBasis = GradingBasis.Half, GradeGap = true }, 6.0f],
        [5.4m, new AssessmentConfiguration { GradingBasis = GradingBasis.Half, GradeGap = true }, 5.0f],
        [4.444m, new AssessmentConfiguration { GradingBasis = GradingBasis.Decimal }, 4.4f],
        [5.4999m, new AssessmentConfiguration { GradingBasis = GradingBasis.Decimal }, 5.4f],
        [5.5m, new AssessmentConfiguration { GradingBasis = GradingBasis.Decimal, GradeGap = true }, 6.0f],
        [5.4m, new AssessmentConfiguration { GradingBasis = GradingBasis.Decimal, GradeGap = true }, 5.0f],
        [9.99m, new AssessmentConfiguration { GradingBasis = GradingBasis.Decimal }, 10f],
    ];

    [Theory]
    [MemberData(nameof(RoundingTestCases))]
    public void ApplyRoundingOfFinalGrade(decimal input, AssessmentConfiguration config, float expected)
    {
        Assert.Equal(expected, AssessmentHelpers.ApplyRounding(input, config));
    }
}