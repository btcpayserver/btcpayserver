using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace BTCPayServer.Tests;

public class StaticAssetTests
{
    [Fact]
    [Trait("Unit", "Unit")]
    public void RtlStylesheetReferencesExist()
    {
        var solutionDirectory = TestUtils.TryGetSolutionDirectoryInfo();
        var serverDirectory = Path.Combine(solutionDirectory.FullName, "BTCPayServer");
        var viewsDirectory = Path.Combine(serverDirectory, "Views");
        var webRootDirectory = Path.Combine(serverDirectory, "wwwroot");
        var stylesheetPattern = new Regex("href=\"~/(?<path>[^\"]+\\.rtl\\.css)\"");

        var references = Directory
            .EnumerateFiles(viewsDirectory, "*.cshtml", SearchOption.AllDirectories)
            .SelectMany(file => stylesheetPattern.Matches(File.ReadAllText(file))
                .Select(match => (View: file, Asset: match.Groups["path"].Value)))
            .ToArray();

        Assert.NotEmpty(references);
        foreach (var reference in references)
        {
            Assert.True(
                File.Exists(Path.Combine(webRootDirectory, reference.Asset)),
                $"{Path.GetRelativePath(solutionDirectory.FullName, reference.View)} references missing static asset /{reference.Asset}");
        }
    }
}
