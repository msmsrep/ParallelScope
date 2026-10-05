using System.IO;
using ParallelScope.Tests.TestSupport;
using ParallelScope.Utilities;

namespace ParallelScope.Tests.Utilities;

public class AppDataPathProviderTests
{
#if DEBUG
    [Fact]
    public void GetOrCreateAppDataDirectory_UsesOverrideDirectoryFromEnvironment()
    {
        // UIテストが実際の settings.json・キャッシュDBへ触れないための差し替え口
        using var temp = new TempDirectory();
        var overrideDir = temp.Combine("data");
        var previous = Environment.GetEnvironmentVariable(AppDataPathProvider.DataDirectoryEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(AppDataPathProvider.DataDirectoryEnvironmentVariable, overrideDir);

            var result = AppDataPathProvider.GetOrCreateAppDataDirectory();

            Assert.Equal(overrideDir, result);
            Assert.True(Directory.Exists(overrideDir));
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppDataPathProvider.DataDirectoryEnvironmentVariable, previous);
        }
    }
#endif
}
