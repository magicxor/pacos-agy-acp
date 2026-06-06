namespace Pacos.Models;

public sealed record ChatResponseInfo(string Text, IReadOnlyCollection<OutputFile> Files);
