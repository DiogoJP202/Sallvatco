namespace Sallvat.Web.Security;

public static class DataProtectionPath
{
    public static bool IsOutsideDirectory(
        string candidatePath,
        string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        var relativePath = Path.GetRelativePath(
            Path.GetFullPath(directoryPath),
            Path.GetFullPath(candidatePath));

        return Path.IsPathFullyQualified(relativePath)
            || relativePath.Equals("..", StringComparison.Ordinal)
            || relativePath.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal);
    }
}
