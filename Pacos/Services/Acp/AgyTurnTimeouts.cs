namespace Pacos.Services.Acp;

/// <summary>
/// The three nested deadlines of one agent turn, derived from a single budget so they can
/// never be configured out of order. From the innermost outward:
/// <list type="number">
/// <item><see cref="Turn"/> — enforced by agy-acp itself (<c>AGY_ACP_TURN_TIMEOUT_SECS</c>): it
/// kills agy and reports <c>stopReason: "timeout"</c>, the only signal Pacos treats as an
/// exhausted budget.</item>
/// <item><see cref="CliBackstop"/> — agy's own <c>--print-timeout</c>. It must not undercut the
/// adapter deadline (its default of 5m would cut longer budgets short); it only bounds an agy
/// process that outlives a broken adapter.</item>
/// <item><see cref="Watchdog"/> — the Pacos <c>session/prompt</c> timeout, which fires only when
/// agy-acp itself stops responding. Besides its proportional margin it always leaves
/// <see cref="AdapterCleanupAllowance"/> after the turn deadline: agy-acp still has to kill agy,
/// drain its pipes and stream the result, and on a short budget a proportional margin alone
/// would not cover that, losing a reply agy-acp was about to deliver.</item>
/// </list>
/// Each tier is strictly larger than the one inside it, by at least one second.
/// </summary>
public sealed record AgyTurnTimeouts(TimeSpan Turn, TimeSpan CliBackstop, TimeSpan Watchdog)
{
    private const double CliBackstopMargin = 0.05;
    private const double WatchdogMargin = 0.15;

    /// <summary>
    /// Time agy-acp may spend after the turn deadline before replying. Must exceed its
    /// <c>PIPE_DRAIN_TIMEOUT</c> (5s, stdout and stderr drained concurrently) with headroom
    /// for killing agy, the final conversation poll and delivery.
    /// </summary>
    public static readonly TimeSpan AdapterCleanupAllowance = TimeSpan.FromSeconds(10);

    public static AgyTurnTimeouts FromBudget(int turnSeconds)
    {
        var backstopSeconds = turnSeconds + MarginSeconds(turnSeconds, CliBackstopMargin);
        var watchdogSeconds = Math.Max(
            turnSeconds + MarginSeconds(turnSeconds, WatchdogMargin),
            turnSeconds + (int)AdapterCleanupAllowance.TotalSeconds);
        watchdogSeconds = Math.Max(watchdogSeconds, backstopSeconds + 1);

        return new AgyTurnTimeouts(
            TimeSpan.FromSeconds(turnSeconds),
            TimeSpan.FromSeconds(backstopSeconds),
            TimeSpan.FromSeconds(watchdogSeconds));
    }

    private static int MarginSeconds(int turnSeconds, double fraction)
        => Math.Max(1, (int)Math.Ceiling(turnSeconds * fraction));
}
