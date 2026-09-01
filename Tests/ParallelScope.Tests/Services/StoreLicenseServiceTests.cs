using ParallelScope.Services;

namespace ParallelScope.Tests.Services;

/// <summary>
/// Plusサブスクリプションの判定。
/// Storeの購読状態そのもの（StoreContext経由の分岐）と、正しい開発者キーによる解放は
/// テストから再現できないため、ここで確認するのは「購読が確認できない間は必ず未購読扱いになる」側です。
/// </summary>
public class StoreLicenseServiceTests
{
    [Fact]
    public void IsPlusActive_IsFalseBeforeAnyLicenseCheck()
    {
        var service = new StoreLicenseService();

        // 起動直後（ライセンス未取得）はPlus機能を出さない
        Assert.False(service.IsPlusActive);
        Assert.False(service.IsStoreAvailable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-key")]
    public void ApplyDeveloperUnlockKey_DoesNotUnlockForMissingOrWrongKey(string? key)
    {
        var service = new StoreLicenseService();

        service.ApplyDeveloperUnlockKey(key);

        Assert.False(service.IsPlusActive);
    }

    [Fact]
    public async Task RefreshLicenseAsync_NeverThrowsWhenTheStoreCannotBeReached()
    {
        // 起動時に必ず呼ばれるため、package identityが無い・オフライン等でも例外を出してはいけない。
        // 結果（IsPlusActive）は実行環境のStoreアカウントの購読状態に左右されるので検証しない
        var service = new StoreLicenseService();

        await WithDebugPlusEnvironmentVariable(null, service.RefreshLicenseAsync);
    }

#if DEBUG
    [Fact]
    public async Task RefreshLicenseAsync_EnablesPlusWhenDebugEnvironmentVariableIsSet()
    {
        // デバッグ実行時に購読済みUIを確認するための逃がし口（Releaseビルドには含まれない）
        var service = new StoreLicenseService();

        await WithDebugPlusEnvironmentVariable("1", service.RefreshLicenseAsync);

        Assert.True(service.IsPlusActive);
    }
#endif

    /// <summary>環境変数はプロセス全体の状態なので、値を差し替えて実行し必ず元へ戻す。</summary>
    private static async Task WithDebugPlusEnvironmentVariable(string? value, Func<Task> action)
    {
        const string name = "PARALLELSCOPE_DEBUG_PLUS";
        var original = Environment.GetEnvironmentVariable(name);

        Environment.SetEnvironmentVariable(name, value);
        try
        {
            await action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, original);
        }
    }
}
