namespace MtgDraftHelper.Core.Csv;

/// <summary>
/// Rychlá proudová čtečka CSV (RFC 4180). Záznam drží v interním bufferu, pole se čtou jako
/// <see cref="ReadOnlySpan{T}"/> bez alokace – datasety 17Lands mají stovky sloupců a miliony řádků.
/// </summary>
public sealed class CsvReader : IDisposable
{
    private readonly TextReader _reader;
    private readonly char[] _input = new char[1 << 16];
    private int _inputPos;
    private int _inputLen;

    private char[] _record = new char[4096];
    private int _recordLen;
    private int[] _starts = new int[64];
    private int[] _lengths = new int[64];

    public CsvReader(TextReader reader) => _reader = reader;

    public int FieldCount { get; private set; }

    public ReadOnlySpan<char> GetSpan(int index) =>
        (uint)index < (uint)FieldCount ? _record.AsSpan(_starts[index], _lengths[index]) : [];

    public string GetString(int index) => GetSpan(index).ToString();

    /// <summary>Celé číslo pole; prázdné nebo nečíselné pole je 0.</summary>
    public int GetInt(int index) => int.TryParse(GetSpan(index), out var value) ? value : 0;

    /// <summary>Načte další záznam. Vrací false na konci souboru.</summary>
    public bool Read()
    {
        FieldCount = 0;
        _recordLen = 0;

        var c = Next();
        if (c < 0)
            return false;

        while (true)
        {
            var start = _recordLen;
            if (c == '"')
            {
                // Pole v uvozovkách: "" uvnitř znamená jednu uvozovku.
                while (true)
                {
                    c = Next();
                    if (c < 0)
                        break;
                    if (c == '"')
                    {
                        if (Peek() == '"')
                        {
                            Next();
                            Append('"');
                            continue;
                        }
                        c = Next();
                        break;
                    }
                    Append((char)c);
                }
                // Případné znaky mezi koncovou uvozovkou a oddělovačem připojíme beze změny.
                while (c >= 0 && c != ',' && c != '\n' && c != '\r')
                {
                    Append((char)c);
                    c = Next();
                }
            }
            else
            {
                while (c >= 0 && c != ',' && c != '\n' && c != '\r')
                {
                    Append((char)c);
                    c = Next();
                }
            }

            AddField(start, _recordLen - start);

            if (c == ',')
            {
                c = Next();
                if (c < 0 || c == '\n' || c == '\r')
                {
                    AddField(_recordLen, 0); // prázdné poslední pole
                    SkipLineEnd(c);
                    return true;
                }
                continue;
            }

            SkipLineEnd(c);
            return true;
        }
    }

    private void SkipLineEnd(int c)
    {
        if (c == '\r' && Peek() == '\n')
            Next();
    }

    private void Append(char c)
    {
        if (_recordLen == _record.Length)
            Array.Resize(ref _record, _record.Length * 2);
        _record[_recordLen++] = c;
    }

    private void AddField(int start, int length)
    {
        if (FieldCount == _starts.Length)
        {
            Array.Resize(ref _starts, _starts.Length * 2);
            Array.Resize(ref _lengths, _lengths.Length * 2);
        }
        _starts[FieldCount] = start;
        _lengths[FieldCount] = length;
        FieldCount++;
    }

    private int Next()
    {
        if (_inputPos >= _inputLen && !Fill())
            return -1;
        return _input[_inputPos++];
    }

    private int Peek()
    {
        if (_inputPos >= _inputLen && !Fill())
            return -1;
        return _input[_inputPos];
    }

    private bool Fill()
    {
        _inputLen = _reader.Read(_input, 0, _input.Length);
        _inputPos = 0;
        return _inputLen > 0;
    }

    public void Dispose() => _reader.Dispose();
}
