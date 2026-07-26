using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Pacos.Constants;
using Pacos.Services.GenerativeAi;

namespace Pacos.Tests.Unit;

[TestFixture]
internal sealed class ChatWorkspaceProvisionerTests
{
    private static readonly DateTimeOffset SessionStart = new(2026, 7, 26, 10, 30, 0, TimeSpan.Zero);
    private static readonly VerifySettings VerifySettings = new();

    private string _workingDirectory = string.Empty;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        VerifySettings.DisableDiff();
    }

    [SetUp]
    public void SetUp()
    {
        _workingDirectory = Path.Combine(Path.GetTempPath(), "pacos-tests", Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_workingDirectory))
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
    }

    [Test]
    public void Provision_ShouldWriteSteeringFileAndEverySkill()
    {
        CreateProvisioner().Provision(_workingDirectory, isGroupChat: true);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(SteeringFilePath()), Is.True);

            foreach (var skill in AgentSkills.All)
            {
                var content = File.ReadAllText(SkillFilePath(skill.FolderName));

                Assert.That(content, Does.StartWith("---"), skill.FolderName);
                Assert.That(content, Does.Contain($"name: {skill.FolderName}"), skill.FolderName);
                Assert.That(content, Does.Contain($"description: \"{skill.Description}\""), skill.FolderName);
                Assert.That(content, Does.Contain(skill.Body), skill.FolderName);
            }
        });
    }

    [Test]
    public void Provision_ShouldIndexSkillsInSteeringFileInsteadOfInliningTheirRules()
    {
        CreateProvisioner().Provision(_workingDirectory, isGroupChat: true);

        var steeringContent = File.ReadAllText(SteeringFilePath());

        Assert.Multiple(() =>
        {
            foreach (var skill in AgentSkills.All)
            {
                Assert.That(steeringContent, Does.Contain($".agents/skills/{skill.FolderName}/SKILL.md"), skill.FolderName);
            }

            // The tool instructions themselves must stay in the skills; keeping them in the
            // steering file would put them back into every single prompt.
            Assert.That(steeringContent, Does.Not.Contain("download_gallery"));
            Assert.That(steeringContent, Does.Not.Contain("create_chart"));
            Assert.That(steeringContent, Does.Not.Contain("outputDirectory"));
        });
    }

    [Test]
    public void Provision_WhenLegacySteeringFileExists_ShouldRemoveIt()
    {
        Directory.CreateDirectory(_workingDirectory);
        var legacyPath = Path.Combine(_workingDirectory, "GEMINI.md");
        File.WriteAllText(legacyPath, "legacy persona");

        CreateProvisioner().Provision(_workingDirectory, isGroupChat: false);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(legacyPath), Is.False);
            Assert.That(File.Exists(SteeringFilePath()), Is.True);
        });
    }

    [Test]
    public void Provision_WhenRerun_ShouldKeepSteeringFileButRefreshSkills()
    {
        var provisioner = CreateProvisioner();
        provisioner.Provision(_workingDirectory, isGroupChat: false);

        var skillPath = SkillFilePath(AgentSkills.All[0].FolderName);
        File.WriteAllText(SteeringFilePath(), "hand-edited steering file");
        File.WriteAllText(skillPath, "stale skill");

        provisioner.Provision(_workingDirectory, isGroupChat: false);

        Assert.Multiple(() =>
        {
            // The steering file ends with the session start stamp, so it must not be rewritten.
            Assert.That(File.ReadAllText(SteeringFilePath()), Is.EqualTo("hand-edited steering file"));
            Assert.That(File.ReadAllText(skillPath), Does.Contain(AgentSkills.All[0].Body));
        });
    }

    [Test]
    public async Task Provision_ShouldWriteExpectedSteeringFile()
    {
        CreateProvisioner().Provision(_workingDirectory, isGroupChat: true);

        var steeringContent = await File.ReadAllTextAsync(SteeringFilePath());

        await Verify(Normalize(steeringContent), VerifySettings);
    }

    [TestCase("gallery-download")]
    [TestCase("web-crawling")]
    [TestCase("chart-generation")]
    public async Task Provision_ShouldWriteExpectedSkillFile(string folderName)
    {
        CreateProvisioner().Provision(_workingDirectory, isGroupChat: true);

        var skillContent = await File.ReadAllTextAsync(SkillFilePath(folderName));

        await Verify(Normalize(skillContent), VerifySettings).UseParameters(folderName);
    }

    private static ChatWorkspaceProvisioner CreateProvisioner()
    {
        return new ChatWorkspaceProvisioner(
            NullLogger<ChatWorkspaceProvisioner>.Instance,
            new FakeTimeProvider(SessionStart));
    }

    // The prompt text comes from raw string literals, so it carries the line endings of the
    // checked-out sources; normalize so the snapshots match on Windows and on Linux CI alike.
    private static string Normalize(string content) => content.ReplaceLineEndings("\n");

    private string SteeringFilePath() => Path.Combine(_workingDirectory, "AGENTS.md");

    private string SkillFilePath(string folderName) =>
        Path.Combine(_workingDirectory, ".agents", "skills", folderName, "SKILL.md");
}
