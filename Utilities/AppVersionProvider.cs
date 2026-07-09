using System.IO;
using System.Xml.Linq;

namespace ParallelScope.Utilities;

/// <summary>AppxManifest.xml の Identity/Version からアプリのバージョン番号を取得するユーティリティ。</summary>
public static class AppVersionProvider
{
    private static readonly XNamespace ManifestNamespace = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";

    /// <summary>
    /// 実行ファイルと同じフォルダにある AppxManifest.xml から Version 属性を読み取る。
    /// パッケージ実行時・非パッケージ実行時のいずれでも、同フォルダにこのファイルが存在すれば取得できる。
    /// 取得できない場合は null を返す。
    /// </summary>
    public static string? GetVersion()
    {
        try
        {
            var manifestPath = Path.Combine(AppContext.BaseDirectory, "AppxManifest.xml");
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            var document = XDocument.Load(manifestPath);
            var identity = document.Root?.Element(ManifestNamespace + "Identity");
            var version = identity?.Attribute("Version")?.Value;

            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch
        {
            // マニフェストが読めない場合はバージョン不明として扱う
            return null;
        }
    }
}
