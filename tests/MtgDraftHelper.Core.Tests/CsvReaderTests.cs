using MtgDraftHelper.Core.Csv;

namespace MtgDraftHelper.Core.Tests;

public class CsvReaderTests
{
    private static List<string[]> ReadAll(string text)
    {
        using var csv = new CsvReader(new StringReader(text));
        var rows = new List<string[]>();
        while (csv.Read())
            rows.Add(Enumerable.Range(0, csv.FieldCount).Select(csv.GetString).ToArray());
        return rows;
    }

    [Fact]
    public void ReadsSimpleRows()
    {
        var rows = ReadAll("a,b,c\n1,2,3\n");
        Assert.Equal([["a", "b", "c"], ["1", "2", "3"]], rows);
    }

    [Fact]
    public void HandlesQuotedFieldsWithCommasAndQuotes()
    {
        var rows = ReadAll("deck_Emrakul,\"deck_Emrakul, the Exigent Doom\",\"say \"\"hi\"\"\"\n");
        Assert.Equal(["deck_Emrakul", "deck_Emrakul, the Exigent Doom", "say \"hi\""], rows.Single());
    }

    [Fact]
    public void HandlesCrLfAndEmptyFields()
    {
        var rows = ReadAll("a,,c\r\n,x,\r\n");
        Assert.Equal([["a", "", "c"], ["", "x", ""]], rows);
    }

    [Fact]
    public void LastRowWithoutNewline()
    {
        var rows = ReadAll("a,b\n1,2");
        Assert.Equal(["1", "2"], rows[1]);
    }

    [Fact]
    public void GetIntParsesNumbersAndTreatsEmptyAsZero()
    {
        using var csv = new CsvReader(new StringReader("3,,x\n"));
        Assert.True(csv.Read());
        Assert.Equal(3, csv.GetInt(0));
        Assert.Equal(0, csv.GetInt(1));
        Assert.Equal(0, csv.GetInt(2));
        Assert.Equal(0, csv.GetInt(10));
    }

    [Fact]
    public void ReadsRecordsLargerThanInternalBuffers()
    {
        var wide = string.Join(',', Enumerable.Range(0, 5000));
        var rows = ReadAll(wide + "\n" + wide + "\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal(5000, rows[1].Length);
        Assert.Equal("4999", rows[1][^1]);
    }
}
