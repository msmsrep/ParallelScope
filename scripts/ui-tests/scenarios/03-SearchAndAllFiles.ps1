# インクリメンタルサーチとAll Files（どちらもキャッシュDBから引くため、起動時のスキャン完了後に確かめる）
$Scenario = @{
    Plus  = $false
    Steps = [ordered]@{
        '検索語を打つと配下から名前で絞り込まれる' = {
            Wait-UiFileRows @('Alpha', 'Beta', 'Gamma', 'hidden.txt', 'readme.txt')
            Set-UiValue 'SearchTextBox' 'alpha'
            Wait-UiFileRows @('Alpha', 'alpha1.txt', 'alpha2.md')
        }
        '打ち足すとさらに絞り込まれる' = {
            Set-UiValue 'SearchTextBox' 'alpha1'
            Wait-UiFileRows @('alpha1.txt')
        }
        '大文字小文字を区別せず、深い階層も対象になる' = {
            Set-UiValue 'SearchTextBox' 'DEEP'
            Wait-UiFileRows @('deep.log')
        }
        '一致しなければ空になる' = {
            Set-UiValue 'SearchTextBox' 'zzz-no-such-file'
            Wait-UiFileRows @()
        }
        '検索欄を空にすると直下一覧に戻る' = {
            Set-UiValue 'SearchTextBox' ''
            Wait-UiFileRows @('Alpha', 'Beta', 'Gamma', 'hidden.txt', 'readme.txt')
        }
        'All Files をONにすると配下の全ファイルが出る（フォルダは出ない）' = {
            Invoke-UiElement 'AllFilesToggleButton' -Action toggle-on
            Wait-UiFileRows @('alpha1.txt', 'alpha2.md', 'beta.csv', 'deep.log', 'hidden.txt', 'readme.txt') -AnyOrder
        }
        'All Files 中の検索はファイルだけを対象にする' = {
            Set-UiValue 'SearchTextBox' 'beta'
            Wait-UiFileRows @('beta.csv')
            Set-UiValue 'SearchTextBox' ''
            Wait-UiFileRows @('alpha1.txt', 'alpha2.md', 'beta.csv', 'deep.log', 'hidden.txt', 'readme.txt') -AnyOrder
        }
        'All Files をOFFにすると直下一覧に戻る' = {
            Invoke-UiElement 'AllFilesToggleButton' -Action toggle-off
            Wait-UiFileRows @('Alpha', 'Beta', 'Gamma', 'hidden.txt', 'readme.txt')
        }
    }
}
