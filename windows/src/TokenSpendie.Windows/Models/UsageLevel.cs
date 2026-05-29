namespace TokenSpendie.Windows.Models;

public enum UsageLevel { Calm, Warn, Hot }

public static class UsageLevelExtensions
{
    public static UsageLevel ForPercent(double percent) =>
        percent >= 90 ? UsageLevel.Hot
        : percent >= 70 ? UsageLevel.Warn
        : UsageLevel.Calm;
}
