using System.Security.Cryptography;
using System.Text;
using Windows.Services.Store;

namespace ParallelScope.Services;

/// <summary>
/// Microsoft Storeのアドオン「ParallelScope Plus」（月額サブスクリプションと買い切り）の
/// ライセンス確認・購入を担当する。解放される機能はどちらも同じ。
/// 非パッケージ実行（開発時F5）やStore外配布ではStoreContextが使えないため、その場合は未購入扱いにフォールバックする。
/// </summary>
public sealed class StoreLicenseService
{
    // Partner Centerのサブスクリプションアドオン「PremiumSubscription」のStore ID
    public const string PlusSubscriptionAddOnStoreId = "9PPKXZMGHKKR";

    // Partner Centerの永続（買い切り）アドオンのStore ID
    public const string PlusLifetimeAddOnStoreId = "9N7W86WFP62F";

    // 開発者専用: settings.jsonのDeveloperUnlockKeyのSHA-256がこの値に一致する場合、
    // Storeの購入状態に関わらずPlusを有効にする。ソースコードは公開されているため、
    // キー本体は埋め込まずハッシュのみを保持する（キーを知っているのは開発者だけ）
    private const string DeveloperUnlockKeyHashHex = "7C629C2677F717CC31D3E8F2C9937795FDEDB8515E433309962632E98112DB7E";

    private StoreContext? _context;
    private bool _isDeveloperUnlocked;

    /// <summary>Plus機能が現在有効か（月額・買い切りのどちらか）。RefreshLicenseAsync完了まではfalse。</summary>
    public bool IsPlusActive { get; private set; }

    /// <summary>月額サブスクリプションが現在有効か。</summary>
    public bool IsSubscriptionActive { get; private set; }

    /// <summary>買い切り版を購入済みか。</summary>
    public bool IsLifetimeOwned { get; private set; }

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
        // 購入済みUIの動作確認用に環境変数で強制的に有効化できるようにする
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
            // アドオンライセンスはSkuStoreIdが「StoreId/xxxx」形式になるため前方一致で判定する。
            // サブスクリプションの期限切れ・解約済みはIsActiveがfalseになる（買い切りは返金時のみfalse）
            IsSubscriptionActive = HasActiveLicense(license, PlusSubscriptionAddOnStoreId);
            IsLifetimeOwned = HasActiveLicense(license, PlusLifetimeAddOnStoreId);
            IsPlusActive = IsSubscriptionActive || IsLifetimeOwned;
        }
        catch
        {
            // package identityが無い（非パッケージ実行）等。未購入扱いで続行する
            _context = null;
            IsSubscriptionActive = false;
            IsLifetimeOwned = false;
            IsPlusActive = false;
        }
    }

    private static bool HasActiveLicense(StoreAppLicense license, string addOnStoreId)
        => license.AddOnLicenses.Values
            .Any(x => x.IsActive && x.SkuStoreId.StartsWith(addOnStoreId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Plusアドオンのストア表示価格（例: "¥300"）をStore IDごとに取得する。取得できない場合は空。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetPlusFormattedPricesAsync()
    {
        if (_context is null)
        {
            return new Dictionary<string, string>();
        }

        try
        {
            // サブスクリプションアドオン・永続アドオンのどちらもProductKindは"Durable"として返されるため1回で取れる
            var result = await _context.GetStoreProductsAsync(
                new[] { "Durable" },
                new[] { PlusSubscriptionAddOnStoreId, PlusLifetimeAddOnStoreId });

            var prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var product in result.Products.Values)
            {
                var price = product.Price?.FormattedPrice;
                if (!string.IsNullOrEmpty(product.StoreId) && !string.IsNullOrEmpty(price))
                {
                    prices[product.StoreId] = price;
                }
            }

            return prices;
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// 購入ダイアログを表示してPlusアドオン（月額または買い切り）の購入を試みる。UIスレッドから呼ぶこと。
    /// 成功（または購入済み）ならライセンス状態を更新してtrueを返す。
    /// </summary>
    public async Task<bool> PurchasePlusAsync(IntPtr hwnd, string addOnStoreId)
    {
        if (_context is null)
        {
            return false;
        }

        try
        {
            // デスクトップアプリでは購入ダイアログを表示する前にウィンドウとの関連付けが必須
            WinRT.Interop.InitializeWithWindow.Initialize(_context, hwnd);
            var result = await _context.RequestPurchaseAsync(addOnStoreId);
            if (result.Status is StorePurchaseStatus.Succeeded or StorePurchaseStatus.AlreadyPurchased)
            {
                if (addOnStoreId.Equals(PlusLifetimeAddOnStoreId, StringComparison.OrdinalIgnoreCase))
                {
                    IsLifetimeOwned = true;
                }
                else
                {
                    IsSubscriptionActive = true;
                }

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
