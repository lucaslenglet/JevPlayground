using JevPlay.Jev;

namespace JevPlay.Providers;

/// <summary>
/// One way of querying a Jev model. All network mapping -- request shape,
/// authentication, response shape -- lives in the implementation, so fixing a
/// provider touches nothing else.
/// </summary>
public interface IJevProvider
{
    /// <summary>The name accepted by <c>--provider</c>.</summary>
    string Name { get; }

    string Model { get; }

    /// <summary>The URL being called, shown at startup and in the interface.</summary>
    string Endpoint { get; }

    /// <summary>True when no network call is made.</summary>
    bool IsOffline { get; }

    Task<JevReply> AskAsync(JevAsk ask, CancellationToken cancellationToken);
}
