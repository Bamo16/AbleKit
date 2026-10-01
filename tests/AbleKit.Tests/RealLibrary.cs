namespace AbleKit.Tests;

/// <summary>The author's own library, which the real-data tests read when it is present.</summary>
internal static class RealLibrary
{
    internal const string StagingRoot = @"P:\RYAN\Ableton\Sample Staging";
    internal const string MashupRoot = @"P:\RYAN\Ableton\Mashup Samples";
    internal const string ProjectsRoot = @"P:\RYAN\Ableton\Projects";

    private static readonly string[] AudioExtensions = [".wav", ".flac", ".aif", ".aiff", ".mp3"];

    internal static bool HasStems(string root) =>
        Directory.Exists(root)
        && Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Any(file =>
                AudioExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
            );
}
