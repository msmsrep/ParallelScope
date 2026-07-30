using System.Windows;

namespace ParallelScope.Utilities;

/// <summary>アプリの配色テーマ設定。settings.jsonには名前（"System"/"Light"/"Dark"）で保存する。</summary>
public enum AppThemeSetting
{
    /// <summary>Windowsのライト/ダーク設定に追従する。</summary>
    System,
    Light,
    Dark
}

/// <summary>配色テーマ設定の解釈とアプリ全体への適用。</summary>
public static class AppTheme
{
    /// <summary>設定ファイルの文字列をテーマ設定へ変換する。未設定・不正値はOS追従に丸める。</summary>
    public static AppThemeSetting Parse(string? value)
    {
        return Enum.TryParse<AppThemeSetting>(value, true, out var parsed) ? parsed : AppThemeSetting.System;
    }

    /// <summary>指定テーマをアプリ全体（開いている全ウィンドウ）へ適用する。</summary>
    public static void Apply(AppThemeSetting theme)
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        // ThemeModeは実験的APIとしてマークされており、既定では診断WPF0001でエラーになるためここだけ抑止する
#pragma warning disable WPF0001
        application.ThemeMode = theme switch
        {
            AppThemeSetting.Light => ThemeMode.Light,
            AppThemeSetting.Dark => ThemeMode.Dark,
            _ => ThemeMode.System
        };
#pragma warning restore WPF0001
    }
}
