using Microsoft.Data.Sqlite;
using ParallelScope.Data;

namespace ParallelScope.Tests.Data;

/// <summary>
/// 日時列の手書きの解析が、Microsoft.Data.Sqlite の GetDateTime と同じ値・同じ Kind を返すことを確かめる
/// （食い違うと、索引の有無や読み出し経路で一覧の日時がずれる）。
/// </summary>
public class SqliteDateTimeParseTests
{
    [Theory]
    [InlineData("2026-11-25 20:38:58")]
    [InlineData("2026-11-25 20:38:58.1")]
    [InlineData("2026-11-25 20:38:58.123")]
    [InlineData("2026-11-25 20:38:58.1234567")]
    [InlineData("2000-02-29 00:00:00.0000001")]
    [InlineData("0001-01-01 00:00:00")]
    [InlineData("9999-12-31 23:59:59.9999999")]
    public void ReadDateTime_MatchesGetDateTime(string text)
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT @v";
        cmd.Parameters.AddWithValue("@v", text);
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());

        var expected = reader.GetDateTime(0);
        var actual = FileCacheRepository.ReadDateTime(reader, 0);

        Assert.Equal(expected, actual);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.True(FileCacheRepository.TryParseSqliteDateTime(text, out _));
    }

    [Theory]
    [InlineData("2026-11-25T20:38:58")]
    [InlineData("2026-11-25 20:38:58Z")]
    [InlineData("2026-11-25 20:38:58.")]
    [InlineData("2026-11-25 20:38:58.12345678")]
    [InlineData("2026-02-30 00:00:00")]
    [InlineData("2026-11-25 20:38:5x")]
    public void TryParseSqliteDateTime_RejectsOtherFormats(string text)
    {
        // 想定外の書式は既定の解析に任せる（ここで誤った値を作らない）
        Assert.False(FileCacheRepository.TryParseSqliteDateTime(text, out _));
    }
}
