using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;
using ParallelScope.Utilities;
using ParallelScope.ViewModels;

namespace ParallelScope.Tests.ViewModels;

/// <summary>戻る/進む/上へと、移動できない場所への移動の扱いの確認。移動先は実在の一時フォルダ。</summary>
[Collection(SharedStateCollection.Name)]
public class NavigationTests : ShellTestBase
{
    private readonly string _root;
    private readonly string _alpha;
    private readonly string _alphaChild;
    private readonly string _beta;

    public NavigationTests()
    {
        _root = PathNormalizer.Normalize(NewTempDirectory().Path);
        _alpha = Directory.CreateDirectory(Path.Combine(_root, "Alpha")).FullName;
        _alphaChild = Directory.CreateDirectory(Path.Combine(_alpha, "Child")).FullName;
        _beta = Directory.CreateDirectory(Path.Combine(_root, "Beta")).FullName;
        SettingsRepository.Save(new AppSettings
        {
            RootPaths = { _root },
            ExcludedPaths = { Path.Combine(_root, "Excluded") }
        });
        Directory.CreateDirectory(Path.Combine(_root, "Excluded"));
    }

    private BrowserTabViewModel StartAtRoot()
    {
        var tab = CreateViewModel().ActiveTab;
        Assert.Equal(_root, tab.CurrentPath);
        return tab;
    }

    [Fact]
    public void GoBackAndForward_WalkThroughTheHistory()
    {
        var tab = StartAtRoot();
        Assert.False(tab.CanGoBack);
        tab.LoadFiles(_alpha);
        tab.LoadFiles(_alphaChild);

        Assert.True(tab.GoBack());
        Assert.Equal(_alpha, tab.CurrentPath);
        Assert.True(tab.GoBack());
        Assert.Equal(_root, tab.CurrentPath);
        Assert.False(tab.CanGoBack);
        Assert.False(tab.GoBack());

        Assert.True(tab.GoForward());
        Assert.Equal(_alpha, tab.CurrentPath);
        Assert.True(tab.GoForward());
        Assert.Equal(_alphaChild, tab.CurrentPath);
        Assert.False(tab.CanGoForward);
        Assert.False(tab.GoForward());
    }

    // ブラウザと同じく、戻った先から別の場所へ移動したら進む履歴は捨てる
    [Fact]
    public void NavigatingAfterGoingBack_DiscardsTheForwardHistory()
    {
        var tab = StartAtRoot();
        tab.LoadFiles(_alpha);
        tab.GoBack();
        Assert.True(tab.CanGoForward);

        tab.LoadFiles(_beta);

        Assert.False(tab.CanGoForward);
        Assert.True(tab.GoBack());
        Assert.Equal(_root, tab.CurrentPath);
    }

    [Fact]
    public void GoUp_MovesToTheParentAndCanBeUndoneWithGoBack()
    {
        var tab = StartAtRoot();
        tab.LoadFiles(_alphaChild);

        Assert.True(tab.GoUp());
        Assert.Equal(_alpha, tab.CurrentPath);

        Assert.True(tab.GoBack());
        Assert.Equal(_alphaChild, tab.CurrentPath);
    }

    [Fact]
    public void NavigateTo_WithoutHistory_DoesNotEnableGoBack()
    {
        var tab = StartAtRoot();

        Assert.True(tab.NavigateTo(_alpha, addToHistory: false));

        Assert.Equal(_alpha, tab.CurrentPath);
        Assert.False(tab.CanGoBack);
    }

    [Fact]
    public void NavigateTo_SameFolder_DoesNotAddHistory()
    {
        var tab = StartAtRoot();
        tab.LoadFiles(_alpha);

        Assert.True(tab.LoadFiles(_alpha + Path.DirectorySeparatorChar));

        tab.GoBack();
        Assert.Equal(_root, tab.CurrentPath);
        Assert.False(tab.CanGoBack);
    }

    [Fact]
    public void NavigateTo_MissingFolder_FailsAndStaysPut()
    {
        var tab = StartAtRoot();
        tab.LoadFiles(_alpha);

        Assert.False(tab.LoadFiles(Path.Combine(_root, "DoesNotExist")));

        Assert.Equal(_alpha, tab.CurrentPath);
        tab.GoBack();
        Assert.Equal(_root, tab.CurrentPath);
    }

    [Fact]
    public void NavigateTo_ExcludedFolder_IsRefused()
    {
        var tab = StartAtRoot();

        Assert.False(tab.LoadFiles(Path.Combine(_root, "Excluded")));

        Assert.Equal(_root, tab.CurrentPath);
        Assert.False(tab.CanGoBack);
    }

    [Fact]
    public void TryNavigateByAddressInput_MovesToTheTypedFolder()
    {
        var tab = StartAtRoot();

        tab.AddressInput = _beta;

        Assert.True(tab.TryNavigateByAddressInput());
        Assert.Equal(_beta, tab.CurrentPath);
        Assert.True(tab.CanGoBack);
    }

    // 小文字で打っても、キャッシュに別表記の親パスとして二重に書き込まないよう実際の表記へそろえる
    [Fact]
    public void TryNavigateByAddressInput_RestoresTheActualCasing()
    {
        var tab = StartAtRoot();

        tab.AddressInput = _alphaChild.ToLowerInvariant();

        Assert.True(tab.TryNavigateByAddressInput());
        Assert.Equal(_alphaChild, tab.CurrentPath);
    }
}
