using Xunit;
using Zest.App.Controllers;
using Zest.Engine;

// ============================================================
// ConfigConverter — Tests for the `zest convert-config` command.
// Verifies argument validation, format resolution, and the
// FileExtensions.Yaml/Toml integration.
// ============================================================

namespace Zest.App.Tests;

[Collection("Sequential")]
public class ConfigConverterTests
{
    [Fact]
    public void Execute_InvalidFromFormat_Returns1()
    {
        var exitCode = ConfigConverter.Execute(["convert-config", "xml", "toml"]);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Execute_InvalidToFormat_Returns1()
    {
        var exitCode = ConfigConverter.Execute(["convert-config", "yaml", "json"]);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Execute_SameFormat_Returns0()
    {
        var exitCode = ConfigConverter.Execute(["convert-config", "yaml", "yaml"]);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Execute_YmlNormalizedToYaml_SameFormat_Returns0()
    {
        // "yml" is normalized to "yaml", so yml → yaml should be same-format
        var exitCode = ConfigConverter.Execute(["convert-config", "yml", "yaml"]);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Execute_NotEnoughArgs_Returns1()
    {
        var exitCode = ConfigConverter.Execute(["convert-config"]);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Execute_NoConfigFileFound_Returns1()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"zest_test_conv_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var originalDir = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = tempDir;
            var exitCode = ConfigConverter.Execute(["convert-config", "yaml", "toml"]);
            Assert.Equal(1, exitCode); // No config file
        }
        finally
        {
            Environment.CurrentDirectory = originalDir;
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Execute_YamlToTomlWithConfigFile_Returns0()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"zest_test_conv_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        // Create a simple YAML config
        var ymlContent = @"
title: Test Site
base_url: https://example.com
description: A test site
";
        File.WriteAllText(Path.Combine(tempDir, "_config.yml"), ymlContent,
                          System.Text.Encoding.UTF8);

        var originalDir = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = tempDir;
            var exitCode = ConfigConverter.Execute(["convert-config", "yaml", "toml"]);
            Assert.Equal(0, exitCode);

            // Verify _config.toml was created
            var tomlPath = Path.Combine(tempDir, "_config.toml");
            Assert.True(File.Exists(tomlPath));

            var tomlContent = File.ReadAllText(tomlPath);
            Assert.Contains("Test Site", tomlContent);
            Assert.Contains("https://example.com", tomlContent);
        }
        finally
        {
            Environment.CurrentDirectory = originalDir;
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Execute_TomlToYamlWithConfigFile_Returns0()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"zest_test_conv_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var tomlContent = @"
title = ""Test Site""
base_url = ""https://example.com""
";
        File.WriteAllText(Path.Combine(tempDir, "_config.toml"), tomlContent,
                          System.Text.Encoding.UTF8);

        var originalDir = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = tempDir;
            var exitCode = ConfigConverter.Execute(["convert-config", "toml", "yaml"]);
            Assert.Equal(0, exitCode);

            var yamlPath = Path.Combine(tempDir, "_config.yml");
            Assert.True(File.Exists(yamlPath));

            var yamlContent = File.ReadAllText(yamlPath);
            Assert.Contains("Test Site", yamlContent);
        }
        finally
        {
            Environment.CurrentDirectory = originalDir;
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
