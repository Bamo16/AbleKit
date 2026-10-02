namespace AbleKit.Tests;

/// <summary>
/// A real Ableton library on the developer's machine, which the real-data tests read when
/// environment variables point at it and skip without: <c>ABLEKIT_SAMPLE_FOLDERS</c>, folders Live
/// has analysed and tagged separated by <c>;</c>, and <c>ABLEKIT_PROJECTS_FOLDER</c>, a folder of sets.
/// </summary>
internal static class RealLibrary
{
    private const string SampleFoldersVariable = "ABLEKIT_SAMPLE_FOLDERS";
    private const string ProjectsFolderVariable = "ABLEKIT_PROJECTS_FOLDER";

    private static readonly string[] AudioExtensions = [".wav", ".flac", ".aif", ".aiff", ".mp3"];

    private static readonly IReadOnlyList<string> Folders =
    [
        .. (Environment.GetEnvironmentVariable(SampleFoldersVariable) ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Directory.Exists),
    ];

    private static readonly string? Projects =
        Environment.GetEnvironmentVariable(ProjectsFolderVariable) is { } folder
        && Directory.Exists(folder)
            ? folder
            : null;

    /// <summary>The sample folders configured, skipping the calling test when there are none.</summary>
    public static IReadOnlyList<string> SampleFolders()
    {
        Assert.SkipWhen(Folders.Count is 0, $"Set {SampleFoldersVariable} to test a real library.");

        return Folders;
    }

    /// <summary>The projects folder configured, skipping the calling test when there is none.</summary>
    public static string ProjectsFolder()
    {
        Assert.SkipWhen(Projects is null, $"Set {ProjectsFolderVariable} to test real sets.");

        return Projects;
    }

    /// <summary>Every audio file under the sample folders.</summary>
    public static IEnumerable<string> AudioFiles() =>
        SampleFolders()
            .SelectMany(folder =>
                Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            )
            .Where(file =>
                AudioExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
            );
}
