# フォルダ移動: 一覧のダブルクリック・戻る/進む/上へ・アドレス欄・ツリー選択
$Scenario = @{
    Plus  = $false
    Steps = [ordered]@{
        'フォルダ行のダブルクリックで開く' = {
            Wait-UiFileRows @('Alpha', 'Beta', 'Gamma', 'hidden.txt', 'readme.txt')
            Invoke-UiClick 'Alpha' -Type DataItem -Double
            Wait-UiValue 'AddressTextBox' (Get-FixturePath 'Alpha')
            Wait-UiFileRows @('Sub1', 'alpha1.txt', 'alpha2.md')
        }
        '戻るで元のフォルダへ戻る' = {
            Invoke-UiElement 'BackButton'
            Wait-UiValue 'AddressTextBox' (Get-FixturePath '')
            Wait-UiFileRows @('Alpha', 'Beta', 'Gamma', 'hidden.txt', 'readme.txt')
            Assert-UiTrue (Test-UiElementEnabled 'ForwardButton') '戻ったあと進むが押せません。'
        }
        '進むで再び開く' = {
            Invoke-UiElement 'ForwardButton'
            Wait-UiValue 'AddressTextBox' (Get-FixturePath 'Alpha')
            Wait-UiFileRows @('Sub1', 'alpha1.txt', 'alpha2.md')
        }
        '上へで親フォルダへ移る' = {
            Invoke-UiElement 'UpButton'
            Wait-UiValue 'AddressTextBox' (Get-FixturePath '')
            Wait-UiFileRows @('Alpha', 'Beta', 'Gamma', 'hidden.txt', 'readme.txt')
        }
        'アドレス欄に入力してEnterで移る' = {
            Set-UiValue 'AddressTextBox' (Get-FixturePath 'Beta')
            Send-UiKeys 'enter' -Target 'AddressTextBox'
            Wait-UiFileRows @('beta.csv')
        }
        'ツリーで選んだフォルダを開く' = {
            Invoke-UiElement 'UiTestRoot' -Type TreeItem -Action expand
            Invoke-UiElement 'Gamma' -Type TreeItem -Action select
            Wait-UiValue 'AddressTextBox' (Get-FixturePath 'Gamma')
            Wait-UiFileRows @()
        }
    }
}
