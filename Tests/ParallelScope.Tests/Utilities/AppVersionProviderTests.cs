using System.Xml.Linq;
using ParallelScope.Utilities;

namespace ParallelScope.Tests.Utilities;

/// <summary>ウィンドウタイトルに出すバージョンが、exeと同じフォルダへコピーされた AppxManifest.xml から読めることの確認。</summary>
public class AppVersionProviderTests
{
    [Fact]
    public void GetVersion_ReadsTheIdentityVersionOfTheManifest()
    {
        var manifest = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "AppxManifest.xml"));
        var expected = manifest.Root!
            .Element(XName.Get("Identity", "http://schemas.microsoft.com/appx/manifest/foundation/windows10"))!
            .Attribute("Version")!.Value;

        Assert.Equal(expected, AppVersionProvider.GetVersion());
        Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", AppVersionProvider.GetVersion());
    }
}
