using Kviz.Business.Games;
using Kviz.Entities.Enums;

namespace Kviz.Tests;

public class RulesTests
{
    [Theory]
    [InlineData(0, 1000)]
    [InlineData(10000, 500)]
    [InlineData(5000, 750)]
    [InlineData(99999, 500)]
    public void Points_decrease_with_response_time(int ms, int expected) =>
        Assert.Equal(expected, ScoreCalculator.QuestionPoints(PointsMode.Standard, 10000, ms));

    [Fact]
    public void Double_and_nopoints_modes()
    {
        Assert.Equal(2000, ScoreCalculator.QuestionPoints(PointsMode.Double, 10000, 0));
        Assert.Equal(0, ScoreCalculator.QuestionPoints(PointsMode.NoPoints, 10000, 0));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 100)]
    [InlineData(4, 300)]
    [InlineData(6, 500)]
    [InlineData(20, 500)]
    public void Streak_bonus_is_capped(int streak, int bonus) => Assert.Equal(bonus, ScoreCalculator.StreakBonus(streak));

    [Theory]
    [InlineData("İstanbul", "istanbul")]
    [InlineData("  ISTANBUL  ", "istanbul")]
    [InlineData("Bakı", "baki")]
    [InlineData("a   b", "a b")]
    public void Normalizer_folds_azerbaijani_i(string input, string expected) =>
        Assert.Equal(expected, TextNormalizer.Normalize(input));

    private static GameQuestionData Mc(bool multi, params int[] correct) => new()
    {
        Type = QuestionType.MultipleChoice, MultiSelect = multi,
        Options = Enumerable.Range(0, 4).Select(i => new GameOptionData { Text = "o" + i, IsCorrect = correct.Contains(i) }).ToList()
    };

    [Fact]
    public void Multiple_choice_single_and_multi_evaluation()
    {
        var single = Mc(false, 2);
        Assert.True(AnswerEvaluator.IsCorrect(single, "2"));
        Assert.False(AnswerEvaluator.IsCorrect(single, "1"));

        var multi = Mc(true, 1, 3);
        Assert.True(AnswerEvaluator.IsCorrect(multi, "1|3"));
        Assert.False(AnswerEvaluator.IsCorrect(multi, "1"));
        Assert.False(AnswerEvaluator.IsCorrect(multi, "1|2|3"));
    }

    [Fact]
    public void Normalize_rejects_invalid_choices()
    {
        var q = Mc(false, 0);
        Assert.False(AnswerEvaluator.TryNormalize(q, new AnswerSubmission { Choices = new[] { 9 } }, out _, out _));
        Assert.False(AnswerEvaluator.TryNormalize(q, new AnswerSubmission { Choices = new[] { 0, 1 } }, out _, out _));
        Assert.False(AnswerEvaluator.TryNormalize(q, new AnswerSubmission(), out _, out _));
        Assert.True(AnswerEvaluator.TryNormalize(q, new AnswerSubmission { Choices = new[] { 1 } }, out var data, out _));
        Assert.Equal("1", data);
    }

    [Fact]
    public void Puzzle_requires_exact_order()
    {
        var q = new GameQuestionData
        {
            Type = QuestionType.Puzzle,
            // göstərilmə sırası qarışıqdır, CorrectPosition düzgün mövqedir
            Options = new() { new() { Text = "C", CorrectPosition = 2 }, new() { Text = "A", CorrectPosition = 0 }, new() { Text = "B", CorrectPosition = 1 } }
        };
        Assert.True(AnswerEvaluator.IsCorrect(q, "1|2|0"));
        Assert.False(AnswerEvaluator.IsCorrect(q, "0|1|2"));
        Assert.False(AnswerEvaluator.TryNormalize(q, new AnswerSubmission { Choices = new[] { 0, 1 } }, out _, out _));
    }

    [Fact]
    public void Type_answer_is_lenient_about_case_and_spaces()
    {
        var q = new GameQuestionData
        {
            Type = QuestionType.TypeAnswer,
            Options = new() { new() { Text = "İstanbul", IsCorrect = true }, new() { Text = "Konstantinopol", IsCorrect = true } }
        };
        Assert.True(AnswerEvaluator.IsCorrect(q, "istanbul"));
        Assert.True(AnswerEvaluator.IsCorrect(q, "  ISTANBUL "));
        Assert.True(AnswerEvaluator.IsCorrect(q, "konstantinopol"));
        Assert.False(AnswerEvaluator.IsCorrect(q, "ankara"));
    }

    [Fact]
    public void Slider_uses_percent_tolerance_and_bounds()
    {
        var q = new GameQuestionData
        {
            Type = QuestionType.Slider, SliderMin = 0, SliderMax = 200, SliderStep = 1, SliderCorrect = 100, SliderTolerancePercent = 10
        };
        Assert.True(AnswerEvaluator.IsCorrect(q, "100"));
        Assert.True(AnswerEvaluator.IsCorrect(q, "120"));
        Assert.False(AnswerEvaluator.IsCorrect(q, "121"));
        Assert.False(AnswerEvaluator.TryNormalize(q, new AnswerSubmission { Number = 500 }, out _, out _));
        Assert.False(AnswerEvaluator.TryNormalize(q, new AnswerSubmission { Number = double.NaN }, out _, out _));
    }

    [Fact]
    public void Unscored_types_are_never_correct()
    {
        var q = new GameQuestionData { Type = QuestionType.Poll, Options = new() { new() { Text = "a" } } };
        Assert.False(AnswerEvaluator.IsCorrect(q, "0"));
        Assert.False(q.AwardsPoints);
    }
}
