using System.Globalization;
using System.Text;
using Pacos.Constants;
using Pacos.Models;

namespace Pacos.Services.GenerativeAi;

/// <summary>
/// Materializes the per-chat agy workspace: the <c>AGENTS.md</c> steering file agy parses
/// at the session working directory, and the Agent Skills under <c>.agents/skills</c> that
/// agy discovers there and activates on demand from their frontmatter description.
///
/// The steering file is written once (its trailing session-start stamp must not drift from
/// turn to turn), while the skill files are rewritten on every provisioning so edits to
/// <see cref="AgentSkills"/> also reach chats provisioned by an earlier release.
/// </summary>
public sealed class ChatWorkspaceProvisioner
{
    private const string SteeringFileName = "AGENTS.md";

    // Steering file name used before the switch to the harness-agnostic AGENTS.md. agy reads
    // both, so a leftover copy would feed the persona to the model twice.
    private const string LegacySteeringFileName = "GEMINI.md";

    private const string SkillsRootDirectoryName = ".agents";
    private const string SkillsDirectoryName = "skills";
    private const string SkillFileName = "SKILL.md";
    private const string SkillsRelativePath = $"{SkillsRootDirectoryName}/{SkillsDirectoryName}";

    private readonly ILogger<ChatWorkspaceProvisioner> _logger;
    private readonly TimeProvider _timeProvider;

    public ChatWorkspaceProvisioner(
        ILogger<ChatWorkspaceProvisioner> logger,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public void Provision(string workingDirectory, bool isGroupChat)
    {
        Directory.CreateDirectory(workingDirectory);

        RemoveLegacySteeringFile(workingDirectory);
        WriteSkills(workingDirectory);
        WriteSteeringFile(workingDirectory, isGroupChat);
    }

    private void RemoveLegacySteeringFile(string workingDirectory)
    {
        var legacyPath = Path.Combine(workingDirectory, LegacySteeringFileName);
        if (!File.Exists(legacyPath))
        {
            return;
        }

        try
        {
            File.Delete(legacyPath);
            _logger.LogInformation("Removed legacy steering file at {Path}", legacyPath);
        }
        catch (IOException e)
        {
            _logger.LogWarning(e, "Failed to remove legacy steering file at {Path}", legacyPath);
        }
        catch (UnauthorizedAccessException e)
        {
            _logger.LogWarning(e, "Failed to remove legacy steering file at {Path}", legacyPath);
        }
    }

    private void WriteSkills(string workingDirectory)
    {
        foreach (var skill in AgentSkills.All)
        {
            var skillDirectory = Path.Combine(workingDirectory, SkillsRootDirectoryName, SkillsDirectoryName, skill.FolderName);
            Directory.CreateDirectory(skillDirectory);
            File.WriteAllText(Path.Combine(skillDirectory, SkillFileName), BuildSkillContent(skill), Encoding.UTF8);
        }

        _logger.LogDebug("Provisioned {Count} agent skill(s) in {Path}", AgentSkills.All.Count, workingDirectory);
    }

    private void WriteSteeringFile(string workingDirectory, bool isGroupChat)
    {
        var steeringPath = Path.Combine(workingDirectory, SteeringFileName);
        if (File.Exists(steeringPath))
        {
            return;
        }

        File.WriteAllText(steeringPath, BuildSteeringContent(isGroupChat), Encoding.UTF8);
        _logger.LogInformation("Wrote steering file at {Path}", steeringPath);
    }

    private static string BuildSkillContent(AgentSkill skill)
    {
        return $"""
            ---
            name: {skill.FolderName}
            description: "{skill.Description}"
            ---

            {skill.Body}

            """;
    }

    private string BuildSteeringContent(bool isGroupChat)
    {
        var chatRule = isGroupChat ? Const.GroupChatRuleSystemPrompt : Const.PersonalChatRuleSystemPrompt;
        var sessionStart = _timeProvider.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        return Const.SystemPrompt
               + Environment.NewLine + Environment.NewLine
               + chatRule
               + Environment.NewLine + Environment.NewLine
               + Const.FileDeliveryRuleSystemPrompt
               + Environment.NewLine + Environment.NewLine
               + BuildSkillsIndex()
               + Environment.NewLine + Environment.NewLine
               + $"Дата начала текущей сессии: {sessionStart}";
    }

    private static string BuildSkillsIndex()
    {
        var builder = new StringBuilder(Const.SkillsRuleSystemPromptHeader);

        foreach (var skill in AgentSkills.All)
        {
            builder
                .Append(Environment.NewLine)
                .Append("- ")
                .Append(skill.FolderName)
                .Append(" — ")
                .Append(skill.Title)
                .Append(" (")
                .Append(SkillsRelativePath)
                .Append('/')
                .Append(skill.FolderName)
                .Append('/')
                .Append(SkillFileName)
                .Append(')');
        }

        return builder.ToString();
    }
}
