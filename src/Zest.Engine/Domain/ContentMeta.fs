namespace Zest.Engine

open System

/// Page frontmatter metadata, parsed from three sources (in priority order):
///   1. TOML front matter between +++ delimiters
///   2. HTML comments (<!-- @key value -->) in file headers
///   3. F# comments (// @key value) in file headers
type ContentMeta = {
    // ── Standard frontmatter fields ──────────────────────────
    /// Page layout name (without extension), e.g. "default"
    Layout:      string option
    /// Page title
    Title:       string option
    /// Custom permalink override (e.g. "/about/")
    Permalink:   string option
    /// Content tags for classification and filtering
    Tags:        string list
    /// Publication date
    Date:        DateTime option
    /// SEO / social description
    Description: string option
    /// Draft status: true = skip during production build
    Draft:       bool
    /// Author attribution
    Author:      string option
    /// Last modification date (parsed from TOML "updated" key)
    Updated:     DateTime option
    /// Sort weight — lower values sort first
    Weight:      int option
    /// Explicit template override
    Template:    string option
    /// Collection / group name for multi-collection sites
    Collection:  string option
    /// All unrecognised keys land here for forward compatibility
    Extra:       Map<string, string>
}

module ContentMeta =
    let empty = {
        Layout      = None
        Title       = None
        Permalink   = None
        Tags        = []
        Date        = None
        Description = None
        Draft       = false
        Author      = None
        Updated     = None
        Weight      = None
        Template    = None
        Collection  = None
        Extra       = Map.empty
    }
