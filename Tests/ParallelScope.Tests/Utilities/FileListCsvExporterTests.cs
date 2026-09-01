using System.IO;
using System.Text;
using ParallelScope.Tests.TestSupport;
using ParallelScope.Utilities;
using ParallelScope.ViewModels;

namespace ParallelScope.Tests.Utilities;

public class FileListCsvExporterTests
{
    private static FileItemViewModel CreateFileItem(
        string fullPath,
        long sizeBytes,
        DateTime modifiedAt,
        string typeText = "TXT File")
    {
        var name = Path.GetFileName(fullPath);
        return new FileItemViewModel(fullPath, name, sizeBytes, modifiedAt)
        {
            TypeText = typeText,
            AttributesText = "A"
        };
    }

    [Fact]
    public void Export_WritesHeaderAndRowsForSelectedColumns()
    {
        using var temp = new TempDirectory();
        var csvPath = temp.Combine("list.csv");
        var items = new[]
        {
            CreateFileItem(@"C:\Root\a.txt", 1536, new DateTime(2026, 1, 2, 3, 4, 5)),
            CreateFileItem(@"C:\Root\Sub\b.txt", 0, new DateTime(2026, 12, 31, 23, 59, 59))
        };

        FileListCsvExporter.Export(
            csvPath,
            items,
            new[] { FileListColumns.Location, FileListColumns.Type, FileListColumns.Size, FileListColumns.Modified },
            sizeInBytes: false);

        var lines = File.ReadAllLines(csvPath);

        Assert.Equal(3, lines.Length);
        Assert.Equal("Name,Location,Type,Size,Modified", lines[0]);
        Assert.Equal(@"a.txt,C:\Root,TXT File,1.5 KB,2026-01-02 03:04:05", lines[1]);
        Assert.Equal(@"b.txt,C:\Root\Sub,TXT File,0 B,2026-12-31 23:59:59", lines[2]);
    }

    [Fact]
    public void Export_WritesRawByteCountAndUnitHeaderWhenSizeInBytes()
    {
        using var temp = new TempDirectory();
        var csvPath = temp.Combine("list.csv");
        var items = new[] { CreateFileItem(@"C:\Root\a.txt", 1536, new DateTime(2026, 1, 2, 3, 4, 5)) };

        FileListCsvExporter.Export(csvPath, items, new[] { FileListColumns.Size }, sizeInBytes: true);

        var lines = File.ReadAllLines(csvPath);

        // 単位付きの表示と区別できるよう、生バイト数のときは見出しにも単位が付く
        Assert.Equal("Name,Size (bytes)", lines[0]);
        Assert.Equal("a.txt,1536", lines[1]);
    }

    [Fact]
    public void Export_QuotesOnlyFieldsContainingSeparatorQuoteOrNewLine()
    {
        using var temp = new TempDirectory();
        var csvPath = temp.Combine("list.csv");
        var items = new[]
        {
            CreateFileItem(@"C:\Root\a,b.txt", 1, new DateTime(2026, 1, 1)),
            CreateFileItem("C:\\Root\\quote\"name.txt", 1, new DateTime(2026, 1, 1)),
            CreateFileItem("C:\\Root\\line\nbreak.txt", 1, new DateTime(2026, 1, 1))
        };

        FileListCsvExporter.Export(csvPath, items, new[] { FileListColumns.Type }, sizeInBytes: false);

        var text = File.ReadAllText(csvPath);

        Assert.Contains("\"a,b.txt\",TXT File", text);
        Assert.Contains("\"quote\"\"name.txt\",TXT File", text);
        Assert.Contains("\"line\nbreak.txt\",TXT File", text);
        // エスケープ不要な値は引用符で囲まない（常に囲むとExcelが全列を文字列として扱う）
        Assert.DoesNotContain("\"TXT File\"", text);
    }

    [Fact]
    public void Export_WritesUtf8Bom()
    {
        using var temp = new TempDirectory();
        var csvPath = temp.Combine("list.csv");

        FileListCsvExporter.Export(csvPath, Array.Empty<FileItemViewModel>(), Array.Empty<string>(), sizeInBytes: false);

        // BOMが無いとExcelがUTF-8と判別できず、日本語のパスやファイル名が文字化けする
        var bytes = File.ReadAllBytes(csvPath);
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes.Take(3).ToArray());
    }

    [Fact]
    public void Export_WritesEmptyFieldForColumnsWithoutValue()
    {
        using var temp = new TempDirectory();
        var csvPath = temp.Combine("list.csv");
        // 作成日時はキャッシュに値が無い間（次のスキャンまで）nullで、表示も空になる
        var items = new[] { CreateFileItem(@"C:\Root\a.txt", 1, new DateTime(2026, 1, 1)) };

        FileListCsvExporter.Export(
            csvPath,
            items,
            new[] { FileListColumns.Created, FileListColumns.Attributes },
            sizeInBytes: false);

        var lines = File.ReadAllLines(csvPath);

        Assert.Equal("Name,Created,Attributes", lines[0]);
        Assert.Equal("a.txt,,A", lines[1]);
    }

    [Fact]
    public void Export_ThrowsWhenCancelled()
    {
        using var temp = new TempDirectory();
        var csvPath = temp.Combine("list.csv");
        var items = new[] { CreateFileItem(@"C:\Root\a.txt", 1, new DateTime(2026, 1, 1)) };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            FileListCsvExporter.Export(csvPath, items, new[] { FileListColumns.Size }, sizeInBytes: false, cts.Token));
    }
}
