using System.Net.Http.Headers;
using System.Text.Json;

namespace RiroganDesktop;

internal sealed class ReleaseChannel : IDisposable
{
    internal const string InstanceName = @"Local\Rirogan.Desktop.D9B4E0E6";
    internal const int RestartForUpdate = 42;

    private static readonly Uri Origin = new("https://software.rirogan.com/");
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    internal static bool TryParseVersion(string? value, out Version version)
    {
        if (value is { Length: <= 17 } && Version.TryParse(value, out var parsed) &&
            parsed.Build >= 0 && parsed.Revision == -1 && parsed.ToString() == value &&
            parsed.Major <= ushort.MaxValue && parsed.Minor <= ushort.MaxValue && parsed.Build <= ushort.MaxValue)
        {
            version = parsed;
            return true;
        }
        version = new Version(0, 0, 0);
        return false;
    }

    internal async Task<Version> ReadVersionAsync(CancellationToken token)
    {
        using var json = await ReadJsonAsync("stable.json", 4096, token).ConfigureAwait(false);
        if (json.RootElement.ValueKind != JsonValueKind.Object ||
            !json.RootElement.TryGetProperty("version", out var value) ||
            value.ValueKind != JsonValueKind.String || !TryParseVersion(value.GetString(), out var version))
            throw new InvalidDataException("O arquivo stable.json não contém uma versão válida.");
        return version;
    }

    internal async Task<JsonDocument> ReadJsonAsync(string path, int limit, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await GetAsync(path, timeout.Token, fresh: true).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength > limit)
            throw new InvalidDataException("O manifesto publicado excede o tamanho esperado.");

        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        var buffer = new byte[limit + 1];
        int count = await body.ReadAtLeastAsync(buffer, buffer.Length, false, timeout.Token).ConfigureAwait(false);
        if (count > limit)
            throw new InvalidDataException("O manifesto publicado excede o tamanho esperado.");

        try { return JsonDocument.Parse(buffer.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 8 }); }
        catch (JsonException ex) { throw new InvalidDataException("O manifesto de atualização está inválido.", ex); }
    }

    internal async Task<HttpResponseMessage> GetAsync(string path, CancellationToken token, bool fresh = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Origin, path));
        if (fresh) request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.OK) return response;
        var status = (int)response.StatusCode;
        response.Dispose();
        throw new HttpRequestException($"Não foi possível baixar {path} (HTTP {status}).");
    }

    public void Dispose() => http.Dispose();
}
