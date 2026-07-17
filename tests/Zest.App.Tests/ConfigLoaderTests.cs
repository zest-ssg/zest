using Xunit;
using Zest.Engine;
using Zest.Infra.Configuration;

// ============================================================
// ConfigLoader — Tests for [compat] and [template] table
// parsing. Verifies the dual-form (flat + nested) reading
// behavior introduced in Phase 7.
// ============================================================

namespace Zest.App.Tests;

public class ConfigLoaderTests
{
    [Fact]
    public void Load_NoConfigFile_ReturnsDefaults()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"zest_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            ConfigLoader.ClearCache();
            var config = ConfigLoader.Load(tempDir);

            Assert.Equal("My Zest Site", config.Title);
            Assert.Equal("native", config.TemplateEngine);
            Assert.False(config.CompatJekyll);
            Assert.False(config.CompatHugo);
            Assert.Equal("zest", config.NunjucksCompatibility);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Load_CompatJekyllEnabled_ParsesCorrectly()
    {
        var tempDir = WriteTempConfig(@"
            title = ""Jekyll Migrated""
            [compat]
            jekyll = true
        ");

        try
        {
            ConfigLoader.ClearCache();
            var config = ConfigLoader.Load(tempDir);

            Assert.Equal("Jekyll Migrated", config.Title);
            Assert.True(config.CompatJekyll);
            Assert.False(config.CompatHexo);
            Assert.False(config.CompatHugo);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Load_CompatMultipleFlags_ParsesAll()
    {
        var tempDir = WriteTempConfig(@"
            title = ""Multi Compat""
            [compat]
            jekyll = true
            hugo = true
            hexo = false
        ");

        try
        {
            ConfigLoader.ClearCache();
            var config = ConfigLoader.Load(tempDir);

            Assert.True(config.CompatJekyll);
            Assert.True(config.CompatHugo);
            Assert.False(config.CompatHexo);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Load_CompatEleventyEnabled_ParsesCorrectly()
    {
        var tempDir = WriteTempConfig(@"
            title = ""11ty Migrated""
            [compat]
            eleventy = true
        ");

        try
        {
            ConfigLoader.ClearCache();
            var config = ConfigLoader.Load(tempDir);

            Assert.True(config.CompatEleventy);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Load_TemplateEngineFromTemplateTable_OverridesTopLevel()
    {
        var tempDir = WriteTempConfig(@"
            title = ""Template Test""
            template_engine = ""native""
            [template]
            engine = ""nunjucks""
        ");

        try
        {
            ConfigLoader.ClearCache();
            var config = ConfigLoader.Load(tempDir);

            // [template].engine overrides template_engine
            Assert.Equal("nunjucks", config.TemplateEngine);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Load_NunjucksCompatibilityFlatForm_ParsesCorrectly()
    {
        var tempDir = WriteTempConfig(@"
            title = ""Flat Nunjucks""
            [template]
            nunjucks_compatibility = ""strict""
        ");

        try
        {
            ConfigLoader.ClearCache();
            var config = ConfigLoader.Load(tempDir);

            Assert.Equal("strict", config.NunjucksCompatibility);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Load_NunjucksCompatibilityNestedForm_ParsesCorrectly()
    {
        var tempDir = WriteTempConfig(@"
            title = ""Nested Nunjucks""
            [template]
            engine = ""nunjucks""
            [template.nunjucks]
            compatibility = ""strict""
        ");

        try
        {
            ConfigLoader.ClearCache();
            var config = ConfigLoader.Load(tempDir);

            Assert.Equal("nunjucks", config.TemplateEngine);
            Assert.Equal("strict", config.NunjucksCompatibility);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Load_NestedOverridesFlat_ForNunjucksCompat()
    {
        // The nested [template.nunjucks] should override the flat
        // nunjucks_compatibility key (nested form wins because it's
        // parsed after).
        var tempDir = WriteTempConfig(@"
            [template]
            nunjucks_compatibility = ""zest""
            [template.nunjucks]
            compatibility = ""strict""
        ");

        try
        {
            ConfigLoader.ClearCache();
            var config = ConfigLoader.Load(tempDir);

            // Nested form wins (parsed last)
            Assert.Equal("strict", config.NunjucksCompatibility);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Load_CacheReturnsSameObject_WhenFileUnchanged()
    {
        var tempDir = WriteTempConfig(@"title = ""Cache Test""");

        try
        {
            ConfigLoader.ClearCache();
            var c1 = ConfigLoader.Load(tempDir);
            var c2 = ConfigLoader.Load(tempDir);

            // Same object reference — caching works
            Assert.Same(c1, c2);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Load_ClearCache_ForcesReload()
    {
        var tempDir = WriteTempConfig(@"title = ""Original""");

        try
        {
            ConfigLoader.ClearCache();
            var c1 = ConfigLoader.Load(tempDir);
            Assert.Equal("Original", c1.Title);

            // Change the file
            var configPath = Path.Combine(tempDir, "_config.toml");
            File.WriteAllText(configPath, @"title = ""Modified""");

            ConfigLoader.ClearCache();
            var c2 = ConfigLoader.Load(tempDir);
            Assert.Equal("Modified", c2.Title);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    // ── helpers ──────────────────────────────────────────────

    private static string WriteTempConfig(string content)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"zest_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "_config.toml"), content);
        return dir;
    }
}
