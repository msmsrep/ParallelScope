using System.Configuration;
using System.Data;
using System.Windows;
using ParallelScope.Services;

namespace ParallelScope;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        // App.xaml のリソース（Fluentテーマ）の読み込みより前に、キャッシュDB・設定・ライセンス確認の準備を裏で始める
        StartupPreload.Start();
    }
}
