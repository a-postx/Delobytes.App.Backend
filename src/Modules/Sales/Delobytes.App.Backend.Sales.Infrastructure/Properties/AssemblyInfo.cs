using System.Runtime.CompilerServices;

// The channel-status projection is a vocabulary table that has to be asserted key by key;
// driving it only through the consumer would require a full batch round trip per synonym.
[assembly: InternalsVisibleTo("Delobytes.App.Backend.Tests")]
