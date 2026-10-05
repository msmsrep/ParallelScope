using ParallelScope.Utilities;

namespace ParallelScope.Tests.Utilities;

/// <summary>つながらないボリュームの台帳（控える・復帰を確かめる・失敗報告からの判定）の確認。</summary>
public class VolumeAvailabilityTrackerTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(@"\\nas\share\folder", @"\\nas\share\")]
    [InlineData(@"\\nas\share", @"\\nas\share\")]
    [InlineData(@"Z:\folder\sub", @"Z:\")]
    public void GetVolumeRoot_ReturnsTheRootOfThePath(string path, string expected)
    {
        Assert.Equal(expected, VolumeAvailabilityTracker.GetVolumeRoot(path), ignoreCase: true);
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"relative\path")]
    [InlineData("::Recent::")]
    public void GetVolumeRoot_ReturnsNullForPathsWithoutAVolume(string path)
    {
        Assert.Null(VolumeAvailabilityTracker.GetVolumeRoot(path));
    }

    [Fact]
    public void MarkUnreachable_AppliesToEveryPathOnTheSameVolume()
    {
        var tracker = new VolumeAvailabilityTracker(_ => false, TimeSpan.FromHours(1));

        tracker.MarkUnreachable(@"\\nas\share\a\b");

        Assert.True(tracker.IsUnreachable(@"\\NAS\share\other"));
        Assert.False(tracker.IsUnreachable(@"\\nas\another\a"));
        Assert.False(tracker.IsUnreachable(@"C:\a"));
    }

    [Fact]
    public void MarkUnreachable_ClearsAndNotifiesOnceTheVolumeIsVisibleAgain()
    {
        var isOnline = false;
        var tracker = new VolumeAvailabilityTracker(_ => Volatile.Read(ref isOnline), TimeSpan.FromMilliseconds(20));
        using var restored = new ManualResetEventSlim();
        string? restoredRoot = null;
        tracker.VolumeRestored += root =>
        {
            restoredRoot = root;
            restored.Set();
        };

        tracker.MarkUnreachable(@"\\nas\share\a");
        Volatile.Write(ref isOnline, true);

        Assert.True(restored.Wait(WaitLimit));
        Assert.Equal(@"\\nas\share\", restoredRoot);
        Assert.False(tracker.IsUnreachable(@"\\nas\share\a"));
    }

    [Fact]
    public void ReportFailure_MarksTheVolumeWhenItsRootIsNotVisible()
    {
        var tracker = new VolumeAvailabilityTracker(_ => false, TimeSpan.FromHours(1));

        tracker.ReportFailure(@"\\nas\share\a");

        Assert.True(SpinWait.SpinUntil(() => tracker.IsUnreachable(@"\\nas\share\a"), WaitLimit));
    }

    [Fact]
    public void ReportFailure_IgnoresFolderLevelFailuresOnAVisibleVolume()
    {
        using var probed = new ManualResetEventSlim();
        var tracker = new VolumeAvailabilityTracker(_ =>
        {
            probed.Set();
            return true;
        }, TimeSpan.FromHours(1));

        tracker.ReportFailure(@"\\nas\share\denied");

        Assert.True(probed.Wait(WaitLimit));
        Assert.False(tracker.IsUnreachable(@"\\nas\share\denied"));
    }

    [Fact]
    public void CheckVolume_MarksTheVolumeWhenItIsNotVisible()
    {
        var tracker = new VolumeAvailabilityTracker(_ => false, TimeSpan.FromHours(1));

        Assert.False(tracker.CheckVolume(@"\\nas\share\a"));
        Assert.True(tracker.IsUnreachable(@"\\nas\share\b"));
    }
}
