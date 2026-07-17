using Xunit;

// ============================================================
// TestCollections — Shared xUnit collection definitions.
// Sequential: for tests that modify Environment.CurrentDirectory
// or other process-wide mutable state.
// ============================================================

namespace Zest.App.Tests;

[CollectionDefinition("Sequential")]
public class SequentialTestCollection
{
    // Marker class only — no tests here.
}
