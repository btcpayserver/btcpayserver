using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BTCPayServer.Configuration;
using Xunit;

namespace BTCPayServer.Tests
{
    public class DocumentationTests
    {
        private static readonly (string Title, HashSet<string> Names)[] ConfigurationCategories =
        {
            ("Process and network", new HashSet<string> { "help", "network", "chains", "nodefaultchain", "conf", "port", "bind", "datadir" }),
            ("Database", new HashSet<string> { "postgres", "explorerpostgres" }),
            ("HTTP and security", new HashSet<string> { "nocsp", "rootpath", "xforwardedproto", "disable-registration" }),
            ("Host and external services", new HashSet<string> { "externalservices", "btcpayhostenabled", "btcpayhostexecutable", "torrcfile", "torservices", "socksendpoint", "updateurl" }),
            ("Logging and diagnostics", new HashSet<string> { "debuglog", "debugloglevel" }),
            ("Development", new HashSet<string> { "cheatmode" }),
            ("Chain services", new HashSet<string> { "btcexplorerurl", "btcexplorercookiefile", "btclightning", "btcexternallndgrpc", "btcexternallndrest", "btcexternalrtl", "btcexternalspark", "btcexternalcharge" })
        };

        private static readonly HashSet<string> LegacyOptions = new HashSet<string>
        {
            "testnet", "regtest", "signet", "deprecated", "recommended-plugins"
        };

        private static readonly Dictionary<string, string> ConfigurationKeys = new Dictionary<string, string>
        {
            { "btcexplorerurl", "btc.explorer.url" },
            { "btcexplorercookiefile", "btc.explorer.cookiefile" },
            { "btclightning", "btc.lightning" },
            { "btcexternallndgrpc", "btc.external.lndgrpc" },
            { "btcexternallndrest", "btc.external.lndrest" },
            { "btcexternalrtl", "btc.external.rtl" },
            { "btcexternalspark", "btc.external.spark" },
            { "btcexternalcharge", "btc.external.charge" }
        };

        [Trait("PreReleaseCheck", "PreReleaseCheck")]
        [Fact]
        public void CheckDocumentation()
        {
            var repositoryRoot = TestUtils.TryGetSolutionDirectoryInfo().FullName;
            var errors = CheckLinks(repositoryRoot);
            var outputPath = Path.Combine(repositoryRoot, "docs", "operators", "configuration-reference.md");
            var generatedReference = GenerateConfigurationReference(errors);

            if (generatedReference is not null && File.ReadAllText(outputPath) != generatedReference)
            {
                File.WriteAllText(outputPath, generatedReference);
                errors.Add("docs/operators/configuration-reference.md was stale and has been updated. Review and commit it, then rerun this test.");
            }

            Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        }

        private static List<string> CheckLinks(string repositoryRoot)
        {
            var files = new[] { "README.md", "RELEASE-CHECKLIST.md", "RELEASE-CYCLES.md" }
                .Select(path => Path.Combine(repositoryRoot, path))
                .Concat(Directory.EnumerateFiles(Path.Combine(repositoryRoot, "docs"), "*.md", SearchOption.AllDirectories))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            var anchors = new Dictionary<string, HashSet<string>>();
            var errors = new List<string>();

            foreach (var file in files)
            {
                var markdown = File.ReadAllText(file);
                var relativeFile = Path.GetRelativePath(repositoryRoot, file).Replace(Path.DirectorySeparatorChar, '/');
                foreach (Match match in Regex.Matches(markdown, @"!\[[^\]]*\]\(([^)]+)\)"))
                {
                    var rawTarget = match.Groups[1].Value.Trim().TrimStart('<').TrimEnd('>');
                    if (string.IsNullOrEmpty(rawTarget) ||
                        Regex.IsMatch(rawTarget, @"^(https?:|data:)", RegexOptions.IgnoreCase))
                        continue;

                    var path = Uri.UnescapeDataString(rawTarget.Split('#', 2)[0]);
                    var importedDocumentation = relativeFile.StartsWith("docs/users/", StringComparison.Ordinal) ||
                                                relativeFile.StartsWith("docs/operators/", StringComparison.Ordinal) ||
                                                relativeFile.StartsWith("docs/developers/", StringComparison.Ordinal);
                    if (importedDocumentation &&
                        !path.StartsWith("./", StringComparison.Ordinal) &&
                        !path.StartsWith("../", StringComparison.Ordinal))
                    {
                        errors.Add($"{relativeFile}: local image path must start with ./ or ../: {rawTarget}");
                    }

                    var target = Path.GetFullPath(path, Path.GetDirectoryName(file)!);
                    if (!File.Exists(target))
                        errors.Add($"{relativeFile}: missing image {rawTarget}");
                }

                foreach (Match match in Regex.Matches(markdown, @"(?<!!)\[[^\]]*\]\(([^)]+)\)"))
                {
                    var rawTarget = match.Groups[1].Value.Trim().TrimStart('<').TrimEnd('>');
                    if (string.IsNullOrEmpty(rawTarget) ||
                        Regex.IsMatch(rawTarget, @"^(https?:|mailto:)", RegexOptions.IgnoreCase) ||
                        rawTarget.StartsWith('#'))
                        continue;

                    var parts = rawTarget.Split('#', 2);
                    var path = Uri.UnescapeDataString(parts[0]);
                    if (string.IsNullOrEmpty(path))
                        continue;

                    var target = Path.GetFullPath(path, Path.GetDirectoryName(file)!);
                    var candidates = new[] { target, $"{target}.md", Path.Combine(target, "README.md") };
                    var existing = candidates.FirstOrDefault(candidate => File.Exists(candidate) || Directory.Exists(candidate));
                    if (existing is null)
                    {
                        errors.Add($"{relativeFile}: missing {rawTarget}");
                    }
                    else if (parts.Length == 2 && Path.GetExtension(existing).Equals(".md", StringComparison.OrdinalIgnoreCase) &&
                             !GetAnchors(existing, anchors).Contains(Uri.UnescapeDataString(parts[1])))
                    {
                        errors.Add($"{relativeFile}: missing anchor {rawTarget}");
                    }
                }
            }

            return errors;
        }

        private static HashSet<string> GetAnchors(string file, Dictionary<string, HashSet<string>> cache)
        {
            if (cache.TryGetValue(file, out var cached))
                return cached;

            var markdown = File.ReadAllText(file);
            var anchors = Regex.Matches(markdown, @"<a\s+(?:name|id)=[""']([^""']+)[""']", RegexOptions.IgnoreCase)
                .Select(match => match.Groups[1].Value)
                .ToHashSet();
            var counts = new Dictionary<string, int>();
            foreach (Match match in Regex.Matches(markdown, @"^#{1,6}\s+(.+)$", RegexOptions.Multiline))
            {
                var slug = Regex.Replace(match.Groups[1].Value, "<[^>]+>", string.Empty);
                slug = Regex.Replace(slug, "[`*_~]", string.Empty).Trim().ToLowerInvariant();
                slug = Regex.Replace(slug, @"[^\p{L}\p{N}\s-]", string.Empty);
                slug = Regex.Replace(slug, @"\s+", "-");
                counts.TryGetValue(slug, out var count);
                counts[slug] = count + 1;
                anchors.Add(count == 0 ? slug : $"{slug}-{count}");
            }

            cache.Add(file, anchors);
            return anchors;
        }

        private static string GenerateConfigurationReference(List<string> errors)
        {
            var originalOutput = Console.Out;
            var originalError = Console.Error;
            using var output = new StringWriter();
            try
            {
                Console.SetOut(output);
                Console.SetError(output);
                new DefaultConfiguration().CreateConfigurationBuilder(new[] { "--help" });
            }
            finally
            {
                Console.SetOut(originalOutput);
                Console.SetError(originalError);
            }

            var help = Regex.Replace(output.ToString(), "\u001b\\[[0-9;]*m", string.Empty);
            if (!help.Contains("Usage: BTCPay [options]"))
            {
                errors.Add("Could not read BTCPay Server command-line help.");
                return null;
            }

            var options = new List<ConfigurationOption>();
            var inOptions = false;
            foreach (var line in Regex.Split(help, "\r?\n"))
            {
                if (line == "Options:")
                {
                    inOptions = true;
                    continue;
                }
                if (!inOptions)
                    continue;
                if (!line.StartsWith("  "))
                {
                    if (options.Count > 0)
                        break;
                    continue;
                }

                var match = Regex.Match(line, @"^\s{2}(.+?)\s{2,}(.+)$");
                if (!match.Success)
                    continue;
                var syntax = match.Groups[1].Value.Trim();
                var names = Regex.Matches(syntax, @"--([a-z0-9-]+)", RegexOptions.IgnoreCase);
                if (names.Count > 0)
                    options.Add(new ConfigurationOption(names[0].Groups[1].Value, syntax, match.Groups[2].Value.Trim()));
            }

            var assigned = ConfigurationCategories.SelectMany(category => category.Names).ToHashSet();
            var unknown = options.Where(option => !assigned.Contains(option.Name) && !LegacyOptions.Contains(option.Name)).Select(option => option.Name).ToArray();
            var missing = assigned.Where(name => options.All(option => option.Name != name)).ToArray();
            if (unknown.Length > 0)
                errors.Add($"Uncategorized options: {string.Join(", ", unknown)}");
            if (missing.Length > 0)
                errors.Add($"Missing options: {string.Join(", ", missing)}");
            if (unknown.Length > 0 || missing.Length > 0)
                return null;

            var sections = ConfigurationCategories.Select(category =>
            {
                var introduction = category.Title == "Chain services" ? ChainServicesIntroduction + "\n\n" : string.Empty;
                var rows = options.Where(option => category.Names.Contains(option.Name)).Select(ConfigurationRow);
                return $"## {category.Title}\n\n{introduction}| Command line | Configuration file | Environment | Description |\n|---|---|---|---|\n{string.Join("\n", rows)}";
            });

            return "# Configuration Reference\n\n" +
                   "<!-- Generated by the PreReleaseCheck test. Do not edit manually. -->\n\n" +
                   "This reference is generated from every command-line option registered by the\n" +
                   "standard Bitcoin build of BTCPay Server. Use the same application version that\n" +
                   "you deploy because options and defaults can change between releases. Some\n" +
                   "advanced settings are available only through configuration providers and are\n" +
                   "documented with the feature that consumes them.\n\n" +
                   "Configuration-file keys, environment variables, and command-line options feed\n" +
                   "the same configuration system. Environment variables use the `BTCPAY_`\n" +
                   "prefix. Legacy compatibility options are intentionally omitted.\n\n" +
                   "The maintainer guide explains how to run the pre-release check and regenerate\n" +
                   "this page.\n\n" +
                   string.Join("\n\n", sections) + "\n";
        }

        private static string ConfigurationRow(ConfigurationOption option)
        {
            var key = ConfigurationKeys.GetValueOrDefault(option.Name, option.Name);
            var configuration = option.Name == "help" ? "N/A" : $"`{key}`";
            var environment = option.Name == "help" ? "N/A" : $"`BTCPAY_{option.Name.ToUpperInvariant()}`";
            var description = option.Name == "btcexplorercookiefile"
                ? "Path to the NBXplorer cookie file (default: the network data directory)"
                : option.Description;
            return $"| `{EscapeCell(option.Syntax)}` | {configuration} | {environment} | {EscapeCell(description)} |";
        }

        private static string EscapeCell(string value)
        {
            return value.Replace("|", "\\|");
        }

        private const string ChainServicesIntroduction = "The generated options use Bitcoin (`BTC`) as the chain prefix. Builds that\n" +
            "include other chains use the same setting names with `btc` replaced by the\n" +
            "lowercase crypto code in command-line and configuration-file keys, and by the\n" +
            "uppercase crypto code in environment variables. For example, Litecoin's\n" +
            "explorer URL is `--ltcexplorerurl`, `ltc.explorer.url`, or\n" +
            "`BTCPAY_LTCEXPLORERURL`.\n\n" +
            "| Chain | Crypto code |\n" +
            "|---|---|\n" +
            "| Bitcoin | `BTC` |\n" +
            "| Bitcoin Gold | `BTG` |\n" +
            "| Dash | `DASH` |\n" +
            "| Dogecoin | `DOGE` |\n" +
            "| Groestlcoin | `GRS` |\n" +
            "| Liquid Bitcoin | `LBTC` |\n" +
            "| Litecoin | `LTC` |\n" +
            "| Monacoin | `MONA` |\n\n" +
            "Only configure chains included in the deployed build and its NBXplorer\n" +
            "instance. Not every chain supports every Lightning-specific setting.";

        private sealed record ConfigurationOption(string Name, string Syntax, string Description);
    }
}
