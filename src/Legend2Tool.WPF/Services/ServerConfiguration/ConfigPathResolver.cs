using System.IO;

namespace Legend2Tool.WPF.Services.ServerConfiguration;

public static class ConfigPathResolver
{
    public static string ResolveServerPath(
        string serverDirectory,
        string? configuredPath,
        string anchorDirectory
    )
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return string.Empty;

        string normalized = configuredPath.Replace('/', '\\');
        string[] segments = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        int anchorIndex = Array.FindIndex(
            segments,
            segment => string.Equals(
                segment,
                anchorDirectory,
                StringComparison.OrdinalIgnoreCase
            )
        );

        if (anchorIndex >= 0)
            return Path.GetFullPath(
                Path.Combine(serverDirectory, Path.Combine(segments[anchorIndex..]))
            );

        return Path.GetFullPath(
            Path.IsPathRooted(normalized)
                ? normalized
                : Path.Combine(serverDirectory, normalized)
        );
    }

    public static string ResolveBackListPath(string serverDirectory, string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return string.Empty;

        string normalized = configuredPath.Replace('/', '\\');
        string[] segments = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        int rootIndex = Array.FindIndex(
            segments,
            segment => string.Equals(segment, "mirserver", StringComparison.OrdinalIgnoreCase)
        );
        if (rootIndex >= 0 && rootIndex + 1 < segments.Length)
            return Path.GetFullPath(
                Path.Combine(serverDirectory, Path.Combine(segments[(rootIndex + 1)..]))
            );

        return Path.GetFullPath(
            Path.IsPathRooted(normalized)
                ? normalized
                : Path.Combine(serverDirectory, normalized)
        );
    }
}
