using Pacos.Models;

namespace Pacos.Services.Acp;

/// <summary>
/// A per-turn scratch area underneath a chat's agy working directory. Files the
/// user attached are written into <see cref="InputDirectory"/>; files the agent
/// is asked to deliver are collected from <see cref="OutputDirectory"/> and sent to
/// the user; <see cref="TempDirectory"/> is agent scratch space (e.g. something it
/// downloaded only to read) that is never collected or sent. The whole turn
/// directory — input, output and temp alike — is deleted on disposal.
/// </summary>
public sealed class TempWorkspace : IDisposable
{
    private const string TurnsFolderName = ".turns";

    private TempWorkspace(string turnDirectory, string inputDirectory, string outputDirectory, string tempDirectory)
    {
        TurnDirectory = turnDirectory;
        InputDirectory = inputDirectory;
        OutputDirectory = outputDirectory;
        TempDirectory = tempDirectory;
    }

    public string TurnDirectory { get; }

    public string InputDirectory { get; }

    public string OutputDirectory { get; }

    /// <summary>
    /// Agent scratch space for files it needs during the turn but that must NOT be
    /// delivered to the user (e.g. a page it downloaded only to read). Never collected
    /// by <see cref="CollectOutputFiles"/>; removed with the rest of the turn on disposal.
    /// </summary>
    public string TempDirectory { get; }

    public static TempWorkspace Create(string chatWorkingDir)
    {
        var turnId = Guid.NewGuid().ToString("N");
        var turnDirectory = Path.Combine(chatWorkingDir, TurnsFolderName, turnId);
        var inputDirectory = Path.Combine(turnDirectory, "input");
        var outputDirectory = Path.Combine(turnDirectory, "output");
        var tempDirectory = Path.Combine(turnDirectory, "temp");

        Directory.CreateDirectory(inputDirectory);
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(tempDirectory);

        return new TempWorkspace(turnDirectory, inputDirectory, outputDirectory, tempDirectory);
    }

    /// <summary>
    /// Writes an attached file into the input directory and returns its full path.
    /// </summary>
    public string WriteInputFile(byte[] content, string fileName)
    {
        var safeName = SanitizeFileName(fileName);
        var path = Path.Combine(InputDirectory, safeName);
        File.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>
    /// Reads every file the agent placed in the output directory into memory.
    /// </summary>
    public IReadOnlyList<OutputFile> CollectOutputFiles()
    {
        if (!Directory.Exists(OutputDirectory))
        {
            return [];
        }

        var files = new List<OutputFile>();
        foreach (var path in Directory.EnumerateFiles(OutputDirectory, "*", SearchOption.AllDirectories))
        {
            files.Add(new OutputFile(Path.GetFileName(path), File.ReadAllBytes(path)));
        }

        return files;
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return "attachment";
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(TurnDirectory))
            {
                Directory.Delete(TurnDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; leftover scratch files are harmless.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup; leftover scratch files are harmless.
        }
    }
}
