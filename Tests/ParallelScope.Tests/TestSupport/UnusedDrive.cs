namespace ParallelScope.Tests.TestSupport;

/// <summary>
/// 割り当てられていないドライブ文字。切断中のNAS・外したドライブ（ボリュームごと見えない）の代わりに使う
/// —— 実在しないUNCパスでは名前解決やSMBのタイムアウトを待たされるが、未割り当てのドライブ文字ならすぐに「無い」と分かる。
/// つながらないボリュームの台帳（VolumeAvailabilityTracker.Shared）はプロセス全体で1つなので、
/// ここで控えたドライブは他のテストの対象（一時フォルダのあるドライブ）と重ならない。
/// </summary>
public static class UnusedDrive
{
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        var used = DriveInfo.GetDrives().Select(x => char.ToUpperInvariant(x.Name[0])).ToHashSet();
        for (var letter = 'Q'; letter <= 'Z'; letter++)
        {
            if (!used.Contains(letter))
            {
                return $@"{letter}:\";
            }
        }

        throw new InvalidOperationException("未割り当てのドライブ文字が見つかりません。");
    }
}
