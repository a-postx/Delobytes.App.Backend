using System.Runtime.CompilerServices;

// The deserialization-retry delay is internal so the DI container keeps a single unambiguous
// constructor; tests shorten it to assert retry behaviour without real backoff.
[assembly: InternalsVisibleTo("Delobytes.App.Backend.Integrations.Tests")]
