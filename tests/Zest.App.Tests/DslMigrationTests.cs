using Xunit;
using Zest.Dsl;

// ============================================================
// DslMigration — Tests for YAML↔TOML config conversion and
// frontmatter rewriting. Simulates real Jekyll / Hexo / Hugo
// migration scenarios.
// ============================================================

namespace Zest.App.Tests;

public class DslMigrationTests
{
    // ── YAML → TOML conversion ─────────────────────────────

    [Fact]
    public void ConvertYamlToToml_SimpleConfig_EscapesStrings()
    {
        var yaml = "\ntitle: My Blog\ndescription: A blog about code\n";
        var toml = DslMigration.convertYamlToToml(yaml);

        Assert.Contains("title = \"My Blog\"", toml);
        Assert.Contains("description = \"A blog about code\"", toml);
    }

    [Fact]
    public void ConvertYamlToToml_NumericValues_NotQuoted()
    {
        var yaml = "\nport: 8080\nmax_posts: 10\n";
        var toml = DslMigration.convertYamlToToml(yaml);

        Assert.Contains("port = 8080", toml);
        Assert.Contains("max_posts = 10", toml);
    }

    [Fact]
    public void ConvertYamlToToml_BooleanValues_NotQuoted()
    {
        var yaml = "\nenable_cache: true\nminify: false\n";
        var toml = DslMigration.convertYamlToToml(yaml);

        Assert.Contains("enable_cache = true", toml);
        Assert.Contains("minify = false", toml);
    }

    [Fact]
    public void ConvertYamlToToml_DottedKeys_BecomeNestedTables()
    {
        var yaml = "\nauthor.name: John Doe\nauthor.email: john@example.com\nsite.title: My Site\n";
        var toml = DslMigration.convertYamlToToml(yaml);

        // Dotted keys -> [table] sections
        Assert.Contains("[author]", toml);
        Assert.Contains("[site]", toml);
        Assert.Contains("name = \"John Doe\"", toml);
        Assert.Contains("email = \"john@example.com\"", toml);
        Assert.Contains("title = \"My Site\"", toml);
    }

    [Fact]
    public void ConvertYamlToToml_MixedFlatAndNested()
    {
        var yaml = "\ntitle: My Site\nbase_url: https://example.com\nauthor.name: Alice\nauthor.github: alice123\ntheme.color: dark\n";
        var toml = DslMigration.convertYamlToToml(yaml);

        // Flat keys at top
        Assert.Contains("title = \"My Site\"", toml);
        Assert.Contains("base_url = \"https://example.com\"", toml);
        // Nested under [tables]
        Assert.Contains("[author]", toml);
        Assert.Contains("[theme]", toml);
        Assert.Contains("name = \"Alice\"", toml);
        Assert.Contains("github = \"alice123\"", toml);
        Assert.Contains("color = \"dark\"", toml);
    }

    [Fact]
    public void ConvertYamlToToml_EmptyString_ReturnsEmpty()
    {
        var result = DslMigration.convertYamlToToml("");
        Assert.True(string.IsNullOrEmpty(result));
    }

    // ── Jekyll-realistic migration scenario ────────────────

    [Fact]
    public void ConvertYamlToToml_JekyllConfig_AllFieldsConverted()
    {
        // Realistic _config.yml from a Jekyll project
        var jekyllYaml = "\n"
            + "title: My Jekyll Blog\n"
            + "email: dev@example.com\n"
            + "description: >-\n"
            + "  A beautiful blog\n"
            + "  built with Jekyll\n"
            + "baseurl: \"\"\n"
            + "url: https://myblog.dev\n"
            + "twitter_username: jekyllrb\n"
            + "github_username: jekyll\n"
            + "markdown: kramdown\n"
            + "theme: minima\n"
            + "plugins:\n"
            + "  - jekyll-feed\n"
            + "  - jekyll-seo-tag\n"
            + "paginate: 5\n"
            + "paginate_path: /page:num/\n"
            + "permalink: /:year/:month/:day/:title/\n";
        var toml = DslMigration.convertYamlToToml(jekyllYaml);

        Assert.Contains("title = \"My Jekyll Blog\"", toml);
        Assert.Contains("email = \"dev@example.com\"", toml);
        Assert.Contains("url = \"https://myblog.dev\"", toml);
        Assert.Contains("twitter_username = \"jekyllrb\"", toml);
        Assert.Contains("github_username = \"jekyll\"", toml);
        Assert.Contains("markdown = \"kramdown\"", toml);
        Assert.Contains("theme = \"minima\"", toml);
        Assert.Contains("paginate = 5", toml);
        Assert.Contains("permalink = \"/:year/:month/:day/:title/\"", toml);
    }

    // ── Hexo-realistic migration scenario ──────────────────

    [Fact]
    public void ConvertYamlToToml_HexoConfig_AllFieldsConverted()
    {
        // Realistic _config.yml from a Hexo project
        var hexoYaml = "\n"
            + "title: Hexo Blog\n"
            + "subtitle: A fast static blog\n"
            + "description: Blog powered by Hexo\n"
            + "author: John Doe\n"
            + "language: en\n"
            + "timezone: Asia/Shanghai\n"
            + "url: https://hexoblog.dev\n"
            + "root: /\n"
            + "theme: landscape\n"
            + "deploy:\n"
            + "  type: git\n"
            + "  repo: git@github.com:user/repo.git\n"
            + "  branch: gh-pages\n"
            + "archive_generator:\n"
            + "  per_page: 10\n"
            + "  yearly: true\n"
            + "  monthly: true\n";
        var toml = DslMigration.convertYamlToToml(hexoYaml);

        Assert.Contains("title = \"Hexo Blog\"", toml);
        Assert.Contains("subtitle = \"A fast static blog\"", toml);
        Assert.Contains("description = \"Blog powered by Hexo\"", toml);
        Assert.Contains("author = \"John Doe\"", toml);
        Assert.Contains("language = \"en\"", toml);
        Assert.Contains("url = \"https://hexoblog.dev\"", toml);
        Assert.Contains("theme = \"landscape\"", toml);
        // Nested tables for deploy
        Assert.Contains("[deploy]", toml);
        Assert.Contains("type = \"git\"", toml);
        Assert.Contains("branch = \"gh-pages\"", toml);
        Assert.Contains("[archive_generator]", toml);
        Assert.Contains("per_page = 10", toml);
        Assert.Contains("yearly = true", toml);
        Assert.Contains("monthly = true", toml);
    }

    // ── TOML → YAML roundtrip ─────────────────────────────

    [Fact]
    public void ConvertTomlToYaml_SimpleConfig()
    {
        var toml = "title = \"My Site\"";
        var yaml = DslMigration.convertTomlToYaml(toml);

        Assert.Contains("title:", yaml);
        Assert.Contains("My Site", yaml);
    }

    [Fact]
    public void ConvertTomlToYaml_DottedKeys_Nested()
    {
        var toml = "\ntitle = \"My Site\"\n[author]\nname = \"Alice\"\nemail = \"alice@example.com\"\n";
        var yaml = DslMigration.convertTomlToYaml(toml);

        Assert.NotNull(yaml);
        Assert.Contains("title", yaml);
        Assert.Contains("author", yaml);
        Assert.Contains("name", yaml);
    }

    [Fact]
    public void YamlToTomlAndBack_Roundtrip_PreservesKeySet()
    {
        var originalYaml = "\ntitle: Test\ndescription: A test site\nauthor.name: dev\n";
        var toml = DslMigration.convertYamlToToml(originalYaml);
        var yamlBack = DslMigration.convertTomlToYaml(toml);

        Assert.Contains("title", yamlBack);
        Assert.Contains("Test", yamlBack);
        Assert.Contains("description", yamlBack);
        Assert.Contains("author", yamlBack);
        Assert.Contains("dev", yamlBack);
    }

    // ── Frontmatter detection and conversion ───────────────

    [Fact]
    public void DetectFrontmatter_YamlDashes_ReturnsYaml()
    {
        var content = "---\ntitle: Hello\n---\n\nBody text.";
        Assert.Equal("yaml", DslMigration.detectFrontmatter(content));
    }

    [Fact]
    public void DetectFrontmatter_TomlPluses_ReturnsToml()
    {
        var content = "+++\ntitle = \"Hello\"\n+++\n\nBody text.";
        Assert.Equal("toml", DslMigration.detectFrontmatter(content));
    }

    [Fact]
    public void DetectFrontmatter_NoDelimiter_ReturnsNone()
    {
        Assert.Equal("none", DslMigration.detectFrontmatter("Just body text."));
    }

    [Fact]
    public void SplitFrontmatter_Yaml_ExtractsCorrectly()
    {
        var content = "---\ntitle: My Post\ndate: 2024-01-01\n---\n\nBody here.";
        var (fm, body, fmt) = DslMigration.splitFrontmatter(content);

        Assert.Equal("yaml", fmt);
        Assert.Contains("title: My Post", fm);
        Assert.Contains("date: 2024-01-01", fm);
        Assert.Contains("Body here.", body);
    }

    [Fact]
    public void SplitFrontmatter_Toml_ExtractsCorrectly()
    {
        var content = "+++\ntitle = \"Post\"\ndate = \"2024-01-01\"\n+++\n\nBody.";
        var (fm, body, fmt) = DslMigration.splitFrontmatter(content);

        Assert.Equal("toml", fmt);
        Assert.Contains("title = \"Post\"", fm);
        Assert.Contains("Body.", body);
    }

    [Fact]
    public void ConvertFrontmatter_YamlToToml_PreservesBody()
    {
        var content = "---\ntitle: Hello\ndate: 2024-01-01\n---\n\n# Welcome\n\nBody content.";
        var result = DslMigration.convertFrontmatter(content, "toml");

        Assert.StartsWith("+++", result);
        Assert.Contains("title =", result);
        Assert.Contains("# Welcome", result);
        Assert.Contains("Body content.", result);
    }

    [Fact]
    public void ConvertFrontmatter_TomlToYaml_PreservesBody()
    {
        var content = "+++\ntitle = \"Post\"\ndate = \"2024-01-01\"\n+++\n\nBody.";
        var result = DslMigration.convertFrontmatter(content, "yaml");

        Assert.StartsWith("---", result);
        Assert.Contains("title:", result);
        Assert.Contains("Body.", result);
    }

    [Fact]
    public void ConvertFrontmatter_NoFrontmatter_ReturnsOriginal()
    {
        var content = "Just plain body without frontmatter.";
        var result = DslMigration.convertFrontmatter(content, "toml");

        Assert.Equal(content, result);
    }

    [Fact]
    public void ConvertFrontmatter_SameFormat_ReturnsOriginal()
    {
        var content = "---\ntitle: Hi\n---\n\nBody.";
        var result = DslMigration.convertFrontmatter(content, "yaml");

        Assert.Equal(content, result);
    }

    // ── Custom migration registration ──────────────────────

    [Fact]
    public void RunMigrations_WithNoRegistered_ReturnsEmpty()
    {
        var result = DslMigration.runMigrations(".", ".");
        Assert.Empty(result);
    }
}
