using ParallelScope.Tests.TestSupport;
using ParallelScope.Utilities;

namespace ParallelScope.Tests.Utilities;

/// <summary>
/// UIスレッドから呼ぶ存在確認の判定の確認。時間切れ（2秒）の側は、ブロックするファイルシステムを用意できないため対象外。
/// </summary>
public class DirectoryAvailabilityCheckerTests
{
    [Fact]
    public void ExistsOrTimedOut_AnswersForAVisibleVolume()
    {
        using var temp = new TempDirectory();

        Assert.True(DirectoryAvailabilityChecker.ExistsOrTimedOut(temp.Path));
        Assert.False(DirectoryAvailabilityChecker.ExistsOrTimedOut(Path.Combine(temp.Path, "Missing")));
    }

    [Fact]
    public void ResolveExistingOrTimedOut_ResolvesExistingFolders()
    {
        using var temp = new TempDirectory();

        var resolved = DirectoryAvailabilityChecker.ResolveExistingOrTimedOut(
            temp.Path, path => path + "-resolved", _ => false);

        Assert.Equal(temp.Path + "-resolved", resolved);
    }

    // ボリュームは見えていてフォルダだけ無い ＝ 本当に無いので、キャッシュに載っていても開かない
    [Fact]
    public void ResolveExistingOrTimedOut_RefusesMissingFoldersOnAVisibleVolume()
    {
        using var temp = new TempDirectory();

        var resolved = DirectoryAvailabilityChecker.ResolveExistingOrTimedOut(
            Path.Combine(temp.Path, "Missing"), path => path, _ => true);

        Assert.Null(resolved);
    }

    // ボリュームごと見えない（切断中）なら、キャッシュで開けるフォルダとして開き、そのボリュームを台帳に控える。
    // 台帳はプロセス全体で1つ（VolumeAvailabilityTracker.Shared）で、一度控えたボリュームは
    // 下のテストのとおり問い合わせずに開くため、「キャッシュに無いので開かない」側はここでは確かめられない
    [Fact]
    public void ResolveExistingOrTimedOut_OpensCachedFoldersOnAnUnreachableVolume()
    {
        var path = Path.Combine(UnusedDrive.Root, "Share", "Folder");

        var resolved = DirectoryAvailabilityChecker.ResolveExistingOrTimedOut(
            path, _ => throw new InvalidOperationException("見えないフォルダの表記は整えない"), _ => true);

        Assert.Equal(path, resolved);
        Assert.True(VolumeAvailabilityTracker.Shared.IsUnreachable(path));
    }

    // 切断中と分かっているボリュームには問い合わせない（移動のたびにSMBのタイムアウトを待たせない）
    [Fact]
    public void ResolveExistingOrTimedOut_DoesNotProbeAVolumeKnownToBeUnreachable()
    {
        var path = Path.Combine(UnusedDrive.Root, "Share", "Other");
        VolumeAvailabilityTracker.Shared.MarkUnreachable(path);

        var resolved = DirectoryAvailabilityChecker.ResolveExistingOrTimedOut(
            path,
            _ => throw new InvalidOperationException("問い合わせない"),
            _ => throw new InvalidOperationException("問い合わせない"));

        Assert.Equal(path, resolved);
        Assert.True(DirectoryAvailabilityChecker.ExistsOrTimedOut(path));
    }
}
