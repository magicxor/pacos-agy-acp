namespace Pacos.Services.Acp;

/// <summary>
/// Outcome of one <c>session/prompt</c> turn: the agent text streamed during the turn and
/// whether agy-acp ended the turn at its deadline (<c>stopReason: "timeout"</c>). A timed-out
/// turn may still carry partial text or have written output files, so the caller decides
/// whether anything usable was produced.
/// </summary>
public sealed record AcpPromptResult(string Text, bool TimedOut);
