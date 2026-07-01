using System.IO;

namespace ParallelScope.Common;

public static class AppDataLocation
{
    private const string AppFolderName = "ParallelScope";
    private const string MsixPackagePrefix = "msmsrep.ParallelScope_";

    public static string GetAppDataDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (IsRunningAsMsix())
        {
            var localStateFromPackage = TryGetMsixLocalStateDirectory(localAppData);
            if (localStateFromPackage is not null)
            {
                Directory.CreateDirectory(localStateFromPackage);
                return localStateFromPackage;
            }
        }

        var fallback = Path.Combine(localAppData, AppFolderName);
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    private static bool IsRunningAsMsix()
    {
        return Environment.ProcessPath?.Contains(@"\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) ?? false;
    }

    private static string? TryGetMsixLocalStateDirectory(string localAppData)
    {
        var packagesRoot = Path.Combine(localAppData, "Packages");
        if (!Directory.Exists(packagesRoot))
        {
            return null;
        }

        var packageDir = Directory
            .EnumerateDirectories(packagesRoot, MsixPackagePrefix + "*")
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (packageDir is null)
        {
            return null;
        }

        return Path.Combine(packageDir, "LocalState");
    }
}
