using System.Net;

namespace MtgDraftHelper.Core.Ratings;

/// <summary>
/// Stahování veřejných datasetů 17Lands (licence CC BY 4.0) z
/// https://www.17lands.com/public_datasets. API webu 17Lands se nepoužívá.
/// </summary>
public sealed class PublicDatasetClient
{
    public const string BaseUrl = "https://17lands-public.s3.amazonaws.com/analysis_data";

    private readonly HttpClient _http;

    public PublicDatasetClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("MtgDraftHelper/0.1 (+https://github.com/dvorakfilip-bit/mtg-draft-helper)");
    }

    public static Uri DatasetUri(string setCode, string format, DatasetKind kind)
    {
        var folder = kind == DatasetKind.Game ? "game_data" : "draft_data";
        return new Uri($"{BaseUrl}/{folder}/{folder}_public.{Uri.EscapeDataString(setCode)}.{Uri.EscapeDataString(format)}.csv.gz");
    }

    /// <summary>Datum poslední změny datasetu, nebo null, pokud dataset neexistuje.</summary>
    public async Task<RemoteDataset?> GetInfoAsync(string setCode, string format, DatasetKind kind, CancellationToken cancellationToken = default)
    {
        var uri = DatasetUri(setCode, format, kind);
        using var request = new HttpRequestMessage(HttpMethod.Head, uri);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // S3 vrací pro neexistující soubor 403.
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            return null;
        response.EnsureSuccessStatusCode();

        return new RemoteDataset(uri, response.Content.Headers.LastModified, response.Content.Headers.ContentLength);
    }

    /// <summary>Stáhne dataset do souboru (nejdřív do .part, po dokončení přejmenuje).</summary>
    public async Task DownloadAsync(Uri uri, string targetPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var partPath = targetPath + ".part";

        using (var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var target = File.Create(partPath);

            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                done += read;
                if (total > 0)
                    progress?.Report((double)done / total.Value);
            }
        }

        File.Move(partPath, targetPath, overwrite: true);
    }
}

public sealed record RemoteDataset(Uri Uri, DateTimeOffset? LastModified, long? Size);
