using Pacos.Services.Acp;

namespace Pacos.Tests.Unit;

[TestFixture]
internal sealed class AcpExceptionTests
{
    [TestCase(
        "agy-acp error -32000: agy failed: Error: Agent execution terminated due to error. "
        + "(cause: RESOURCE_EXHAUSTED (code 429): Resource has been exhausted (e.g. check quota).)")]
    [TestCase(
        "agy-acp error -32603: agent executor error: model unreachable: RESOURCE_EXHAUSTED (code 429): "
        + "Individual quota reached. Please upgrade your subscription to increase your limits. Resets in 40h52m46s.")]
    [TestCase("agy-acp error -32603: Individual quota reached.")]
    public void IsQuotaError_ReturnsTrue_ForQuotaErrors(string message)
    {
        var exception = new AcpException(message);

        Assert.That(exception.IsQuotaError, Is.True);
    }

    [TestCase("agy-acp error -32000: agy failed: Error: Agent execution terminated due to error.")]
    [TestCase("agy-acp error -32000: agy exited with status: exit status: 1")]
    [TestCase("agy-acp process exited")]
    [TestCase("session/new did not return a sessionId")]
    public void IsQuotaError_ReturnsFalse_ForOtherErrors(string message)
    {
        var exception = new AcpException(message);

        Assert.That(exception.IsQuotaError, Is.False);
    }
}
