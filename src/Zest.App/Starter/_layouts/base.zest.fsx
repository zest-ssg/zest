// @layout default

// ---------------------------------------------------------------------------
// base.zest.fsx - an alias for default.zest.fsx.
//
// The engine's taxonomy generator hard-codes "base" as the layout of the tag
// index and tag pages it emits, so the name has to resolve to something; it
// renders exactly what default.zest.fsx renders, and content files may name
// either one.
//
// A layout that only forwards `content` to its parent is the simplest way to
// keep one real implementation while honouring a name the engine expects.
// ---------------------------------------------------------------------------

printfn "%s" content
