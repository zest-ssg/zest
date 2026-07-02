namespace Zest.Engine

open System.Collections.Generic

/// <summary>
/// Globals injected into the F# script evaluation context.
/// </summary>
type ScriptContext = {
    Site: IDictionary<string, obj>
    Collections: IDictionary<string, obj>
    Data: IDictionary<string, obj>
    Page: ContentPage option
    Content: string option
}
