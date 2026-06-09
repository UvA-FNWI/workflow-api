using UvA.Workflow.Assessments;

namespace UvA.Workflow.Tests;

public class AssessmentServiceTests
{
    // Helper to build a page's results concisely
    private static QuestionResult[] Page(params (int weight, double answer)[] fields) =>
        fields.Select(f => new QuestionResult { Weight = f.weight, Answer = f.answer }).ToArray();

    // Builds a SourceResult from named pages, computing Weight and WeightedAverage per page
    private static SourceResult Source(string name, params (string pageName, QuestionResult[] questions)[] pages)
    {
        var pageResults = pages.Select(p =>
        {
            var weight = p.questions.Sum(q => q.Weight);
            var avg = weight == 0
                ? 0
                : Math.Round(
                    p.questions.Sum(q => (decimal)q.Answer * q.Weight) / weight,
                    2, MidpointRounding.AwayFromZero);
            return new PageResult
            {
                PageName = p.pageName,
                Weight = weight,
                WeightedAverage = avg,
                QuestionResults = p.questions.ToList()
            };
        }).ToList();

        return new SourceResult { SourceName = name, PageResults = pageResults };
    }

    [Fact]
    public void CalculateTotalWeightedAverage_SingleFormAllPagesFilled_ReturnsCorrectAverage()
    {
        var form = Source("form",
            ("Report", Page((1, 3), (1, 3), (2, 4), (1, 5), (2, 4), (1, 7))), // avg = 4.25, weight = 8
            ("Process", Page((1, 7), (1, 7), (1, 7), (2, 7), (1, 7), (1, 7), (1, 7))), // avg = 7, weight = 8
            ("Presentation", Page((1, 6), (1, 5), (2, 4))) // avg = 4.75, weight = 4
        );

        var result = AssessmentService.CalculateTotalWeightedAverage([form]);

        // (4.25 * 8 + 7 * 8 + 4.75 * 4) / 20 = 109 / 20 = 5.45
        Assert.Equal(5.45m, result);
    }

    [Fact]
    public void CalculateTotalWeightedAverage_SingleFormNotAllPagesFilled_ReturnsZero()
    {
        var form = Source("form",
            ("Report", Page((1, 6), (1, 7), (2, 7), (1, 7), (2, 7), (1, 7))), // filled
            ("Process", Page((1, 0), (1, 0), (1, 0), (2, 0), (1, 0), (1, 0), (1, 0))), // empty
            ("Presentation", Page((1, 5), (1, 6), (2, 7))) // filled
        );

        var result = AssessmentService.CalculateTotalWeightedAverage([form]);

        Assert.Equal(0m, result);
    }

    [Fact]
    public void CalculateTotalWeightedAverage_TwoForms_BothFullyFilled_CombinesResults()
    {
        var form1 = Source("form1",
            ("Report", Page((1, 3), (1, 3), (2, 4), (1, 5), (2, 4), (1, 7))), // avg = 4.25, weight = 8
            ("Process", Page((1, 7), (1, 7), (1, 7), (2, 7), (1, 7), (1, 7), (1, 7))), // avg = 7, weight = 8
            ("Presentation", Page((1, 6), (1, 5), (2, 4))) // avg = 4.75, weight = 4
        );
        var form2 = Source("form2",
            ("Report", Page((1, 6), (1, 7), (2, 7), (1, 7), (2, 7), (1, 7))), // avg = 6.875, weight = 8
            ("Process", Page((1, 5), (1, 5), (1, 5), (2, 5), (1, 5), (1, 5), (1, 5))), // avg = 5, weight = 8
            ("Presentation", Page((1, 5), (1, 6), (2, 7))) // avg = 6.25, weight = 4
        );

        var result = AssessmentService.CalculateTotalWeightedAverage([form1, form2]);

        // Report avg:       (4.25 + 6.875) / 2 = 5.5625  * weight 8 = 44.5
        // Process avg:      (7 + 5) / 2         = 6        * weight 8 = 48
        // Presentation avg: (4.75 + 6.25) / 2   = 5.5     * weight 4 = 22
        // Total: (44.5 + 48 + 22) / 20 = 5.73
        Assert.Equal(5.73m, result);
    }

    [Fact]
    public void CalculateTotalWeightedAverage_TwoForms_OneFullOnePartial_CombinesBothForms()
    {
        var supervisor = Source("supervisor",
            ("Report", Page((1, 3), (1, 3), (2, 4), (1, 5), (2, 4), (1, 7))), // avg = 4.25, weight = 8
            ("Process", Page((1, 7), (1, 7), (1, 7), (2, 7), (1, 7), (1, 7), (1, 7))), // avg = 7, weight = 8
            ("Presentation", Page((1, 6), (1, 5), (2, 4))) // avg = 4.75, weight = 4
        );
        var secondReader = Source("secondReader",
            ("Report", Page((1, 6), (1, 7), (2, 7), (1, 7), (2, 7), (1, 7))), // avg = 6.875, weight = 8
            ("Process", Page((1, 0), (1, 0), (1, 0), (2, 0), (1, 0), (1, 0), (1, 0))), // empty
            ("Presentation", Page((1, 5), (1, 6), (2, 7))) // avg = 6.25, weight = 4
        );

        var result = AssessmentService.CalculateTotalWeightedAverage([supervisor, secondReader]);

        // Report avg:       (4.25 + 6.875) / 2 = 5.5625  * weight 8 = 44.5
        // Process avg:       7 (only supervisor) = 7      * weight 8 = 56
        // Presentation avg: (4.75 + 6.25) / 2   = 5.5    * weight 4 = 22
        // Total: (44.5 + 56 + 22) / 20 = 6.13
        Assert.Equal(6.13m, result);
    }

    [Fact]
    public void
        CalculateTotalWeightedAverage_TwoFormsNeitherFullyFilled_ButTogetherCoverAllPages_ReturnsCorrectAverage()
    {
        var form1 = Source("form1",
            ("Report", Page((1, 6), (1, 7), (2, 7), (1, 7), (2, 7), (1, 7))), // avg = 6.875, weight = 8
            ("Process", Page((1, 0), (1, 0), (1, 0), (2, 0), (1, 0), (1, 0), (1, 0))), // empty
            ("Presentation", Page((1, 5), (1, 6), (2, 7))) // avg = 6.25, weight = 4
        );
        var form2 = Source("form2",
            ("Report", Page((1, 0), (1, 0), (2, 0), (1, 0), (2, 0), (1, 0))), // empty
            ("Process", Page((1, 7), (1, 7), (1, 7), (2, 7), (1, 7), (1, 7), (1, 7))), // avg = 7, weight = 8
            ("Presentation", Page((1, 0), (1, 0), (2, 0))) // empty
        );

        var result = AssessmentService.CalculateTotalWeightedAverage([form1, form2]);

        // Report avg:       6.875 (only form1)  * weight 8 = 55
        // Process avg:      7     (only form2)  * weight 8 = 56
        // Presentation avg: 6.25  (only form1)  * weight 4 = 25
        // Total: (55 + 56 + 25) / 20 = 6.8
        Assert.Equal(6.80m, result);
    }
}