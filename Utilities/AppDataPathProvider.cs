using System.IO;

namespace ParallelScope.Utilities;

/// <summary>
/// アプリケーションデータ保存先フォルダの解決ロジックを一箇所にまとめたユーティリティ。
/// MSIX パッケージ実行時と通常実行時でフォルダを切り替える。
/// </summary>
public static class AppDataPathProvider
{
    private const string MsixPackageFamilyName = "msmsrep.ParallelScope_77t1an0ygyrva";
    private const string LocalFolderName = "ParallelScope";

    /// <summary>アプリデータフォルダのフルパスを返す（存在しなければ作成する）。</summary>
    public static string GetOrCreateAppDataDirectory()
    {
        var appDataDir = IsRunningAsMsixPackage()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages",
                MsixPackageFamilyName,
                "LocalState")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                LocalFolderName);

        Directory.CreateDirectory(appDataDir);
        return appDataDir;
    }

    private static bool IsRunningAsMsixPackage()
    {
        return Environment.ProcessPath?.Contains(@"\WindowsApps\") ?? false;
    }
}
