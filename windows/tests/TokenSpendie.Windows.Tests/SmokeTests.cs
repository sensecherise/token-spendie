using FluentAssertions;
using Xunit;

namespace TokenSpendie.Windows.Tests;

public class SmokeTests
{
    [Fact]
    public void TestRunnerWorks()
    {
        (2 + 2).Should().Be(4);
    }
}
