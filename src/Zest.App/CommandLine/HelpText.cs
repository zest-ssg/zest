namespace Zest.App.CommandLine;

/// <summary>
/// Centralized help text constants for the Zest CLI.
/// Keeps Program.cs clean and enables consistent formatting across commands.
/// </summary>
internal static class HelpText
{
    public const string Version = "0.3.0";

    public const string Header = "Zest v{0} — Zenith Efficient Static Toolkit";

    public const string Ecosystem = "Ecosystem: .zpage.fsx + .zcss + .toml + .js";

    public const string Usage = "Usage: zest <command> [options]";

    public const string Commands =
        "  build                       Build static site to _site/";

    public const string CommandsServe =
        "  serve                       Build + start dev server (live reload)";

    public const string CommandsPreview =
        "  preview                     Preview built _site/ directory";

    public const string CommandsInit =
        "  init                        Scaffold new project from template";

    public const string CommandsVersion =
        "  --version, -v               Show version info";

    public const string CommandsHelp =
        "  --help,    -h               Show this help";

    public const string Options =
        "  --port, -p PORT             Server port (default: 8080)";

    public const string OptionsWatch =
        "  --watch, -w                 Watch files and auto-rebuild";

    public const string FileFormats =
        "  .zpage.fsx  F# template scripts with HTML DSL & Markdown\n" +
        "  .zhtml      Lightweight HTML templates (no F#)\n" +
        "  .zcss       Zest Stylesheet — CSS superset with nesting/vars\n" +
        "  .toml       Site config & global data (zero-config by default)\n" +
        "  .js         Client scripts, copied as-is to output\n" +
        "  .md         Standard Markdown content files";

    public const string HelpSuffix = "Run 'zest <command> --help' for command-specific options.";
}
