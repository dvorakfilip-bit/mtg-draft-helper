using System.Text;

namespace MtgDraftHelper.Core.Logs;

/// <summary>
/// Čte nové řádky z rostoucího souboru. Pozná přepsání souboru (Arena při startu Player.log
/// zkrátí) a začne číst znovu od začátku. Nedokončený poslední řádek vrací až po dopsání.
/// </summary>
public sealed class LogTailer
{
    private readonly string _path;
    private readonly Decoder _decoder = new UTF8Encoding(false).GetDecoder();
    private readonly StringBuilder _partial = new();
    private long _position;
    private byte[]? _head;

    public LogTailer(string path) => _path = path;

    public string Path => _path;

    /// <summary>Soubor byl od posledního čtení přepsán; volající má zahodit stav.</summary>
    public event Action? Rewritten;

    /// <summary>Přečte všechny dokončené řádky přidané od posledního volání.</summary>
    public IReadOnlyList<string> ReadNewLines()
    {
        var lines = new List<string>();
        if (!File.Exists(_path))
            return lines;

        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        if (IsRewritten(stream))
        {
            _position = 0;
            _partial.Clear();
            _decoder.Reset();
            _head = null;
            Rewritten?.Invoke();
        }

        if (_head is null)
            _head = ReadHead(stream);

        stream.Position = _position;
        var bytes = new byte[1 << 16];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        int read;
        while ((read = stream.Read(bytes, 0, bytes.Length)) > 0)
        {
            _position += read;
            var count = _decoder.GetChars(bytes, 0, read, chars, 0);
            for (var i = 0; i < count; i++)
            {
                var c = chars[i];
                if (c == '\n')
                {
                    if (_partial.Length > 0 && _partial[^1] == '\r')
                        _partial.Length--;
                    lines.Add(_partial.ToString());
                    _partial.Clear();
                }
                else
                {
                    _partial.Append(c);
                }
            }
        }
        return lines;
    }

    /// <summary>Soubor je kratší než přečtená část, nebo se změnil jeho začátek.</summary>
    private bool IsRewritten(FileStream stream)
    {
        if (_position == 0)
            return false;
        if (stream.Length < _position)
            return true;
        if (_head is null)
            return false;
        var current = ReadHead(stream);
        return !current.AsSpan(0, Math.Min(current.Length, _head.Length)).SequenceEqual(_head.AsSpan(0, Math.Min(current.Length, _head.Length)));
    }

    private static byte[] ReadHead(FileStream stream)
    {
        stream.Position = 0;
        var head = new byte[(int)Math.Min(256, stream.Length)];
        var read = stream.Read(head, 0, head.Length);
        return head[..read];
    }
}
