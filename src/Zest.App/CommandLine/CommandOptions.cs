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
/// Options for `zest serve [--port PORT] [--host HOST] [--open]`
/// </summary>
public record ServeCommandOptions : CommandOptions
{
    public int? PortOverride { get; init; }
    public string Host { get; init; } = "localhost";
    public bool OpenBrowser { get; init; }
}

/// <summary>
/// Options for `zest preview [--port PORT] [--host HOST] [--open]`
/// </summary>
public record PreviewCommandOptions
{
    public int Port { get; init; } = 8080;
    public string Host { get; init; } = "localhost";
    public bool OpenBrowser { get; init; }
    public bool Verbose { get; init; }
    public bool Quiet { get; init; }
    public bool ShowHelp { get; init; }
}

/// <summary>
/// Options for `zest init [path]`
/// </summary>
public record InitCommandOptions
{
    public string TargetDirectory { get; init; } = ".";
}
