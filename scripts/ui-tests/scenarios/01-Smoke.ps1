# 起動直後の表示: ルートの直下一覧・アドレス欄・ツリーが出ること
$Scenario = @{
    Plus  = $false
    Steps = [ordered]@{
        'ルートの直下一覧が出る（フォルダが先・名前順、隠しファイルも既定で表示）' = {
            Wait-UiFileRows @('Alpha', 'Beta', 'Gamma', 'hidden.txt', 'readme.txt')
        }
        'アドレス欄がルートを指す' = {
            Wait-UiValue 'AddressTextBox' (Get-FixturePath '')
        }
        'ツリーに Folders とルートが出る' = {
            Assert-UiTrue (Test-UiElementExists 'Folders') 'ツリーに Folders がありません。'
            Assert-UiTrue (Test-UiElementExists 'UiTestRoot') 'ツリーにルートがありません。'
        }
        '最初は戻る・進むが押せず、上へは押せる' = {
            Assert-UiTrue (-not (Test-UiElementEnabled 'BackButton')) '戻るが押せる状態です。'
            Assert-UiTrue (-not (Test-UiElementEnabled 'ForwardButton')) '進むが押せる状態です。'
            Assert-UiTrue (Test-UiElementEnabled 'UpButton') '上へが押せない状態です。'
        }
    }
}
