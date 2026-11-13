using System.Runtime.CompilerServices;

// The deserialization-retry delay is internal so the DI container keeps a single unambiguous
// constructor; tests shorten it to assert retry behaviour without real backoff.
[assembly: InternalsVisibleTo("Delobytes.App.Backend.Integrations.Tests")]

// The Ozon order mapping result is internal; the main test project pins the identifier chain
// between the products and orders imports, which needs the mapper's output directly. It lives
// there, not in Integrations.Tests, because the Sales-side half of that chain is reachable only
// from the project that references the host and therefore every module.
[assembly: InternalsVisibleTo("Delobytes.App.Backend.Tests")]
