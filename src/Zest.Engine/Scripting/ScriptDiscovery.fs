namespace Zest.Engine.Scripting

open System
open System.Collections.Generic
open System.IO
open System.Security.Cryptography

/// DLL discovery and isolation for the Zest.Dsl assembly used by FSI subprocesses.
module ScriptDiscovery =

    let mutable private dslDllPath = ""

    /// Find the Zest.Dsl.dll path — looks in several common locations.
    let findDslDll () : string =
        if not (String.IsNullOrEmpty dslDllPath) && File.Exists dslDllPath then dslDllPath
        else
            let engineDir =
                let loc = System.Reflection.Assembly.GetExecutingAssembly().Location
                if not (String.IsNullOrEmpty loc) && File.Exists loc then
                    Path.GetDirectoryName loc
                else
                    AppContext.BaseDirectory
            let candidates = [
                Path.Combine(engineDir, "Zest.Dsl.dll")
                Path.Combine(engineDir, "..", "..", "..", "..", "..", "Zest.Dsl", "bin", "Release", "net10.0", "Zest.Dsl.dll")
                Path.Combine(engineDir, "..", "..", "..", "..", "..", "Zest.Dsl", "bin", "Debug", "net10.0", "Zest.Dsl.dll")
            ]
            let result =
                match candidates |> List.tryFind File.Exists with
                | Some p -> p
                | None ->
                    let rec searchUp (dir: string) =
                        let c1 = Path.Combine(dir, "Zest.Dsl.dll")
                        if File.Exists c1 then Some c1
                        else
                            let c2 = Path.Combine(dir, "src", "Zest.Dsl", "bin", "Release", "net10.0", "Zest.Dsl.dll")
                            if File.Exists c2 then Some c2
                            else
                                let c3 = Path.Combine(dir, "src", "Zest.Dsl", "bin", "Debug", "net10.0", "Zest.Dsl.dll")
                                if File.Exists c3 then Some c3
                                else
                                    let parent = Path.GetDirectoryName(dir)
                                    if String.IsNullOrEmpty parent || parent = dir then None
                                    else searchUp parent
                    match searchUp (Directory.GetCurrentDirectory()) with
                    | Some p -> p
                    | None -> failwithf "Zest.Dsl.dll not found. Engine dir: %s" engineDir
            dslDllPath <- result
            result

    /// Cached DLL isolation — copies Zest.Dsl.dll to a temp directory
    /// to avoid FSharp.Core version conflicts with FSI.
    let private dslDllCache = Dictionary<string, string>()

    let getIsolatedDslDll () : string =
        let srcPath = findDslDll ()
        let srcHash =
            use md5 = MD5.Create()
            use stream = File.OpenRead(srcPath)
            let hash = md5.ComputeHash(stream)
            Convert.ToHexString(hash)
        match dslDllCache.TryGetValue(srcHash) with
        | true, cachedPath -> cachedPath
        | false, _ ->
            let tempDir = Path.Combine(Path.GetTempPath(), "zest-dsl-" + srcHash)
            Directory.CreateDirectory(tempDir) |> ignore
            let destPath = Path.Combine(tempDir, "Zest.Dsl.dll")
            if not (File.Exists destPath) then
                File.Copy(srcPath, destPath, true)
            dslDllCache.[srcHash] <- destPath
            destPath
