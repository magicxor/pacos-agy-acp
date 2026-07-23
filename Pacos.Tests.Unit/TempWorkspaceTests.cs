using Pacos.Services.Acp;

namespace Pacos.Tests.Unit;

[TestFixture]
internal sealed class TempWorkspaceTests
{
    [Test]
    public void TempDirectory_IsScratchThatIsNeverCollectedAndIsDeletedOnDispose()
    {
        var chatWorkingDir = Path.Combine(Path.GetTempPath(), "pacos-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chatWorkingDir);

        try
        {
            string turnDirectory;
            IReadOnlyList<string> collectedNames;

            using (var workspace = TempWorkspace.Create(chatWorkingDir))
            {
                turnDirectory = workspace.TurnDirectory;

                Assert.Multiple(() =>
                {
                    Assert.That(Directory.Exists(workspace.TempDirectory), Is.True);
                    Assert.That(workspace.TempDirectory, Does.StartWith(workspace.TurnDirectory));
                    Assert.That(workspace.TempDirectory, Is.Not.EqualTo(workspace.OutputDirectory));
                });

                File.WriteAllText(Path.Combine(workspace.OutputDirectory, "delivered.txt"), "deliver me");
                File.WriteAllText(Path.Combine(workspace.TempDirectory, "scratch.bin"), "do not deliver");

                collectedNames = workspace.CollectOutputFiles().Select(file => file.FileName).ToList();
            }

            Assert.Multiple(() =>
            {
                // Only the output file is collected; the temp file is never sent to the user.
                Assert.That(collectedNames, Has.Count.EqualTo(1));
                Assert.That(collectedNames, Has.Member("delivered.txt"));
                Assert.That(collectedNames, Has.No.Member("scratch.bin"));
                // The whole turn directory (temp included) is removed on disposal.
                Assert.That(Directory.Exists(turnDirectory), Is.False);
            });
        }
        finally
        {
            if (Directory.Exists(chatWorkingDir))
            {
                Directory.Delete(chatWorkingDir, recursive: true);
            }
        }
    }
}
