using System.IO.Compression;
using System.Text;
using MtgDraftHelper.Core.Csv;
using MtgDraftHelper.Core.Storage;

namespace MtgDraftHelper.Core.Ratings;

public enum DatasetUpdateStatus
{
    /// <summary>Dataset na 17Lands neexistuje.</summary>
    NotAvailable,

    /// <summary>Lokální data odpovídají aktuální verzi datasetu.</summary>
    UpToDate,

    /// <summary>Dataset byl stažen a zpracován.</summary>
    Updated,
}

public sealed record DatasetUpdateResult(DatasetKind Kind, DatasetUpdateStatus Status, long Rows, TimeSpan Duration);

/// <summary>
/// Aktualizace a čtení statistik 17Lands: zjistí verzi datasetu, při změně ho stáhne,
/// zpracuje a uloží do <see cref="StatsStore"/>. Stažený soubor se po zpracování maže.
/// </summary>
public sealed class RatingsService
{
    private readonly StatsStore _store;
    private readonly PublicDatasetClient _client;
    private readonly string _cacheDir;
    private readonly double _topThreshold;

    public RatingsService(StatsStore store, PublicDatasetClient client, string cacheDir, double topWinRateThreshold = RatingsDefaults.TopWinRateThreshold)
    {
        _store = store;
        _client = client;
        _cacheDir = cacheDir;
        _topThreshold = topWinRateThreshold;
    }

    public StatsStore Store => _store;

    public async Task<IReadOnlyList<DatasetUpdateResult>> UpdateAsync(
        string setCode, string format, bool force = false, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var results = new List<DatasetUpdateResult>();
        foreach (var kind in new[] { DatasetKind.Game, DatasetKind.Draft })
            results.Add(await UpdateOneAsync(setCode, format, kind, force, progress, cancellationToken).ConfigureAwait(false));
        return results;
    }

    private async Task<DatasetUpdateResult> UpdateOneAsync(
        string setCode, string format, DatasetKind kind, bool force, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        var remote = await _client.GetInfoAsync(setCode, format, kind, cancellationToken).ConfigureAwait(false);
        if (remote is null)
        {
            progress?.Report($"{kind}: dataset {setCode} {format} neexistuje.");
            return new DatasetUpdateResult(kind, DatasetUpdateStatus.NotAvailable, 0, TimeSpan.Zero);
        }

        var local = _store.GetDatasetInfo(setCode, format, kind);
        if (!force && local is not null && local.LastModified == remote.LastModified)
        {
            progress?.Report($"{kind}: aktuální (verze {remote.LastModified:yyyy-MM-dd}).");
            return new DatasetUpdateResult(kind, DatasetUpdateStatus.UpToDate, local.Rows, TimeSpan.Zero);
        }

        var file = Path.Combine(_cacheDir, Path.GetFileName(remote.Uri.LocalPath));
        progress?.Report($"{kind}: stahuji {remote.Size / (1024 * 1024)} MB…");
        await _client.DownloadAsync(remote.Uri, file, cancellationToken: cancellationToken).ConfigureAwait(false);

        try
        {
            progress?.Report($"{kind}: zpracovávám…");
            var rows = await Task.Run(() => Process(file, setCode, format, kind, remote.LastModified, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            progress?.Report($"{kind}: hotovo, {rows:N0} řádků.");
            return new DatasetUpdateResult(kind, DatasetUpdateStatus.Updated, rows, DateTime.UtcNow - started);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private long Process(string file, string setCode, string format, DatasetKind kind, DateTimeOffset? lastModified, CancellationToken cancellationToken)
    {
        using var csv = OpenGzipCsv(file);
        if (kind == DatasetKind.Game)
        {
            var result = new GameDataAggregator(_topThreshold).Aggregate(csv, cancellationToken);
            _store.SaveGameData(setCode, format, result, lastModified);
            return result.Rows;
        }
        else
        {
            var result = new DraftDataAggregator(_topThreshold).Aggregate(csv, cancellationToken);
            _store.SaveDraftData(setCode, format, result, lastModified);
            return result.Rows;
        }
    }

    public static CsvReader OpenGzipCsv(string path)
    {
        var stream = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        return new CsvReader(new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 16));
    }

    /// <summary>
    /// Formát, ze kterého se pro daný event berou statistiky: první formát z řady záloh,
    /// pro který jsou lokálně zpracovaná herní data. Null, pokud žádná data nejsou.
    /// </summary>
    public string? ResolveFormat(string setCode, string format) =>
        RatingsDefaults.FormatFallbacks(format)
            .FirstOrDefault(f => _store.GetDatasetInfo(setCode, f, DatasetKind.Game) is not null);
}
