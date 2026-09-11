namespace Zest.Engine.Build

open System
open System.IO

// ============================================================
// AtomicFile — lock-safe file replacement for build outputs
// ============================================================
// The preview/dev server streams output files to browsers while a
// rebuild may replace them. A plain FileMode.Create write fails with
// "being used by another process" whenever a reader holds the file
// open without sharing delete access. Writing to a temp file and then
// renaming it over the target lets in-flight readers finish on the old
// file while new requests see the new one; a short retry absorbs
// transient locks from antivirus scanners and editors.
//
// Dependencies: System.IO
// ============================================================

/// Lock-safe atomic file replacement for build outputs.
module AtomicFile =

    let private retryDelayMs = 50
    let private maxAttempts = 8

    /// Write the temp file and close the stream before returning. The handle
    /// must be released before File.Move: an open FILE_SHARE_READ handle
    /// denies the DELETE access the rename requests — even the caller's own
    /// handle — so a live stream makes the move fail with a spurious
    /// "being used by another process".
    let private writeTempFile (tmp: string) (bytes: byte[]) : unit =
        use fs = new FileStream(tmp, FileMode.Create, FileAccess.Write,
                                FileShare.Read, 8192, FileOptions.SequentialScan)
        fs.Write(bytes, 0, bytes.Length)
        fs.Flush(flushToDisk = true)

    /// Replace the file at <paramref name="path"/> with <paramref name="bytes"/>.
    /// Writes to a unique sibling temp file first, then moves it into place with
    /// a bounded retry so transient file locks never abort a rebuild. The unique
    /// temp name also lets concurrent writers targeting the same path (duplicate
    /// output URLs) finish without clashing on a shared scratch file.
    let write (path: string) (bytes: byte[]) : unit =
        let dir = Path.GetDirectoryName path
        if not (String.IsNullOrEmpty dir) then Directory.CreateDirectory dir |> ignore
        let tmp = sprintf "%s.zest-tmp-%s" path (Guid.NewGuid().ToString("N"))
        let mutable moved = false
        let mutable attempt = 0
        while not moved && attempt < maxAttempts do
            try
                writeTempFile tmp bytes
                File.Move(tmp, path, overwrite = true)
                moved <- true
            with
            | :? IOException when attempt + 1 < maxAttempts ->
                // Target is transiently locked by a concurrent reader; retry
                // after a short pause instead of failing the whole build.
                attempt <- attempt + 1
                Threading.Thread.Sleep retryDelayMs
            | _ ->
                attempt <- maxAttempts
                try if File.Exists tmp then File.Delete tmp with _ -> ()
                reraise ()
        if moved then
            try if File.Exists tmp then File.Delete tmp with _ -> ()
