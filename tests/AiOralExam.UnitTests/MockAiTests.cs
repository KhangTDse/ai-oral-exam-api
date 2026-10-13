using AiOralExam.Modules.AccessConfig.Contracts;
using AiOralExam.Modules.Interview.Application.Ai;
using AiOralExam.Modules.Interview.Application.Ai.Mock;

namespace AiOralExam.UnitTests;

public class MockAiTests
{
    private static readonly RubricCriterionInfo[] Criteria =
    [
        new(Guid.NewGuid(), 1, "Explains the idea", 6),
        new(Guid.NewGuid(), 2, "Gives an example", 4),
    ];

    private static RubricScoringRequest Request(string? answer) =>
        new("Q", Criteria, [new DialogueTurn("Q", answer, false)], "Vietnamese");

    [Fact]
    public async Task Scorer_is_deterministic_and_scores_every_criterion()
    {
        var scorer = new MockRubricScorer();
        var answer = string.Join(' ', Enumerable.Repeat("word", 30));

        var a = await scorer.ScoreAsync(Request(answer));
        var b = await scorer.ScoreAsync(Request(answer));

        Assert.Equal(a.Scores, b.Scores);
        Assert.Equal(a.Feedback, b.Feedback);
        Assert.Equal(Criteria.Select(c => c.Id), a.Scores.Select(s => s.CriterionId));
        Assert.Equal(5m, a.Scores.Sum(s => s.Points)); // half of 6 + half of 4
    }

    [Fact]
    public async Task Scorer_gives_zero_for_no_answer()
    {
        var result = await new MockRubricScorer().ScoreAsync(Request(null));
        Assert.All(result.Scores, s => Assert.Equal(0m, s.Points));
    }

    [Fact]
    public async Task Analyzer_asks_follow_up_only_when_enabled_and_quota_left()
    {
        var req = new AnswerAnalysisRequest("Q", Criteria, [new DialogueTurn("Q", "too short", false)], 0, 2, "Vietnamese");

        Assert.True((await new MockAnswerAnalyzer(true).AnalyzeAsync(req)).NeedsFollowUp);
        Assert.False((await new MockAnswerAnalyzer(false).AnalyzeAsync(req)).NeedsFollowUp);
        Assert.False((await new MockAnswerAnalyzer(true).AnalyzeAsync(req with { FollowUpsUsed = 2 })).NeedsFollowUp);
    }
}
