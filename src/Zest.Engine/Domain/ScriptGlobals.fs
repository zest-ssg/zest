namespace Zest.Engine

open System.Collections.Generic

/// <summary>
/// Globals injected into the F# script evaluation context.
/// </summary>
type ScriptGlobals = {
    Site: IDictionary<string, obj>
    Collections: IDictionary<string, obj>
    Data: IDictionary<string, obj>
    Page: Page option
    Content: string option
}
