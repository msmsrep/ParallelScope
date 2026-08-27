using System.Globalization;
using System.IO;
using System.Text;
using ParallelScope.ViewModels;

namespace ParallelScope.Utilities;

/// <summary>
/// ファイル一覧の表示内容をCSVへ書き出す。
/// 出力する列・行順は画面の表示に合わせる。値も表示中の文字列のままだが、
/// サイズだけは表計算ソフトで集計・並べ替えできるよう生のバイト数を選べる。
/// </summary>
public static class FileListCsvExporter
{
    // BOMが無いとExcelがUTF-8と判別できず、日本語のパスやファイル名が文字化けする
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>
    /// 指定パスへCSVを書き出す。数十万行になり得るため、UIスレッドではなくバックグラウンドから呼ぶこと。
    /// </summary>
    /// <param name="filePath">出力先のファイルパス。</param>
    /// <param name="items">出力する行（画面の表示順のスナップショット）。</param>
    /// <param name="optionalColumns">Name以外に出力する列キー（<see cref="FileListColumns"/>）。</param>
    /// <param name="sizeInBytes">
    /// trueならSize列を生のバイト数（例: 12345678）で、falseなら表示中の文字列（例: 11.8 MB）で出力する。
    /// </param>
    public static void Export(
        string filePath,
        IReadOnlyList<FileItemViewModel> items,
        IReadOnlyList<string> optionalColumns,
        bool sizeInBytes,
        CancellationToken token = default)
    {
        using var writer = new StreamWriter(filePath, append: false, Utf8WithBom);
        var builder = new StringBuilder();

        AppendField(builder, "Name", isFirst: true);
        foreach (var column in optionalColumns)
        {
            // 単位付きの表示と区別できるよう、生バイト数のときは見出しにも単位を書く
            var header = sizeInBytes && column == FileListColumns.Size ? "Size (bytes)" : column;
            AppendField(builder, header, isFirst: false);
        }

        writer.WriteLine(builder);

        foreach (var item in items)
        {
            token.ThrowIfCancellationRequested();

            builder.Clear();
            AppendField(builder, item.Name, isFirst: true);
            foreach (var column in optionalColumns)
            {
                AppendField(builder, GetColumnText(item, column, sizeInBytes), isFirst: false);
            }

            writer.WriteLine(builder);
        }
    }

    // 表示中の列に対応する値を取り出す（表示用の文字列はViewModel側で生成される）
    private static string GetColumnText(FileItemViewModel item, string column, bool sizeInBytes) => column switch
    {
        FileListColumns.Location => item.Location,
        FileListColumns.Type => item.TypeText,
        // 生バイト数は桁区切りを付けず、ロケールに依存しない表記で出す（表計算ソフトが数値として読めるように）
        FileListColumns.Size => sizeInBytes
            ? item.SizeBytes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
            : item.SizeText,
        FileListColumns.Modified => item.ModifiedTime,
        FileListColumns.Created => item.CreatedTime,
        FileListColumns.Attributes => item.AttributesText,
        _ => string.Empty
    };

    // RFC 4180準拠のエスケープ。区切り・引用符・改行を含む場合のみ引用符で囲む
    // （常に囲むとExcelが全列を文字列として扱ってしまう）
    private static void AppendField(StringBuilder builder, string value, bool isFirst)
    {
        if (!isFirst)
        {
            builder.Append(',');
        }

        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
        {
            builder.Append(value);
            return;
        }

        builder.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
    }
}
