using Pacos.Models;

namespace Pacos.Services.Acp;

/// <summary>
/// A per-turn scratch area underneath a chat's agy working directory. Files the
/// user attached are written into <see cref="InputDirectory"/>; files the agent
/// is asked to produce are collected from <see cref="OutputDirectory"/>. The
/// whole turn directory is deleted on disposal.
/// </summary>
public sealed class TempWorkspace : IDisposable
{
    private const string TurnsFolderName = ".turns";

    private TempWorkspace(string turnDirectory, string inputDirectory, string outputDirectory)
    {
        TurnDirectory = turnDirectory;
        InputDirectory = inputDirectory;
        OutputDirectory = outputDirectory;
    }

    public string TurnDirectory { get; }

    public string InputDirectory { get; }

    public string OutputDirectory { get; }

    public static TempWorkspace Create(string chatWorkingDir)
    {
        var turnId = Guid.NewGuid().ToString("N");
        var turnDirectory = Path.Combine(chatWorkingDir, TurnsFolderName, turnId);
        var inputDirectory = Path.Combine(turnDirectory, "input");
        var outputDirectory = Path.Combine(turnDirectory, "output");

        Directory.CreateDirectory(inputDirectory);
        Directory.CreateDirectory(outputDirectory);

        return new TempWorkspace(turnDirectory, inputDirectory, outputDirectory);
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
