# 複数タブ（Plus）: ショートカット・閉じたタブの開き直し・上限
$Scenario = @{
    Plus  = $true
    Steps = [ordered]@{
        '1画面でタブが1つのときは閉じるボタンを出さない' = {
            Wait-UiTabCount 1
            Assert-UiTrue (-not (Test-UiElementExists 'CloseTabButton')) '最後の1つのタブに閉じるボタンがあります。'
        }
        'Ctrl+T で新しいタブが開き、閉じるボタンが出る' = {
            Send-UiKeys 'ctrl+t'
            Wait-UiTabCount 2
            Assert-UiTrue (Test-UiElementExists 'CloseTabButton') 'タブが2つなのに閉じるボタンがありません。'
        }
        '新しいタブでの移動はそのタブだけに効く' = {
            Wait-UiFileRows @('Alpha', 'Beta', 'Gamma', 'hidden.txt', 'readme.txt')
            Invoke-UiClick 'Alpha' -Type DataItem -Double
            Wait-UiFileRows @('Sub1', 'alpha1.txt', 'alpha2.md')
            $names = Get-UiTabNames
            Assert-UiEqual 'UiTestRoot|Alpha' ($names -join '|') 'タブ名:'
        }
        'Ctrl+Tab / Ctrl+数字 でタブを切り替える' = {
            Send-UiKeys 'ctrl+tab'
            Wait-UiValue 'AddressTextBox' (Get-FixturePath '')
            Send-UiKeys 'ctrl+2'
            Wait-UiValue 'AddressTextBox' (Get-FixturePath 'Alpha')
            Send-UiKeys 'ctrl+1'
            Wait-UiValue 'AddressTextBox' (Get-FixturePath '')
        }
        'Ctrl+W で表示中のタブを閉じる' = {
            Send-UiKeys 'ctrl+2'
            Wait-UiValue 'AddressTextBox' (Get-FixturePath 'Alpha')
            Send-UiKeys 'ctrl+w'
            Wait-UiTabCount 1
            Wait-UiValue 'AddressTextBox' (Get-FixturePath '')
        }
        'Ctrl+Shift+T で閉じたタブを開き直す' = {
            Send-UiKeys 'ctrl+shift+t'
            Wait-UiTabCount 2
            Wait-UiValue 'AddressTextBox' (Get-FixturePath 'Alpha')
        }
        '20タブで上限になり、＋ボタンもCtrl+Tも効かなくなる' = {
            for ($i = 2; $i -lt 20; $i++) { Invoke-UiElement 'NewTabButton' }
            Wait-UiTabCount 20 -TimeoutMs 10000
            Assert-UiTrue (-not (Test-UiElementEnabled 'NewTabButton')) '上限なのに＋ボタンが押せます。'
            Send-UiKeys 'ctrl+t'
            Start-Sleep -Milliseconds 500
            Wait-UiTabCount 20
        }
        '閉じるボタンでタブを閉じる' = {
            Invoke-UiElement 'CloseTabButton'
            Wait-UiTabCount 19
            Assert-UiTrue (Test-UiElementEnabled 'NewTabButton') '上限を下回ったのに＋ボタンが押せません。'
        }
    }
}
