namespace Zest.Compiler.Execution
open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Security.Cryptography

/// DLL discovery and isolation for the Zest.Markup assembly used by FSI subprocesses.
module DslAssembly =

    // Discovery and isolation run from parallel rendering and the finalize
    // step, so both the memoised path and the cache must be synchronised.
    let private dslLock = obj()
    let mutable private dslDllPath = ""

    /// Find the Zest.Markup.dll path — looks in several common locations.
    let findDslDll () : string =
        lock dslLock (fun () ->
            if not (String.IsNullOrEmpty dslDllPath) && File.Exists dslDllPath then dslDllPath
            else
                let engineDir =
                    let loc = System.Reflection.Assembly.GetExecutingAssembly().Location
                    if not (String.IsNullOrEmpty loc) && File.Exists loc then
                        Path.GetDirectoryName loc
                    else
                        AppContext.BaseDirectory
                let candidates = [
                    Path.Combine(engineDir, "Zest.Markup.dll")
                    Path.Combine(engineDir, "..", "..", "..", "..", "..", "Zest.Markup", "bin", "Release", "net10.0", "Zest.Markup.dll")
                    Path.Combine(engineDir, "..", "..", "..", "..", "..", "Zest.Markup", "bin", "Debug", "net10.0", "Zest.Markup.dll")
                ]
                let result =
                    match candidates |> List.tryFind File.Exists with
                    | Some p -> p
                    | None ->
                        let rec searchUp (dir: string) =
                            let c1 = Path.Combine(dir, "Zest.Markup.dll")
                            if File.Exists c1 then Some c1
                            else
                                let c2 = Path.Combine(dir, "src", "Zest.Markup", "bin", "Release", "net10.0", "Zest.Markup.dll")
                                if File.Exists c2 then Some c2
                                else
                                    let c3 = Path.Combine(dir, "src", "Zest.Markup", "bin", "Debug", "net10.0", "Zest.Markup.dll")
                                    if File.Exists c3 then Some c3
                                    else
                                        let parent = Path.GetDirectoryName(dir)
                                        if String.IsNullOrEmpty parent || parent = dir then None
                                        else searchUp parent
                        match searchUp (Directory.GetCurrentDirectory()) with
                        | Some p -> p
                        | None -> failwithf "Zest.Markup.dll not found. Engine dir: %s" engineDir
                dslDllPath <- result
                result)

    /// Cached DLL isolation — copies Zest.Markup.dll (and its sibling
    /// dependencies) to a temp directory to avoid FSharp.Core version
    /// conflicts with FSI. ConcurrentDictionary keeps parallel callers from
    /// corrupting the table; the copy itself is idempotent.
    let private dslDllCache = ConcurrentDictionary<string, string>()

    let getIsolatedDslDll () : string =
        let srcPath = findDslDll ()
        let srcHash =
            use md5 = MD5.Create()
            use stream = File.OpenRead(srcPath)
            let hash = md5.ComputeHash(stream)
            Convert.ToHexString(hash)
        dslDllCache.GetOrAdd(srcHash, fun hash ->
            let tempDir = Path.Combine(Path.GetTempPath(), "zest-dsl-" + hash)
            Directory.CreateDirectory(tempDir) |> ignore
            // Isolate the full dependency set (Zest.Markup + Zest.Compiler + third-party
            // deps) into one folder so FSI resolves a single, consistent
            // Zest.Compiler assembly — Zest.Markup now references it for the native
            // `md` Markdown helper. FSharp.Core is deliberately NOT copied: it
            // stays resolved from the app base, so only one copy is loaded
            // (avoids FS0001 / version conflicts). The FSI↔app boundary is a
            // plain string (the script's rendered output), so two physical
            // copies of these assemblies cause no type-identity issues.
            if not (File.Exists(Path.Combine(tempDir, "Zest.Markup.dll"))) then
                for f in Directory.GetFiles(Path.GetDirectoryName(srcPath), "*.dll") do
                    if not (String.Equals(Path.GetFileName(f), "FSharp.Core.dll", StringComparison.OrdinalIgnoreCase)) then
                        try File.Copy(f, Path.Combine(tempDir, Path.GetFileName(f)), true)
                        with _ -> ()   // another process may hold the file; keep the existing copy
            Path.Combine(tempDir, "Zest.Markup.dll"))
