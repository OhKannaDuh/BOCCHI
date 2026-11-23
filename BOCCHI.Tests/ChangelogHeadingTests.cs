using BOCCHI.Services.Changelog;
using Xunit;

namespace BOCCHI.Tests;

public class ChangelogHeadingTests
{
    [Theory]
    [InlineData("# 4.4.0.0", "# 4.3.0.0")]
    [InlineData("# 4.4.0.0", "## 4.3.0.0")]
    [InlineData("## 4.4.0.0", "# 4.3.0.0")]
    [InlineData("## 4.4.0.0", "## 4.3.0.0")]
    [InlineData("# v4.4.0.0", "# 4.3.0.0")]
    [InlineData("# v4.4.0.0", "## 4.3.0.0")]
    public void VersionHeadingsKeepSubheadingsAndStopAtNextRelease(string heading, string nextHeading)
    {
        string markdown = $"# 4.5.0.0\n- Later\n{heading}\n- Fixed\n### New\n- Added\n{nextHeading}\n- Earlier";

        string? section = ChangelogText.ExtractVersionSection(markdown, "4.4.0.0");

        Assert.Equal($"- Fixed{Environment.NewLine}### New{Environment.NewLine}- Added{Environment.NewLine}", section);
    }
}
