using Xunit;
using Zest.App.Controllers;
using Zest.Engine;
using Zest.Infra.Services;

// ============================================================
// MigrateCommand — Tests for the `zest migrate` command.
// Verifies argument validation, unsupported SSG rejection,
// and basic migrate flow.
// ============================================================

namespace Zest.App.Tests;

[Collection("Sequential")]
public class MigrateCommandTests
{
    [Fact]
    public void Execute_NoArgs_Returns1()
    {
        var exitCode = MigrateCommand.Execute(["migrate"]);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Execute_UnknownSsg_Returns1()
    {
        var exitCode = MigrateCommand.Execute(["migrate", "wordpress"]);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Execute_ValidSsgDryRun_Succeeds()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"zest_test_migrate_{Guid.NewGuid():N}");
        var toDir = Path.Combine(tempDir, "_zest_out");
        Directory.CreateDirectory(tempDir);

        var originalDir = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = tempDir;

            var exitCode = MigrateCommand.Execute([
                "migrate", "jekyll", "--from", tempDir, "--to", toDir, "--dry-run"
            ]);
            Assert.Equal(0, exitCode);
        }
        finally
        {
            Environment.CurrentDirectory = originalDir;
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Execute_ValidSsgWritesFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"zest_test_migrate_{Guid.NewGuid():N}");
        var toDir = Path.Combine(tempDir, "_zest_out");
        Directory.CreateDirectory(tempDir);

        var originalDir = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = tempDir;

            var exitCode = MigrateCommand.Execute([
                "migrate", "jekyll", "--from", tempDir, "--to", toDir
            ]);

            Assert.Equal(0, exitCode);
            Assert.True(Directory.Exists(toDir));

            // The generated _config.toml should exist and contain compat info
            var configPath = Path.Combine(toDir, "_config.toml");
            Assert.True(File.Exists(configPath));

            var config = File.ReadAllText(configPath);
            Assert.Contains("[compat]", config);
            Assert.Contains("jekyll = true", config);
        }
        finally
        {
            Environment.CurrentDirectory = originalDir;
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Execute_InvalidFromDir_Returns1()
    {
        var exitCode = MigrateCommand.Execute([
            "migrate", "jekyll", "--from", "Z:\\nonexistent_path_xyz"
        ]);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Execute_LongFormOptions_ParsedCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"zest_test_migrate_{Guid.NewGuid():N}");
        var toDir = Path.Combine(tempDir, "_zest_out");
        Directory.CreateDirectory(tempDir);

        var originalDir = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = tempDir;

            // Test --from=<dir> and --to=<dir> syntax
            var exitCode = MigrateCommand.Execute([
                "migrate", "jekyll",
                $"--from={tempDir}",
                $"--to={toDir}",
                "--dry-run"
            ]);
            Assert.Equal(0, exitCode);
        }
        finally
        {
            Environment.CurrentDirectory = originalDir;
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Execute_AllFourSsgs_AcceptWithoutError()
    {
        foreach (var ssg in new[] { "jekyll", "hexo", "hugo", "eleventy" })
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"zest_test_{ssg}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            var originalDir = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = tempDir;
                var exitCode = MigrateCommand.Execute([
                    "migrate", ssg, "--from", tempDir,
                    "--to", Path.Combine(tempDir, "out"),
                    "--dry-run"
                ]);
                Assert.Equal(0, exitCode);
            }
            finally
            {
                Environment.CurrentDirectory = originalDir;
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
