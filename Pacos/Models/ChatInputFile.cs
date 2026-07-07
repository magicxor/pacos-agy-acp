namespace Pacos.Models;

public enum ChatInputOrigin
{
    UserMessage,     // медиа из сообщения самого пользователя
    RepliedMessage,  // медиа из поста, на который он отвечает
}

public sealed record ChatInputFile(byte[] Bytes, string MimeType, ChatInputOrigin Origin);
