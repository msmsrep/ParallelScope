# 無料版: Plus機能（タブ・分割・ツリー開閉・CSV出力）が出ない／押せないこと
$Scenario = @{
    Plus  = $false
    Steps = [ordered]@{
        'タブ列とツリー開閉ボタンを出さない' = {
            Wait-UiFileRows @('Alpha', 'Beta', 'Gamma', 'hidden.txt', 'readme.txt')
            Assert-UiTrue (-not (Test-UiElementExists 'TabItemsControl')) '無料版でタブ列が出ています。'
            Assert-UiTrue (-not (Test-UiElementExists 'NewTabButton')) '無料版で＋ボタンが出ています。'
            Assert-UiTrue (-not (Test-UiElementExists 'TreeToggleButton')) '無料版でツリー開閉ボタンが出ています。'
        }
        'タイトルバーに無料版と出る' = {
            Wait-UiElement 'FreeLicenseTextBlock' | Out-Null
        }
        'メニューの分割・CSV出力は押せない' = {
            Invoke-UiElement 'TitleBarMenuButton'
            Wait-UiElement 'SplitViewMenuItem' | Out-Null
            Assert-UiTrue (-not (Test-UiElementEnabled 'SplitViewMenuItem')) '無料版で分割が押せます。'
            Assert-UiTrue (-not (Test-UiElementEnabled 'ExportCsvMenuItem')) '無料版でCSV出力が押せます。'
            Send-UiKeys 'escape'
            Wait-UiElement 'SplitViewMenuItem' -Gone | Out-Null
        }
        'フォルダ行の右クリックで「新しいタブで開く」「反対側のペインで開く」は押せない' = {
            Invoke-UiClick 'Alpha' -Type DataItem -Right
            Wait-UiElement 'OpenInNewTabMenuItem' | Out-Null
            Assert-UiTrue (-not (Test-UiElementEnabled 'OpenInNewTabMenuItem')) '無料版で「新しいタブで開く」が押せます。'
            Assert-UiTrue (-not (Test-UiElementEnabled 'OpenInOtherPaneMenuItem')) '無料版で「反対側のペインで開く」が押せます。'
            Send-UiKeys 'escape'
            Wait-UiElement 'OpenInNewTabMenuItem' -Gone | Out-Null
        }
        'タブのショートカットは効かない（Ctrl+T のあとも1画面・タブ列なし）' = {
            Send-UiKeys 'ctrl+t'
            Send-UiKeys 'f6'
            Start-Sleep -Milliseconds 500
            Assert-UiTrue (-not (Test-UiElementExists 'TabItemsControl')) 'Ctrl+T でタブ列が出ました。'
            Assert-UiTrue (-not (Test-UiElementExists 'Pane1')) '2つ目のペインが出ています。'
        }
    }
}
