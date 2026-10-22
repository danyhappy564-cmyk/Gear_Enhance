using GearEnhance;
using Xunit;

namespace GearEnhanceClientTests;

public sealed class ResultTextTests
{
    [Fact]
    public void Confirmed_outcomes_have_distinct_large_titles_and_a_protected_failure_never_claims_success()
    {
        Assert.Equal("강화 성공!", ResultText.Title(new() { Success = true }, false));
        Assert.Equal("스타 강화 진화 성공!", ResultText.Title(new() { Success = true, IsEvolution = true }, true));
        Assert.Equal("장비 파괴", ResultText.Title(new() { FailureMode = FailureMode.Destroy }, false));
        Assert.Equal("강화 단계 하락", ResultText.Title(new() { FailureMode = FailureMode.Downgrade }, false));
        var receipt = new EnhanceReceipt { DestructionPrevented = true, FailureMode = FailureMode.KeepItem };
        Assert.Equal("강화 실패", ResultText.Title(receipt, false));
        Assert.Contains("파괴 방지 발동", ResultText.Detail(receipt));
        Assert.Equal(FeedbackCue.Failure, GameFeedback.Result(receipt, false));
        receipt.DestructionPrevented = false; receipt.DowngradePrevented = true;
        Assert.Contains("단계 하락 방지 발동", ResultText.Detail(receipt));
    }
}
