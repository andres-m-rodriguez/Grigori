using Grigori.Reviews.Contracts;

namespace Grigori.Reviews.Application;

/// <summary>
/// The outbound port — what a new integration implements to plug into Reviews.
/// </summary>
/// <remarks>
/// <para>
/// This is the half Reviews calls. Registering an implementation is what makes an
/// <see cref="Origin"/> actionable: Reviews routes on <see cref="Name"/>, so a Review whose
/// Origin is <c>github:…</c> is served by the integration that answers <c>"github"</c>, and a
/// Review with no Origin is served by nobody and simply never leaves Grigori.
/// </para>
/// <para>
/// It carries only <see cref="Name"/> today because Grigori has nothing to send yet. The
/// write operations — post a note, submit a verdict, merge — join this interface in the phase
/// that introduces Intents, and they land here rather than in a new abstraction.
/// </para>
/// </remarks>
public interface IReviewIntegration
{
    /// <summary>
    /// Matches <see cref="Origin.Integration"/>. Lowercase, stable, and part of the wire format
    /// agents see — renaming it invalidates every stored Origin.
    /// </summary>
    string Name { get; }
}
