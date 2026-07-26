namespace Pacos.Models;

/// <summary>
/// An agy Agent Skill provisioned into a chat workspace as
/// <c>.agents/skills/{FolderName}/SKILL.md</c>. agy discovers those files at the
/// session working directory and pulls the <see cref="Body"/> into context only
/// once <see cref="Description"/> matches the user's intent, so bulky tool
/// instructions no longer weigh on every turn.
/// </summary>
public sealed record AgentSkill
{
    /// <summary>Directory name under <c>.agents/skills</c>; doubles as the frontmatter <c>name</c>.</summary>
    public required string FolderName { get; init; }

    /// <summary>Short label listed in the steering file's skill index.</summary>
    public required string Title { get; init; }

    /// <summary>
    /// Frontmatter <c>description</c>: what the skill does and when to use it — this is what agy
    /// matches the user's intent against. Must stay a single line free of double quotes.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>Markdown body of the generated <c>SKILL.md</c>, appended after the frontmatter.</summary>
    public required string Body { get; init; }
}
