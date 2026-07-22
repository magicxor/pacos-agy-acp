namespace Pacos.Models;

/// <summary>
/// A single output file destined for the media album, tagged with the kind of
/// send to use and whether it must be covered with a spoiler.
/// </summary>
internal sealed record PlannedMedia(OutputFile File, OutputMediaKind Kind, bool HasSpoiler);
