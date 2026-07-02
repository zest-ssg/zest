namespace Zest.Engine

/// <summary>
/// A named collection of pages (like 11ty collections).
/// </summary>
type PageGroup = {
    Name: string
    Pages: ContentPage list
    Type: GroupType
}
and GroupType =
    | Directory
    | Tag
    | Category
