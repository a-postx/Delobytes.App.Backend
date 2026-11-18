using System.Runtime.CompilerServices;

// The read queries are asserted through ToQueryString: a translation that silently falls back to
// client evaluation would change the keyset order and the round trips without failing a test.
[assembly: InternalsVisibleTo("Delobytes.App.Backend.Tests")]
