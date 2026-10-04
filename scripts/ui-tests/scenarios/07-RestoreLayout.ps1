# タブ構成・分割状態の保存と復元（Plus）。未購読で起動している間は復元せず、保存済みの構成も消さない
$Scenario = @{
    Plus  = $true
    Steps = [ordered]@{
        'タブを増やして2画面にする' = {
            Wait-UiFileRows @('Alpha', 'Beta', 'Gamma', 'hidden.txt', 'readme.txt')
            Send-UiKeys 'ctrl+t'
            Wait-UiTabCount 2
            Invoke-UiClick 'Alpha' -Type DataItem -Double
            Wait-UiValue 'AddressTextBox' (Get-FixturePath 'Alpha')
            Send-UiKeys 'ctrl+1'
            Wait-UiValue 'AddressTextBox' (Get-FixturePath '')
            Invoke-UiClick 'Beta' -Type DataItem -Right
            Invoke-UiElement 'OpenInOtherPaneMenuItem'
            Wait-UiValue 'AddressTextBox' (Get-FixturePath 'Beta') -Pane 1
        }
        '起動し直すとタブ構成・表示中のタブ・2画面が戻る' = {
            Restart-UiApp
            Wait-UiElement 'Pane1' | Out-Null
            Wait-UiTabCount 2 -Pane 0
            Assert-UiEqual 'UiTestRoot|Alpha' ((Get-UiTabNames -Pane 0) -join '|') 'Pane0 のタブ:'
            Wait-UiValue 'AddressTextBox' (Get-FixturePath '') -Pane 0
            Wait-UiValue 'AddressTextBox' (Get-FixturePath 'Beta') -Pane 1
            Wait-UiFileRows @('beta.csv') -Pane 1
        }
        '表示していなかったタブは初めて表示したときに読み込まれる' = {
            Select-UiTab 1 -Pane 0
            Wait-UiValue 'AddressTextBox' (Get-FixturePath 'Alpha') -Pane 0
            Wait-UiFileRows @('Sub1', 'alpha1.txt', 'alpha2.md') -Pane 0
        }
        '未購読で起動すると1画面・タブなしで開く' = {
            Restart-UiApp -Plus $false
            Assert-UiTrue (-not (Test-UiElementExists 'Pane1')) '未購読なのに2画面で開きました。'
            Assert-UiTrue (-not (Test-UiElementExists 'TabItemsControl')) '未購読なのにタブ列が出ています。'
        }
        '未購読の間に移動しても、購読に戻ると保存済みの構成が戻る' = {
            Invoke-UiClick 'Gamma' -Type DataItem -Double
            Wait-UiValue 'AddressTextBox' (Get-FixturePath 'Gamma')
            Restart-UiApp -Plus $true
            Wait-UiElement 'Pane1' | Out-Null
            Assert-UiEqual 'UiTestRoot|Alpha' ((Get-UiTabNames -Pane 0) -join '|') 'Pane0 のタブ:'
            Wait-UiValue 'AddressTextBox' (Get-FixturePath 'Beta') -Pane 1
        }
    }
}
