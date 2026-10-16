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
        // VerifySettings.DisableDiff();
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

        using (Assert.EnterMultipleScope())
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
        }
    }

    [Test]
    public void Provision_ShouldIndexSkillsInSteeringFileInsteadOfInliningTheirRules()
    {
        CreateProvisioner().Provision(_workingDirectory, isGroupChat: true);

        var steeringContent = File.ReadAllText(SteeringFilePath());

        using (Assert.EnterMultipleScope())
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
        }
    }

    [Test]
    public void Provision_WhenLegacySteeringFileExists_ShouldRemoveIt()
    {
        Directory.CreateDirectory(_workingDirectory);
        var legacyPath = Path.Combine(_workingDirectory, "GEMINI.md");
        File.WriteAllText(legacyPath, "legacy persona");

        CreateProvisioner().Provision(_workingDirectory, isGroupChat: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(legacyPath), Is.False);
            Assert.That(File.Exists(SteeringFilePath()), Is.True);
        }
    }

    [Test]
    public void Provision_WhenRerun_ShouldKeepUpToDateSteeringFileAndRefreshSkills()
    {
        var timeProvider = new FakeTimeProvider(SessionStart);
        var provisioner = CreateProvisioner(timeProvider);
        provisioner.Provision(_workingDirectory, isGroupChat: false);
        var originalSteering = File.ReadAllText(SteeringFilePath());

        var skillPath = SkillFilePath(AgentSkills.All[0].FolderName);
        File.WriteAllText(skillPath, "stale skill");
        timeProvider.Advance(TimeSpan.FromHours(5));

        provisioner.Provision(_workingDirectory, isGroupChat: false);

        using (Assert.EnterMultipleScope())
        {
            // The steering file ends with the session start stamp, so a later turn must not move it.
            Assert.That(File.ReadAllText(SteeringFilePath()), Is.EqualTo(originalSteering));
            Assert.That(File.ReadAllText(skillPath), Does.Contain(AgentSkills.All[0].Body));
        }
    }

    [Test]
    public void Provision_WhenSteeringFileIsFromAnEarlierRelease_ShouldRefreshItButKeepTheSessionStart()
    {
        Directory.CreateDirectory(_workingDirectory);
        File.WriteAllText(
            SteeringFilePath(),
            "# РОЛЬ\n- instructions from an earlier release\n\nДата начала текущей сессии: 2026-01-02 03:04:05");

        CreateProvisioner().Provision(_workingDirectory, isGroupChat: false);

        var steeringContent = File.ReadAllText(SteeringFilePath());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(steeringContent, Does.StartWith(Const.SystemPrompt));
            Assert.That(steeringContent, Does.Not.Contain("instructions from an earlier release"));
            Assert.That(steeringContent, Does.EndWith("Дата начала текущей сессии: 2026-01-02 03:04:05"));
        }
    }

    [Test]
    public void Provision_WhenSteeringFileHasNoSessionStart_ShouldStampTheCurrentTime()
    {
        Directory.CreateDirectory(_workingDirectory);
        File.WriteAllText(SteeringFilePath(), "legacy steering file without a stamp");

        CreateProvisioner().Provision(_workingDirectory, isGroupChat: false);

        Assert.That(
            File.ReadAllText(SteeringFilePath()),
            Does.EndWith("Дата начала текущей сессии: 2026-07-26 10:30:00"));
    }

    [Test]
    public void Provision_WhenSkillWasRemovedFromCatalog_ShouldDeleteItsDirectory()
    {
        var provisioner = CreateProvisioner();
        provisioner.Provision(_workingDirectory, isGroupChat: false);

        var staleDirectory = SkillDirectoryPath("retired-skill");
        Directory.CreateDirectory(staleDirectory);
        File.WriteAllText(Path.Combine(staleDirectory, "SKILL.md"), "skill from an earlier release");

        provisioner.Provision(_workingDirectory, isGroupChat: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(staleDirectory), Is.False);

            foreach (var skill in AgentSkills.All)
            {
                Assert.That(File.Exists(SkillFilePath(skill.FolderName)), Is.True, skill.FolderName);
            }
        }
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
    [TestCase("link-summary")]
    [TestCase("chart-generation")]
    [TestCase("geo-maps")]
    [TestCase("movie-releases")]
    public async Task Provision_ShouldWriteExpectedSkillFile(string folderName)
    {
        CreateProvisioner().Provision(_workingDirectory, isGroupChat: true);

        var skillContent = await File.ReadAllTextAsync(SkillFilePath(folderName));

        await Verify(Normalize(skillContent), VerifySettings).UseParameters(folderName);
    }

    private static ChatWorkspaceProvisioner CreateProvisioner(TimeProvider? timeProvider = null)
    {
        return new ChatWorkspaceProvisioner(
            NullLogger<ChatWorkspaceProvisioner>.Instance,
            timeProvider ?? new FakeTimeProvider(SessionStart));
    }

    // The prompt text comes from raw string literals, so it carries the line endings of the
    // checked-out sources; normalize so the snapshots match on Windows and on Linux CI alike.
    // The example paths in the prompts are rooted at "/tmp/", which on Linux is also
    // Path.GetTempPath(): Verify would scrub it to "{TempPath}" there and leave it alone on
    // Windows, so apply that very replacement up front on every platform.
    private static string Normalize(string content) => content
        .ReplaceLineEndings("\n")
        .Replace("/tmp/", "{TempPath}", StringComparison.Ordinal);

    private string SteeringFilePath() => Path.Combine(_workingDirectory, "AGENTS.md");

    private string SkillDirectoryPath(string folderName) =>
        Path.Combine(_workingDirectory, ".agents", "skills", folderName);

    private string SkillFilePath(string folderName) =>
        Path.Combine(SkillDirectoryPath(folderName), "SKILL.md");
}
