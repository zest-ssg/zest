namespace Zest.Compiler.Build
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

    /// Write the temp file and close the stream before returning.
    ///
    /// The handle must be released before File.Move: an open FILE_SHARE_READ
    /// handle denies the DELETE access the rename requests — even the caller's
    /// own handle — so a live stream makes the move fail with a spurious
    /// "being used by another process".
    ///
    /// Flushing is buffered, not flushed to disk. Build output is regenerable
    /// and the rename is what makes it visible, so paying for an fsync per file
    /// only made large sites slower — a site with 500 pages paid 500 fsyncs.
    let private writeTempFile (tmp: string) (bytes: byte[]) : unit =
        use fs = new FileStream(tmp, FileMode.Create, FileAccess.Write,
                                FileShare.Read, 8192, FileOptions.SequentialScan)
        fs.Write(bytes, 0, bytes.Length)

    /// Replace the file at <paramref name="path"/> with <paramref name="bytes"/>.
    ///
    /// Writes to a unique sibling temp file first, then moves it into place with
    /// a bounded retry so transient file locks never abort a rebuild. The unique
    /// temp name also lets concurrent writers targeting the same path (duplicate
    /// output URLs) finish without clashing on a shared scratch file.
    let write (path: string) (bytes: byte[]) : unit =
        let dir = Path.GetDirectoryName path
        if not (String.IsNullOrEmpty dir) then Directory.CreateDirectory dir |> ignore
        let tmp = sprintf "%s.zest-tmp-%s" path (Guid.NewGuid().ToString("N"))
        // The temp file is deleted on every non-retryable exit, including the
        // rethrow. The successful path needs no cleanup: the move consumed it.
        let rec attempt (n: int) =
            try
                writeTempFile tmp bytes
                File.Move(tmp, path, overwrite = true)
            with
            | :? IOException when n + 1 < maxAttempts ->
                // Target is transiently locked by a concurrent reader; retry
                // after a short pause instead of failing the whole build.
                Threading.Thread.Sleep retryDelayMs
                attempt (n + 1)
            | _ ->
                try if File.Exists tmp then File.Delete tmp with _ -> ()
                reraise ()
        attempt 0
