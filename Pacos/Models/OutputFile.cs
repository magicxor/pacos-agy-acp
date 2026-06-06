namespace Pacos.Models;

/// <summary>
/// A file produced by the agent during a turn, materialised in memory so it can
/// be delivered to the user after the temporary turn directory is removed.
/// </summary>
public sealed record OutputFile(string FileName, byte[] Content);
