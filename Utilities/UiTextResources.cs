namespace ParallelScope.Utilities;

/// <summary>
/// UI表示文字列の対訳表。<see cref="English"/>と<see cref="Japanese"/>はキーを揃えて持つ
/// （揃っているかは単体テストで検証している）。引き当ては<see cref="UiText"/>経由で行う。
/// </summary>
internal static class UiTextResources
{
    public static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // メインウィンドウ: メニュー・ナビゲーション
        ["Menu"] = "Menu",
        ["Menu.Settings"] = "⚙  Settings",
        ["Menu.ExportCsv"] = "📄  Export CSV...",
        ["Menu.UserGuide"] = "📖  User Guide",
        ["Menu.SplitView"] = "⬛  Split View",
        ["Menu.SplitVertical"] = "     Side by Side",
        ["Menu.SplitHorizontal"] = "     Top and Bottom",
        ["Nav.Back"] = "← Back",
        ["Nav.Forward"] = "Forward →",
        ["Nav.Up"] = "↑ Up",
        ["FileList.AllFiles"] = "All Files",

        // タイトルバーのキャプションボタン
        ["TitleBar.Minimize"] = "Minimize",
        ["TitleBar.Maximize"] = "Maximize",
        ["TitleBar.Restore"] = "Restore Down",
        ["TitleBar.Close"] = "Close",

        // タブ列
        ["Tab.New"] = "New Tab",
        ["Tab.Close"] = "Close Tab",
        ["Tab.Duplicate"] = "Duplicate Tab",
        ["Tab.CloseOthers"] = "Close Other Tabs",

        // ファイル一覧の列見出し
        ["Column.Name"] = "Name",
        ["Column.Location"] = "Location",
        ["Column.Type"] = "Type",
        ["Column.Size"] = "Size",
        ["Column.Modified"] = "Modified",
        ["Column.Created"] = "Created",
        ["Column.Attributes"] = "Attributes",

        // 右クリックメニュー
        ["Context.CopyFile"] = "Copy File",
        ["Context.CopyFileName"] = "Copy File Name",
        ["Context.CopyFullPath"] = "Copy Full Path",
        ["Context.OpenParentFolder"] = "Open Parent Folder",
        ["Context.OpenInNewTab"] = "Open in New Tab",
        ["Context.OpenInOtherPane"] = "Open in Other Pane",
        ["Context.ClosePane"] = "Close This Pane",
        ["Context.ScanSubtree"] = "Scan everything under this folder",
        ["Context.AddFavorite"] = "★  Add to Favorites",
        ["Context.RemoveFavorite"] = "☆  Remove from Favorites",

        // フォルダツリーの仮想ノード
        ["Tree.Folders"] = "Folders",
        ["Tree.Favorites"] = "Favorites",
        ["Tree.Frequent"] = "Frequently Used",
        ["Tree.Recent"] = "Recent",
        ["Tree.Loading"] = "Loading...",

        // メインウィンドウのメッセージ
        ["Dialog.Error"] = "Error",
        ["UserGuide.Caption"] = "User Guide",
        ["UserGuide.OpenFailed"] = "Could not open the user guide: {0}",
        ["Csv.Caption"] = "Export CSV",
        ["Csv.NoItems"] = "There are no items to export.",
        ["Csv.Filter"] = "CSV - sizes as displayed (*.csv)|*.csv|CSV - sizes in bytes (*.csv)|*.csv",
        ["Csv.Exported"] = "Exported {0} item(s) to:\n{1}",
        ["Csv.ExportFailed"] = "Could not export the CSV file: {0}",
        ["File.OpenFailed"] = "Could not open the file: {0}",
        ["ParentFolder.Caption"] = "Open Parent Folder",
        ["ParentFolder.Missing"] = "The item no longer exists.",
        ["ParentFolder.OpenFailed"] = "Could not open the parent folder: {0}",
        ["Clipboard.Caption"] = "Copy Error",
        ["Clipboard.Failed"] = "Could not copy to the clipboard: {0}",
        ["Navigation.Caption"] = "Navigation Error",
        ["Navigation.Failed"] = "Could not navigate to the specified folder. Please check the path.",
        ["Scan.Folder.Caption"] = "Folder Scan",
        ["Scan.Folder.Completed"] = "Scan completed. Updated cache for {0} folder(s).",
        ["Scan.Folder.ErrorCaption"] = "Folder Scan Error",
        ["Scan.Folder.Failed"] = "Scan failed: {0}",
        ["Scan.Full.Caption"] = "Full Scan",
        ["Scan.Full.Completed"] = "Full scan completed. Updated cache for {0} folder(s).",
        ["Scan.Full.CanceledCaption"] = "Full Scan Canceled",
        ["Scan.Full.Canceled"] = "Full scan was canceled.",
        ["Scan.Full.ErrorCaption"] = "Full Scan Error",
        ["Scan.Full.Failed"] = "Full scan failed: {0}",

        // 設定画面: 共通
        ["Settings.Title"] = "Settings",
        ["Common.Add"] = "Add",
        ["Common.Remove"] = "Remove",
        ["Common.Up"] = "↑ Up",
        ["Common.Down"] = "↓ Down",
        ["Common.Save"] = "Save",
        ["Common.Cancel"] = "Cancel",
        ["Settings.SaveAndFullScan"] = "Save + Full Scan",

        // 設定画面: 左メニュー
        ["Settings.Menu.RootFolders"] = "Root Folders",
        ["Settings.Menu.Columns"] = "Display Columns",
        ["Settings.Menu.TreeNodes"] = "Folder Tree",
        ["Settings.Menu.Theme"] = "Theme",
        ["Settings.Menu.Language"] = "Language",
        ["Settings.Menu.Subscription"] = "Subscription",
        ["Settings.Menu.Support"] = "Support",

        // 設定画面: ルートフォルダ
        ["Settings.Root.Header"] = "Target Root Folders",
        ["Settings.Root.Interval"] = "Auto full scan interval",
        ["Settings.Root.IntervalUnit"] = "hours",
        ["Settings.Root.Excluded"] = "Excluded Folders",
        ["Settings.InputErrorCaption"] = "Input Error",
        ["Settings.InvalidPath"] = "The path format is invalid.",
        ["Settings.FolderNotFound"] = "The specified folder does not exist.",
        ["Settings.SaveErrorCaption"] = "Save Error",
        ["Settings.NoRootFolder"] = "Please add at least one target root folder.",
        ["Settings.InvalidInterval"] = "Enter the auto full scan interval as a positive number of hours.",

        // 設定画面: 表示列
        ["Settings.Columns.Header"] = "Display Columns",
        ["Settings.Columns.PlusUpsell"] = "Customizing display columns is a Plus feature. Subscribe to unlock it.",
        ["Settings.Columns.Description"] = "Select the columns to show in the file list, and put them in the order you want.",
        ["Settings.Columns.Note"] = "The Name column is always shown. Column widths and the order you set by dragging the column headers are saved when you close the app.",
        ["Settings.Columns.Reset"] = "Reset columns to defaults",
        ["Settings.Columns.ResetHint"] = "The selected columns and their order are back to the defaults, and the column widths will be reset when you save.",
        ["Settings.HiddenItems.Header"] = "Hidden and system items",
        ["Settings.HiddenItems.Description"] = "Both are shown by default. Clear a box to leave those items out of the file list and the folder tree.",
        ["Settings.HiddenItems.ShowHidden"] = "Show hidden files and folders",
        ["Settings.HiddenItems.ShowSystem"] = "Show system files and folders",
        ["Settings.Column.Name"] = "Name (always shown)",
        ["Settings.Column.Location"] = "Location (parent folder path)",
        ["Settings.Column.Attributes"] = "Attributes (R/H/S/A)",

        // 設定画面: ツリー最上位のノード
        ["Settings.TreeNodes.Header"] = "Folder Tree",
        ["Settings.TreeNodes.PlusUpsell"] = "Customizing the top-level tree nodes is a Plus feature. Subscribe to unlock it.",
        ["Settings.TreeNodes.Description"] = "Select the shortcut nodes to show at the top of the folder tree, and put them in the order you want.",
        ["Settings.TreeNodes.Note"] = "The Folders node is always shown, but you can move it. \"Recent\" and \"Frequently Used\" are both built from the folders you open; hiding one does not stop the other from filling up.",
        ["Settings.TreeNodes.Reset"] = "Reset the tree nodes to defaults",
        ["Settings.TreeNodes.ResetHint"] = "The selected nodes and their order are back to the defaults. They are applied when you save.",
        ["Settings.TreeNode.AllRoots"] = "Folders (always shown)",

        // 設定画面: 配色テーマ
        ["Settings.Theme.Header"] = "Theme",
        ["Settings.Theme.Description"] = "Choose the color theme of the app.",
        ["Settings.Theme.System"] = "System (follow Windows)",
        ["Settings.Theme.Light"] = "Light",
        ["Settings.Theme.Dark"] = "Dark",
        ["Settings.Theme.Note"] = "The theme is applied and saved immediately.",

        // 設定画面: 表示言語
        ["Settings.Language.Header"] = "Language",
        ["Settings.Language.Description"] = "Choose the display language of the app.",
        ["Settings.Language.System"] = "System (follow Windows)",
        ["Settings.Language.English"] = "English",
        ["Settings.Language.Japanese"] = "日本語 (Japanese)",
        ["Settings.Language.Note"] = "The language is applied and saved immediately.",

        // 設定画面: サブスクリプション
        ["Plus.Name"] = "ParallelScope Plus",
        ["Settings.GoToSubscription"] = "Go to Subscription page",
        ["Settings.Subscription.Header"] = "Subscription",
        ["Settings.Subscription.Active"] = "✅ Your Plus subscription is active.",
        ["Settings.Subscription.Description"] = "Subscribe to ParallelScope Plus to unlock premium features such as Favorites, Recent and Frequently Used folders in the tree, customizing display columns in the file list, and exporting the file list to CSV.",
        ["Settings.Subscription.Subscribe"] = "🔓 Subscribe to Plus",
        ["Settings.Subscription.SubscribeWithPrice"] = "🔓 Subscribe to Plus ({0} / month)",
        ["Settings.Subscription.StoreUnavailable"] = "The Microsoft Store is not available. Please install this app from the Microsoft Store to subscribe.",
        ["Settings.Subscription.PurchaseNotCompleted"] = "The purchase was not completed.",
        ["Settings.Subscription.ManageDescription"] = "Subscriptions are billed through the Microsoft Store. To cancel your subscription, open the subscriptions page of your Microsoft account.",
        ["Settings.Subscription.Manage"] = "🔗 Manage / Cancel subscription",
        ["Settings.Subscription.ManageCaption"] = "Manage subscription",

        // 設定画面: サポート
        ["Settings.Support.Header"] = "Support",
        ["Settings.Support.Body"] = "This app (Parallel Scope) is independently developed and managed. We accept voluntary support to help with continuous updates and feature improvements.\n\nIf you would like to support us, it would be a great encouragement if you could do so via the link below.",
        ["Settings.Support.Note"] = "(This support is a voluntary donation without consideration, and no benefits are provided in return.)",
        ["Settings.Support.Kofi"] = "☕ Buy a coffee for the developer",
        ["Settings.Support.GitHubSponsors"] = "💜 GitHub Sponsors"
    };

    public static readonly IReadOnlyDictionary<string, string> Japanese = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // メインウィンドウ: メニュー・ナビゲーション
        ["Menu"] = "メニュー",
        ["Menu.Settings"] = "⚙  設定",
        ["Menu.ExportCsv"] = "📄  CSVに書き出す...",
        ["Menu.UserGuide"] = "📖  使い方ガイド",
        ["Menu.SplitView"] = "⬛  画面を分割する",
        ["Menu.SplitVertical"] = "     左右に分割",
        ["Menu.SplitHorizontal"] = "     上下に分割",
        ["Nav.Back"] = "← 戻る",
        ["Nav.Forward"] = "進む →",
        ["Nav.Up"] = "↑ 上へ",
        ["FileList.AllFiles"] = "All Files",

        // タイトルバーのキャプションボタン
        ["TitleBar.Minimize"] = "最小化",
        ["TitleBar.Maximize"] = "最大化",
        ["TitleBar.Restore"] = "元のサイズに戻す",
        ["TitleBar.Close"] = "閉じる",

        // タブ列
        ["Tab.New"] = "新しいタブ",
        ["Tab.Close"] = "タブを閉じる",
        ["Tab.Duplicate"] = "タブを複製",
        ["Tab.CloseOthers"] = "他のタブを閉じる",

        // ファイル一覧の列見出し
        ["Column.Name"] = "名前",
        ["Column.Location"] = "場所",
        ["Column.Type"] = "種類",
        ["Column.Size"] = "サイズ",
        ["Column.Modified"] = "更新日時",
        ["Column.Created"] = "作成日時",
        ["Column.Attributes"] = "属性",

        // 右クリックメニュー
        ["Context.CopyFile"] = "ファイルをコピー",
        ["Context.CopyFileName"] = "ファイル名をコピー",
        ["Context.CopyFullPath"] = "フルパスをコピー",
        ["Context.OpenParentFolder"] = "親フォルダーを開く",
        ["Context.OpenInNewTab"] = "新しいタブで開く",
        ["Context.OpenInOtherPane"] = "反対側のペインで開く",
        ["Context.ClosePane"] = "このペインを閉じる",
        ["Context.ScanSubtree"] = "このフォルダー配下をすべてスキャン",
        ["Context.AddFavorite"] = "★  お気に入りに追加",
        ["Context.RemoveFavorite"] = "☆  お気に入りから削除",

        // フォルダツリーの仮想ノード
        ["Tree.Folders"] = "フォルダー",
        ["Tree.Favorites"] = "お気に入り",
        ["Tree.Frequent"] = "よく使うフォルダー",
        ["Tree.Recent"] = "最近開いたフォルダー",
        ["Tree.Loading"] = "読み込み中...",

        // メインウィンドウのメッセージ
        ["Dialog.Error"] = "エラー",
        ["UserGuide.Caption"] = "使い方ガイド",
        ["UserGuide.OpenFailed"] = "使い方ガイドを開けませんでした: {0}",
        ["Csv.Caption"] = "CSVに書き出す",
        ["Csv.NoItems"] = "書き出す項目がありません。",
        ["Csv.Filter"] = "CSV - サイズは表示どおり (*.csv)|*.csv|CSV - サイズはバイト数 (*.csv)|*.csv",
        ["Csv.Exported"] = "{0} 件を次の場所へ書き出しました:\n{1}",
        ["Csv.ExportFailed"] = "CSVファイルを書き出せませんでした: {0}",
        ["File.OpenFailed"] = "ファイルを開けませんでした: {0}",
        ["ParentFolder.Caption"] = "親フォルダーを開く",
        ["ParentFolder.Missing"] = "この項目は存在しません。",
        ["ParentFolder.OpenFailed"] = "親フォルダーを開けませんでした: {0}",
        ["Clipboard.Caption"] = "コピーエラー",
        ["Clipboard.Failed"] = "クリップボードにコピーできませんでした: {0}",
        ["Navigation.Caption"] = "移動エラー",
        ["Navigation.Failed"] = "指定されたフォルダーへ移動できませんでした。パスを確認してください。",
        ["Scan.Folder.Caption"] = "フォルダースキャン",
        ["Scan.Folder.Completed"] = "スキャンが完了しました。{0} 個のフォルダーのキャッシュを更新しました。",
        ["Scan.Folder.ErrorCaption"] = "フォルダースキャンのエラー",
        ["Scan.Folder.Failed"] = "スキャンに失敗しました: {0}",
        ["Scan.Full.Caption"] = "フルスキャン",
        ["Scan.Full.Completed"] = "フルスキャンが完了しました。{0} 個のフォルダーのキャッシュを更新しました。",
        ["Scan.Full.CanceledCaption"] = "フルスキャンの中止",
        ["Scan.Full.Canceled"] = "フルスキャンを中止しました。",
        ["Scan.Full.ErrorCaption"] = "フルスキャンのエラー",
        ["Scan.Full.Failed"] = "フルスキャンに失敗しました: {0}",

        // 設定画面: 共通
        ["Settings.Title"] = "設定",
        ["Common.Add"] = "追加",
        ["Common.Remove"] = "削除",
        ["Common.Up"] = "↑ 上へ",
        ["Common.Down"] = "↓ 下へ",
        ["Common.Save"] = "保存",
        ["Common.Cancel"] = "キャンセル",
        ["Settings.SaveAndFullScan"] = "保存 + フルスキャン",

        // 設定画面: 左メニュー
        ["Settings.Menu.RootFolders"] = "ルートフォルダー",
        ["Settings.Menu.Columns"] = "表示する列",
        ["Settings.Menu.TreeNodes"] = "フォルダツリー",
        ["Settings.Menu.Theme"] = "配色テーマ",
        ["Settings.Menu.Language"] = "言語",
        ["Settings.Menu.Subscription"] = "サブスクリプション",
        ["Settings.Menu.Support"] = "サポート",

        // 設定画面: ルートフォルダ
        ["Settings.Root.Header"] = "対象のルートフォルダー",
        ["Settings.Root.Interval"] = "自動フルスキャンの間隔",
        ["Settings.Root.IntervalUnit"] = "時間",
        ["Settings.Root.Excluded"] = "除外するフォルダー",
        ["Settings.InputErrorCaption"] = "入力エラー",
        ["Settings.InvalidPath"] = "パスの形式が正しくありません。",
        ["Settings.FolderNotFound"] = "指定されたフォルダーは存在しません。",
        ["Settings.SaveErrorCaption"] = "保存エラー",
        ["Settings.NoRootFolder"] = "対象のルートフォルダーを1つ以上追加してください。",
        ["Settings.InvalidInterval"] = "自動フルスキャンの間隔は、正の整数（時間）で入力してください。",

        // 設定画面: 表示列
        ["Settings.Columns.Header"] = "表示する列",
        ["Settings.Columns.PlusUpsell"] = "表示する列のカスタマイズは Plus の機能です。サブスクリプションに登録すると使えます。",
        ["Settings.Columns.Description"] = "ファイル一覧に表示する列を選び、好きな順番に並べ替えられます。",
        ["Settings.Columns.Note"] = "「名前」列は常に表示されます。列の幅と、列見出しのドラッグで変えた並び順は、アプリを閉じるときに保存されます。",
        ["Settings.Columns.Reset"] = "列の設定を既定に戻す",
        ["Settings.Columns.ResetHint"] = "表示する列と並び順を既定に戻しました。列の幅は保存時にリセットされます。",
        ["Settings.HiddenItems.Header"] = "隠しファイル・システムファイル",
        ["Settings.HiddenItems.Description"] = "どちらも既定では表示します。チェックを外すと、ファイル一覧とフォルダーツリーのどちらにも出しません。",
        ["Settings.HiddenItems.ShowHidden"] = "隠しファイル・隠しフォルダーを表示する",
        ["Settings.HiddenItems.ShowSystem"] = "システムファイル・システムフォルダーを表示する",
        ["Settings.Column.Name"] = "名前（常に表示）",
        ["Settings.Column.Location"] = "場所（親フォルダーのパス）",
        ["Settings.Column.Attributes"] = "属性（R/H/S/A）",

        // 設定画面: ツリー最上位のノード
        ["Settings.TreeNodes.Header"] = "フォルダツリー",
        ["Settings.TreeNodes.PlusUpsell"] = "ツリー最上位のノードのカスタマイズは Plus の機能です。サブスクリプションに登録すると使えます。",
        ["Settings.TreeNodes.Description"] = "フォルダツリーの最上位に表示するショートカットのノードを選び、好きな順番に並べ替えられます。",
        ["Settings.TreeNodes.Note"] = "「Folders」は常に表示されますが、位置は入れ替えられます。「Recent」と「Frequently Used」はどちらも開いたフォルダの記録から作られるので、片方を非表示にしてももう片方には並び続けます。",
        ["Settings.TreeNodes.Reset"] = "ツリーのノードを既定に戻す",
        ["Settings.TreeNodes.ResetHint"] = "表示するノードと並び順を既定に戻しました。保存すると反映されます。",
        ["Settings.TreeNode.AllRoots"] = "Folders（常に表示）",

        // 設定画面: 配色テーマ
        ["Settings.Theme.Header"] = "配色テーマ",
        ["Settings.Theme.Description"] = "アプリの配色テーマを選びます。",
        ["Settings.Theme.System"] = "システム（Windowsに合わせる）",
        ["Settings.Theme.Light"] = "ライト",
        ["Settings.Theme.Dark"] = "ダーク",
        ["Settings.Theme.Note"] = "配色テーマはすぐに適用・保存されます。",

        // 設定画面: 表示言語
        ["Settings.Language.Header"] = "言語",
        ["Settings.Language.Description"] = "アプリの表示言語を選びます。",
        ["Settings.Language.System"] = "システム（Windowsに合わせる）",
        ["Settings.Language.English"] = "English (英語)",
        ["Settings.Language.Japanese"] = "日本語",
        ["Settings.Language.Note"] = "表示言語はすぐに適用・保存されます。",

        // 設定画面: サブスクリプション
        ["Plus.Name"] = "ParallelScope Plus",
        ["Settings.GoToSubscription"] = "サブスクリプションのページへ",
        ["Settings.Subscription.Header"] = "サブスクリプション",
        ["Settings.Subscription.Active"] = "✅ Plus のサブスクリプションは有効です。",
        ["Settings.Subscription.Description"] = "ParallelScope Plus に登録すると、ツリーの「お気に入り」「最近開いたフォルダー」「よく使うフォルダー」、ファイル一覧の表示列のカスタマイズ、ファイル一覧のCSV書き出しといった機能が使えます。",
        ["Settings.Subscription.Subscribe"] = "🔓 Plus に登録する",
        ["Settings.Subscription.SubscribeWithPrice"] = "🔓 Plus に登録する（{0} / 月）",
        ["Settings.Subscription.StoreUnavailable"] = "Microsoft Store を利用できません。登録するには、Microsoft Store からこのアプリをインストールしてください。",
        ["Settings.Subscription.PurchaseNotCompleted"] = "購入は完了しませんでした。",
        ["Settings.Subscription.ManageDescription"] = "サブスクリプションは Microsoft Store 経由で請求されます。解約するには、Microsoft アカウントのサブスクリプションのページを開いてください。",
        ["Settings.Subscription.Manage"] = "🔗 サブスクリプションの管理・解約",
        ["Settings.Subscription.ManageCaption"] = "サブスクリプションの管理",

        // 設定画面: サポート
        ["Settings.Support.Header"] = "サポート",
        ["Settings.Support.Body"] = "このアプリ（Parallel Scope）は個人で開発・運営しています。継続的なアップデートや機能改善のため、任意の支援を受け付けています。\n\nもし支援していただける場合は、下のリンクからお願いできればとても励みになります。",
        ["Settings.Support.Note"] = "（この支援は対価のない任意の寄付であり、見返りとしての特典はありません。）",
        ["Settings.Support.Kofi"] = "☕ 開発者にコーヒーをおごる",
        ["Settings.Support.GitHubSponsors"] = "💜 GitHub Sponsors"
    };
}
