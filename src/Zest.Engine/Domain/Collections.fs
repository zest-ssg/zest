namespace Zest.Engine

/// <summary>
/// A named collection of pages (like 11ty collections).
/// </summary>
type Collection = {
    Name: string
    Pages: Page list
    Type: CollectionType
}
and CollectionType =
    | Directory
    | Tag
    | Category
