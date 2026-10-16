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
/// The steering file is regenerated on every provisioning but keeps the session-start stamp
/// it was first written with (the stamp must not drift from turn to turn), so chats
/// provisioned by an earlier release pick up current instructions; an unchanged file is not
/// touched. The skills are rewritten — and skills no longer listed in <see cref="AgentSkills"/>
/// deleted — on every provisioning, so the catalog stays the single source of truth.
/// </summary>
public sealed class ChatWorkspaceProvisioner
{
    private const string SteeringFileName = "AGENTS.md";
    private const string SessionStartPrefix = "Дата начала текущей сессии: ";
    private const string SessionStartFormat = "yyyy-MM-dd HH:mm:ss";

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
        var skillsDirectory = Directory
            .CreateDirectory(Path.Combine(workingDirectory, SkillsRootDirectoryName, SkillsDirectoryName))
            .FullName;

        foreach (var skill in AgentSkills.All)
        {
            var skillDirectory = Path.Combine(skillsDirectory, skill.FolderName);
            Directory.CreateDirectory(skillDirectory);
            File.WriteAllText(Path.Combine(skillDirectory, SkillFileName), BuildSkillContent(skill), Encoding.UTF8);
        }

        RemoveStaleSkills(skillsDirectory);

        _logger.LogDebug("Provisioned {Count} agent skill(s) in {Path}", AgentSkills.All.Count, workingDirectory);
    }

    // agy discovers and activates every skill it finds on disk, so a skill dropped from or
    // renamed in AgentSkills.All would live on in chats provisioned by an earlier release.
    private void RemoveStaleSkills(string skillsDirectory)
    {
        var provisionedFolderNames = AgentSkills.All
            .Select(static skill => skill.FolderName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in Directory.EnumerateDirectories(skillsDirectory))
        {
            if (provisionedFolderNames.Contains(Path.GetFileName(directory)))
            {
                continue;
            }

            try
            {
                Directory.Delete(directory, recursive: true);
                _logger.LogInformation("Removed stale agent skill at {Path}", directory);
            }
            catch (IOException e)
            {
                _logger.LogWarning(e, "Failed to remove stale agent skill at {Path}", directory);
            }
            catch (UnauthorizedAccessException e)
            {
                _logger.LogWarning(e, "Failed to remove stale agent skill at {Path}", directory);
            }
        }
    }

    private void WriteSteeringFile(string workingDirectory, bool isGroupChat)
    {
        var steeringPath = Path.Combine(workingDirectory, SteeringFileName);
        var existingContent = File.Exists(steeringPath) ? File.ReadAllText(steeringPath, Encoding.UTF8) : null;
        var sessionStart = (existingContent is null ? null : ReadSessionStart(existingContent))
                           ?? _timeProvider.GetUtcNow().UtcDateTime.ToString(SessionStartFormat, CultureInfo.InvariantCulture);

        var content = BuildSteeringContent(isGroupChat, sessionStart);
        if (string.Equals(existingContent, content, StringComparison.Ordinal))
        {
            return;
        }

        File.WriteAllText(steeringPath, content, Encoding.UTF8);
        if (existingContent is null)
        {
            _logger.LogInformation("Wrote steering file at {Path}", steeringPath);
        }
        else
        {
            _logger.LogInformation("Refreshed steering file at {Path}", steeringPath);
        }
    }

    private static string? ReadSessionStart(string steeringContent)
    {
        var line = steeringContent
            .Split('\n')
            .Select(static line => line.TrimEnd('\r'))
            .LastOrDefault(static line => line.StartsWith(SessionStartPrefix, StringComparison.Ordinal));

        return line is null ? null : line[SessionStartPrefix.Length..].Trim();
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

    private static string BuildSteeringContent(bool isGroupChat, string sessionStart)
    {
        var chatRule = isGroupChat ? Const.GroupChatRuleSystemPrompt : Const.PersonalChatRuleSystemPrompt;

        return Const.SystemPrompt
               + Environment.NewLine + Environment.NewLine
               + chatRule
               + Environment.NewLine + Environment.NewLine
               + Const.FileDeliveryRuleSystemPrompt
               + Environment.NewLine + Environment.NewLine
               + BuildSkillsIndex()
               + Environment.NewLine + Environment.NewLine
               + SessionStartPrefix + sessionStart;
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
