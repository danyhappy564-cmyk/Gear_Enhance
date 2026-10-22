using GearEnhance;
using Xunit;

namespace GearEnhanceClientTests;

public sealed class DeveloperOptionsTests
{
    [Fact]
    public void Balance_entries_are_hidden_by_default_and_only_visibility_changes_when_developer_mode_is_toggled()
    {
        var policy = new DeveloperOptions();
        var chance = policy.Describe("2. 확률", "항상 성공 (100%)");
        var cost = policy.Describe("3. 비용", "기준 가격 대비 비용 (%)");
        Assert.False(chance.Browsable); Assert.False(cost.Browsable);
        Assert.Equal("99. 개발자 옵션", chance.Category);
        policy.SetVisible(true);
        Assert.True(chance.Browsable); Assert.True(cost.Browsable);
        Assert.True(policy.Describe("10. 스타 강화 진화", "진화 성공률 (%)").Browsable);
        policy.SetVisible(false);
        Assert.False(chance.Browsable); Assert.False(cost.Browsable);
        Assert.Contains("항상 성공", chance.DispName);
    }
}
