# 設定画面: 表示言語の即時切り替えと、隠しファイル表示の保存
$Scenario = @{
    Plus  = $true
    Steps = [ordered]@{
        '設定画面を開く' = {
            Wait-UiFileRows @('Alpha', 'Beta', 'Gamma', 'hidden.txt', 'readme.txt')
            Invoke-UiElement 'TitleBarMenuButton'
            Invoke-UiElement 'SettingsMenuItem'
            $script:MainHwnd = Use-UiWindow 'Settings'
        }
        '日本語を選ぶとその場で設定画面と一覧の列見出しが切り替わる' = {
            Invoke-UiElement 'LanguageMenuItem' -Action select
            Invoke-UiElement 'JapaneseLanguageRadioButton' -Action select
            Wait-UiElement '言語' | Out-Null
            $settingsHwnd = Use-UiWindow 'ParallelScope*'
            try {
                Wait-UiElement '名前' | Out-Null
                Wait-UiElement '種類' | Out-Null
            }
            finally {
                Set-UiWindowHandle $settingsHwnd
            }
        }
        '英語に戻すと列見出しも戻る' = {
            Invoke-UiElement 'EnglishLanguageRadioButton' -Action select
            $settingsHwnd = Use-UiWindow 'ParallelScope*'
            try {
                Wait-UiElement 'Name' | Out-Null
                Wait-UiElement '名前' -Gone | Out-Null
            }
            finally {
                Set-UiWindowHandle $settingsHwnd
            }
        }
        '隠しファイルを表示しない設定を保存すると一覧から消える' = {
            Invoke-UiElement 'ColumnsMenuItem' -Action select
            Invoke-UiElement 'ShowHiddenItemsCheckBox' -Action toggle-off
            Invoke-UiElement 'SaveButton'
            Set-UiWindowHandle $script:MainHwnd
            Wait-UiFileRows @('Alpha', 'Beta', 'Gamma', 'readme.txt')
        }
        '起動し直しても設定が残る' = {
            Restart-UiApp
            Wait-UiFileRows @('Alpha', 'Beta', 'Gamma', 'readme.txt')
            Wait-UiElement 'Name' | Out-Null
        }
    }
}
