# 2画面（Plus）: 分割・操作対象ペインの切り替え・タブのペイン間移動・ペインを閉じる
$Scenario = @{
    Plus  = $true
    Steps = [ordered]@{
        '1画面では右クリックメニューに「このペインを閉じる」を出さない' = {
            Wait-UiFileRows @('Alpha', 'Beta', 'Gamma', 'hidden.txt', 'readme.txt')
            Invoke-UiClick 'Beta' -Type DataItem -Right
            Wait-UiElement 'OpenInOtherPaneMenuItem' | Out-Null
            Assert-UiTrue (-not (Test-UiElementExists 'ClosePaneMenuItem')) '1画面なのに「このペインを閉じる」があります。'
        }
        '1画面で「反対側のペインで開く」を選ぶと分割して開く' = {
            Invoke-UiElement 'OpenInOtherPaneMenuItem'
            Wait-UiElement 'Pane1' | Out-Null
            Wait-UiValue 'AddressTextBox' (Get-FixturePath 'Beta') -Pane 1
            Wait-UiFileRows @('beta.csv') -Pane 1
            Wait-UiValue 'AddressTextBox' (Get-FixturePath '') -Pane 0
        }
        'メニューから分割を解除すると1画面に戻り、タブは残る側へ移る' = {
            Invoke-UiElement 'TitleBarMenuButton'
            Invoke-UiElement 'SplitViewMenuItem'
            Wait-UiElement 'Pane1' -Gone | Out-Null
            Wait-UiTabCount 2 -Pane 0
        }
        'メニューから分割すると2つ目のペインが出る' = {
            Invoke-UiElement 'TitleBarMenuButton'
            Invoke-UiElement 'SplitViewMenuItem'
            Wait-UiElement 'Pane1' | Out-Null
            Wait-UiTabCount 1 -Pane 1
        }
        'F6 で操作対象のペインが切り替わる（Ctrl+T の追加先で確かめる）' = {
            $before0 = @(Get-UiTabNames -Pane 0).Count
            $before1 = @(Get-UiTabNames -Pane 1).Count
            Send-UiKeys 'ctrl+t'
            Start-Sleep -Milliseconds 500
            $first0 = @(Get-UiTabNames -Pane 0).Count
            $first1 = @(Get-UiTabNames -Pane 1).Count
            $addedToPane0 = $first0 -eq $before0 + 1
            Assert-UiTrue ($addedToPane0 -xor ($first1 -eq $before1 + 1)) "Ctrl+T でどちらか一方のペインにだけタブが増えるはずです（Pane0: $before0→$first0, Pane1: $before1→$first1）。"

            Send-UiKeys 'f6'
            Send-UiKeys 'ctrl+t'
            if ($addedToPane0) { Wait-UiTabCount ($before1 + 1) -Pane 1 } else { Wait-UiTabCount ($before0 + 1) -Pane 0 }
        }
        'タブをドラッグで反対側のペインへ移す' = {
            $count0 = @(Get-UiTabNames -Pane 0).Count
            $count1 = @(Get-UiTabNames -Pane 1).Count
            $tabName = @(Get-UiTabNames -Pane 0)[0]
            $targetName = @(Get-UiTabNames -Pane 1)[0]
            Move-UiElementByDrag $tabName $targetName -FromPane 0 -ToPane 1
            Wait-UiTabCount ($count0 - 1) -Pane 0
            Wait-UiTabCount ($count1 + 1) -Pane 1
        }
        '2画面では右クリックの「このペインを閉じる」で畳み、タブは残る側へ移る' = {
            $total = @(Get-UiTabNames -Pane 0).Count + @(Get-UiTabNames -Pane 1).Count
            $rows = @(Get-UiFileRows -Pane 1)
            Assert-UiTrue ($rows.Count -gt 0) 'Pane1 の一覧が空で右クリックできません。'
            Invoke-UiClick $rows[0] -Type DataItem -Pane 1 -Right
            Invoke-UiElement 'ClosePaneMenuItem'
            Wait-UiElement 'Pane1' -Gone | Out-Null
            Wait-UiTabCount $total -Pane 0
        }
    }
}
