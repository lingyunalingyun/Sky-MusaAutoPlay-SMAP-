using System.Reflection;
using SMAP_WPF;
using Xunit;

namespace SMAP.Tests;

public sealed class ReleaseCandidateVersionTests
{
    [Theory]
    [InlineData("3.0.0", "3.0.0-rc.1", 1)]
    [InlineData("3.0.0-rc.2", "3.0.0-rc.1", 1)]
    [InlineData("3.0.0-rc.11", "3.0.0-rc.2", 1)]
    [InlineData("3.0.0-beta.2", "3.0.0-rc.1", -1)]
    [InlineData("2.2", "3.0.0-rc.1", -1)]
    [InlineData("1.1-wpf", "1.1", 0)]
    public void RcCanUpgradeToFinalWithoutDowngradingToOldReleases(string left, string right, int sign)
    {
        var method = typeof(UpdateChecker).GetMethod("Compare", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Equal(sign, Math.Sign((int)method.Invoke(null, [left, right])!));
    }
}
