using System;

namespace GearEnhance;

public static class ProtectionCosts
{
    public static long Fee(int baseCost, int nextLevel, double percent)
    {
        if (baseCost < 0 || baseCost > 100000000 || nextLevel < 1 || nextLevel > 100
            || double.IsNaN(percent) || double.IsInfinity(percent) || percent < 0 || percent > 1000)
            throw new ArgumentException("보호 비용 설정 범위를 확인해 주세요.");
        return (long)Math.Ceiling(baseCost * (percent / 100) * nextLevel * nextLevel);
    }
    public static int Total(int baseCost, long destruction, long downgrade)
    {
        if (baseCost < 0 || destruction < 0 || downgrade < 0 || destruction > 100000000 || downgrade > 100000000
            || baseCost + destruction + downgrade > 100000000)
            throw new InvalidOperationException("보호 비용을 포함한 총액이 1억 루블을 초과합니다. 보호 옵션이나 F12 비용 설정을 낮춰 주세요.");
        return (int)(baseCost + destruction + downgrade);
    }
}
