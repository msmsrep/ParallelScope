using System.Security.Cryptography;
using System.Text;
using Windows.Services.Store;

namespace ParallelScope.Services;

/// <summary>
/// Microsoft Storeのサブスクリプションアドオン「ParallelScope Plus」のライセンス確認・購入を担当する。
/// 非パッケージ実行（開発時F5）やStore外配布ではStoreContextが使えないため、その場合は未購読扱いにフォールバックする。
/// </summary>
public sealed class StoreLicenseService
{
    // Partner Centerのサブスクリプションアドオン「PremiumSubscription」のStore ID
    private const string PlusAddOnStoreId = "9PPKXZMGHKKR";

    // 開発者専用: settings.jsonのDeveloperUnlockKeyのSHA-256がこの値に一致する場合、
    // Storeの購読状態に関わらずPlusを有効にする。ソースコードは公開されているため、
    // キー本体は埋め込まずハッシュのみを保持する（キーを知っているのは開発者だけ）
    private const string DeveloperUnlockKeyHashHex = "7C629C2677F717CC31D3E8F2C9937795FDEDB8515E433309962632E98112DB7E";

    private StoreContext? _context;
    private bool _isDeveloperUnlocked;

    /// <summary>Plusサブスクリプションが現在有効か。InitializeAsync完了まではfalse。</summary>
    public bool IsPlusActive { get; private set; }

    /// <summary>StoreContextが利用可能か（パッケージ実行かつStore APIの初期化に成功したか）。</summary>
    public bool IsStoreAvailable => _context is not null;

    /// <summary>
    /// settings.jsonの開発者キーを検証し、正しければPlusを強制的に有効化する。RefreshLicenseAsyncより先に呼ぶ。
    /// </summary>
    public void ApplyDeveloperUnlockKey(string? key)
    {
        _isDeveloperUnlocked = !string.IsNullOrEmpty(key)
            && Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))
                .Equals(DeveloperUnlockKeyHashHex, StringComparison.OrdinalIgnoreCase);

        if (_isDeveloperUnlocked)
        {
            IsPlusActive = true;
        }
    }

    /// <summary>
    /// Storeのライセンス情報を取得してIsPlusActiveを更新する。起動時と設定画面表示時に呼ぶ。
    /// ライセンスはStoreがローカルにキャッシュしているためオフラインでも判定できる。
    /// </summary>
    public async Task RefreshLicenseAsync()
    {
        if (_isDeveloperUnlocked)
        {
            IsPlusActive = true;
            return;
        }

#if DEBUG
        // 開発時（非パッケージ実行）はStoreライセンスを取得できないため、
        // 購読済みUIの動作確認用に環境変数で強制的に有効化できるようにする
        if (Environment.GetEnvironmentVariable("PARALLELSCOPE_DEBUG_PLUS") == "1")
        {
            IsPlusActive = true;
            return;
        }
#endif
        try
        {
            _context ??= StoreContext.GetDefault();
            var license = await _context.GetAppLicenseAsync();
            // サブスクリプションのアドオンライセンスはSkuStoreIdが「StoreId/xxxx」形式になるため前方一致で判定する。
            // 期限切れ・解約済みはIsActiveがfalseになる
            IsPlusActive = license.AddOnLicenses.Values
                .Any(x => x.IsActive && x.SkuStoreId.StartsWith(PlusAddOnStoreId, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            // package identityが無い（非パッケージ実行）等。未購読扱いで続行する
            _context = null;
            IsPlusActive = false;
        }
    }

    /// <summary>
    /// Plusサブスクリプションのストア表示価格（例: "¥300"）を取得する。取得できない場合はnull。
    /// </summary>
    public async Task<string?> GetPlusFormattedPriceAsync()
    {
        if (_context is null)
        {
            return null;
        }

        try
        {
            // サブスクリプションアドオンのProductKindは"Durable"として返される
            var result = await _context.GetStoreProductsAsync(new[] { "Durable" }, new[] { PlusAddOnStoreId });
            return result.Products.Values.FirstOrDefault()?.Price?.FormattedPrice;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 購入ダイアログを表示してPlusサブスクリプションの購入を試みる。UIスレッドから呼ぶこと。
    /// 成功（または購入済み）ならIsPlusActiveを更新してtrueを返す。
    /// </summary>
    public async Task<bool> PurchasePlusAsync(IntPtr hwnd)
    {
        if (_context is null)
        {
            return false;
        }

        try
        {
            // デスクトップアプリでは購入ダイアログを表示する前にウィンドウとの関連付けが必須
            WinRT.Interop.InitializeWithWindow.Initialize(_context, hwnd);
            var result = await _context.RequestPurchaseAsync(PlusAddOnStoreId);
            if (result.Status is StorePurchaseStatus.Succeeded or StorePurchaseStatus.AlreadyPurchased)
            {
                IsPlusActive = true;
            }

            return IsPlusActive;
        }
        catch
        {
            return false;
        }
    }
}
