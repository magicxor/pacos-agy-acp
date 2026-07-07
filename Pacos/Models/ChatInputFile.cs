namespace Pacos.Models;

public enum ChatInputOrigin
{
    UserMessage,     // media from the user's own message
    RepliedMessage,  // media from the post the user is replying to
}

public sealed record ChatInputFile(byte[] Bytes, string MimeType, ChatInputOrigin Origin);
