namespace Pacos.Models;

public sealed record ChatInputFile(byte[] Bytes, string MimeType, ChatInputOrigin Origin);
