using Pacos.Services.ChatCommandHandlers;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class DrawHandlerTests
{
    private const string Suffix = " Обязательно сохрани результат как файл изображения в выходную директорию.";

    [Test]
    public void BuildDrawMessage_WhenPromptProvided_ShouldUsePrompt()
    {
        var result = DrawHandler.BuildDrawMessage("user", "кот в сапогах", string.Empty, 0);
        Assert.That(result, Is.EqualTo("user: Сгенерируй изображение по запросу: кот в сапогах." + Suffix));
    }

    [Test]
    public void BuildDrawMessage_WhenPromptEmptyAndRepliedTextPresent_ShouldVisualizeRepliedMessage()
    {
        var result = DrawHandler.BuildDrawMessage("user", string.Empty, "сегодня дождь", 0);
        Assert.That(result, Is.EqualTo("user: Сгенерируй изображение, визуализирующее следующее сообщение: сегодня дождь." + Suffix));
    }

    [Test]
    public void BuildDrawMessage_WhenPromptAndRepliedTextPresent_ShouldPreferPrompt()
    {
        var result = DrawHandler.BuildDrawMessage("user", "пчёлка", "сегодня дождь", 1);
        Assert.That(result, Is.EqualTo("user: Используя прикреплённое изображение как основу, сгенерируй новое изображение по запросу: пчёлка." + Suffix));
    }

    [Test]
    public void BuildDrawMessage_WhenPromptEmptyAndRepliedTextPresentWithImages_ShouldVisualizeRepliedMessage()
    {
        var result = DrawHandler.BuildDrawMessage("user", string.Empty, "сегодня дождь", 2);
        Assert.That(result, Is.EqualTo("user: Используя прикреплённые изображения как основу, сгенерируй новое изображение, визуализирующее следующее сообщение: сегодня дождь." + Suffix));
    }

    [Test]
    public void BuildDrawMessage_WhenPromptAndRepliedTextEmpty_ShouldUseBasisOnly()
    {
        var result = DrawHandler.BuildDrawMessage("user", string.Empty, string.Empty, 1);
        Assert.That(result, Is.EqualTo("user: Используя прикреплённое изображение как основу, сгенерируй новое изображение." + Suffix));
    }
}
