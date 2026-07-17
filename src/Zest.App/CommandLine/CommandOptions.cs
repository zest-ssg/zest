namespace Zest.App.CommandLine;

/// <summary>
/// Base class for all parsed command options.
/// </summary>
public abstract record CommandOptions
{
    public bool Verbose { get; init; }
    public bool Quiet { get; init; }
    public bool ShowHelp { get; init; }
}

/// <summary>
/// Options for `zest build [path] [--watch] [--no-incremental]`
/// </summary>
public record BuildCommandOptions : CommandOptions
{
    public string? ProjectPath { get; init; }
    public bool Watch { get; init; }
}

/// <summary>
/// Options for `zest serve [--port PORT] [--host HOST] [--open] [--spa]`
/// </summary>
public record ServeCommandOptions : CommandOptions
{
    public int? PortOverride { get; init; }
    public string Host { get; init; } = "localhost";
    public bool OpenBrowser { get; init; }
    public bool SPA { get; init; }
    public bool DirectoryListing { get; init; }
}

/// <summary>
/// Options for `zest preview [--port PORT] [--host HOST] [--open] [--watch] [--livereload] [--spa]`
/// </summary>
public record PreviewCommandOptions : CommandOptions
{
    public int Port { get; init; } = 8080;
    public string Host { get; init; } = "localhost";
    public bool OpenBrowser { get; init; }
    public bool Watch { get; init; }
    public bool LiveReload { get; init; }
    public bool SPA { get; init; }
    public bool DirectoryListing { get; init; }
}

/// <summary>
/// Options for `zest init [path]`
/// </summary>
public record InitCommandOptions
{
    public string TargetDirectory { get; init; } = ".";
}
