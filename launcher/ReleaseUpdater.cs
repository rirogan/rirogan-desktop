using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RiroganDesktop;

internal sealed class ReleaseUpdater : IDisposable
{
    internal const string DesktopExecutable = "Rirogan.Desktop.exe";

    private const int ManifestLimit = 512 * 1024;
    private const long FileLimit = 300L * 1024 * 1024;
    private const long ReleaseLimit = 800L * 1024 * 1024;
    private static readonly Regex SafeSegment = new(@"\A[a-zA-Z0-9_\-][a-zA-Z0-9._\-]*\z", RegexOptions.CultureInvariant);
    private static readonly Regex ReservedName = new(@"\A(?:CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|\z)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    internal static string CacheRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rirogan", "Launcher");

    private readonly ReleaseChannel channel = new();
    private readonly string baseline = Path.Combine(AppContext.BaseDirectory, "App");
    private readonly string releases = Path.Combine(CacheRoot, "Releases");

    private sealed record Asset(string Path, long Size, string Sha256);

    internal async Task<string> PrepareAsync(IProgress<string> progress, CancellationToken token)
    {
        var version = await channel.ReadVersionAsync(token);
        Directory.CreateDirectory(releases);
        RemoveInterruptedWork();
        var installed = InstalledReleases();
        var newest = installed.Count > 0 ? installed[0].Version : Program.MinimumDesktopVersion;
        if (version < newest || version < Program.MinimumDesktopVersion)
            throw new InvalidDataException($"O servidor publicou a versão {version}, anterior à versão já instalada.");

        var prefix = $"releases/{version}/";
        using var manifest = await channel.ReadJsonAsync(prefix + "files.json", ManifestLimit, token);
        var assets = ReadAssets(manifest.RootElement, version);
        var destination = Path.Combine(releases, version.ToString());

        progress.Report($"Conferindo os arquivos do Rirogan {version}…");
        if (await IsCompleteAsync(destination, assets, version, token))
            return Path.Combine(destination, DesktopExecutable);
        if (await IsCompleteAsync(baseline, assets, version, token))
            return Path.Combine(baseline, DesktopExecutable);

        var sources = installed.Select(release => release.Folder).Append(baseline).ToArray();
        var stage = Path.Combine(releases, ".stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            int completed = 0;
            foreach (var asset in assets)
            {
                token.ThrowIfCancellationRequested();
                var target = Path.Combine(stage, asset.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                bool copied = false;
                foreach (var source in sources)
                {
                    var local = Path.Combine(source, asset.Path);
                    if (!await MatchesAsync(local, asset, token)) continue;
                    File.Copy(local, target);
                    if (await MatchesAsync(target, asset, token))
                    {
                        copied = true;
                        break;
                    }
                    File.Delete(target);
                }

                if (!copied)
                {
                    progress.Report($"Baixando arquivos alterados da versão {version}… ({completed + 1}/{assets.Count})");
                    await DownloadAsync(prefix + "files/" + asset.Path, target, asset, token);
                }
                completed++;
            }

            var executable = Path.Combine(stage, DesktopExecutable);
            if (!HasVersion(executable, version))
                throw new InvalidDataException("A versão do aplicativo baixado difere da versão publicada.");
            token.ThrowIfCancellationRequested();

            Commit(stage, destination);
            foreach (var old in InstalledReleases().Skip(2))
                TryDeleteDirectory(old.Folder);
            return Path.Combine(destination, DesktopExecutable);
        }
        finally
        {
            TryDeleteDirectory(stage);
        }
    }

    private static List<Asset> ReadAssets(JsonElement json, Version version)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("version", out var tag) ||
            tag.ValueKind != JsonValueKind.String || tag.GetString() != version.ToString() ||
            !json.TryGetProperty("files", out var items) || items.ValueKind != JsonValueKind.Array ||
            items.GetArrayLength() is < 1 or > 2048)
            throw new InvalidDataException("A lista de arquivos da atualização está inválida.");

        var files = new List<Asset>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("path", out var name) || name.ValueKind != JsonValueKind.String ||
                !item.TryGetProperty("size", out var length) || length.ValueKind != JsonValueKind.Number ||
                !length.TryGetInt64(out long bytes) || bytes is < 0 or > FileLimit ||
                !item.TryGetProperty("sha256", out var digest) || digest.ValueKind != JsonValueKind.String ||
                digest.GetString() is not { Length: 64 } hash || !hash.All(Uri.IsHexDigit) ||
                name.GetString() is not { Length: > 0 and <= 200 } path ||
                !IsSafePath(path) || !paths.Add(path))
                throw new InvalidDataException("A atualização contém um arquivo inválido ou duplicado.");
            total += bytes;
            if (total > ReleaseLimit) throw new InvalidDataException("A atualização excede o tamanho permitido.");
            files.Add(new Asset(path, bytes, hash));
        }

        foreach (var file in files)
        {
            int slash = file.Path.IndexOf('/');
            while (slash >= 0)
            {
                if (paths.Contains(file.Path[..slash]))
                    throw new InvalidDataException("A atualização contém caminhos de arquivo em conflito.");
                slash = file.Path.IndexOf('/', slash + 1);
            }
        }
        if (!paths.Contains(DesktopExecutable) || !paths.Contains("Rirogan.Desktop.dll") ||
            !paths.Contains("Rirogan.Desktop.runtimeconfig.json") || !paths.Contains("Rirogan.Desktop.deps.json"))
            throw new InvalidDataException("A atualização não contém os arquivos necessários para abrir o Rirogan.");
        return files;
    }

    private static bool IsSafePath(string path) => path.Split('/').All(segment =>
        SafeSegment.IsMatch(segment) && !segment.EndsWith('.') && !ReservedName.IsMatch(segment));

    private static async Task<bool> IsCompleteAsync(string folder, List<Asset> assets, Version version, CancellationToken token)
    {
        if (!Directory.Exists(folder)) return false;
        var paths = new HashSet<string>(assets.Select(asset => asset.Path), StringComparer.OrdinalIgnoreCase);
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) return false;
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false
        };
        foreach (string file in Directory.EnumerateFiles(folder, "*", enumeration))
        {
            var relative = Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '/');
            if (!paths.Remove(relative)) return false;
        }
        if (paths.Count != 0) return false;

        foreach (var asset in assets)
        {
            if (!await MatchesAsync(Path.Combine(folder, asset.Path), asset, token)) return false;
        }
        return HasVersion(Path.Combine(folder, DesktopExecutable), version);
    }

    private static async Task<bool> MatchesAsync(string file, Asset asset, CancellationToken token)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length != asset.Size) return false;
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await SHA256.HashDataAsync(stream, token);
            return string.Equals(Convert.ToHexString(hash), asset.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private async Task DownloadAsync(string url, string file, Asset asset, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var response = await channel.GetAsync(url, timeout.Token);
        if (response.Content.Headers.ContentLength is long length && length != asset.Size)
            throw new InvalidDataException($"O tamanho de {asset.Path} difere do manifesto.");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        await using var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[128 * 1024];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(), timeout.Token)) > 0)
        {
            total += read;
            if (total > asset.Size) throw new InvalidDataException($"O arquivo {asset.Path} excede o tamanho declarado.");
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
        }
        if (total != asset.Size || !string.Equals(Convert.ToHexString(hash.GetHashAndReset()), asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"O arquivo {asset.Path} está incompleto ou difere do manifesto.");
        await output.FlushAsync(timeout.Token);
    }

    private static bool HasVersion(string file, Version version)
    {
        var info = FileVersionInfo.GetVersionInfo(file);
        return info.FileMajorPart == version.Major && info.FileMinorPart == version.Minor && info.FileBuildPart == version.Build;
    }

    private static void Commit(string stage, string destination)
    {
        string? damaged = null;
        if (Directory.Exists(destination))
        {
            damaged = Path.Combine(Path.GetDirectoryName(destination)!, ".damaged-" + Guid.NewGuid().ToString("N"));
            Directory.Move(destination, damaged);
        }
        try { Directory.Move(stage, destination); }
        catch
        {
            if (damaged != null) Directory.Move(damaged, destination);
            throw;
        }
        if (damaged != null) TryDeleteDirectory(damaged);
    }

    private List<(Version Version, string Folder)> InstalledReleases()
    {
        var versions = new List<(Version Version, string Folder)>();
        foreach (string folder in Directory.EnumerateDirectories(releases))
        {
            if (ReleaseChannel.TryParseVersion(Path.GetFileName(folder), out var version) &&
                (File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0)
                versions.Add((version, folder));
        }
        versions.Sort((a, b) => b.Version.CompareTo(a.Version));
        return versions;
    }

    private void RemoveInterruptedWork()
    {
        foreach (string folder in Directory.EnumerateDirectories(releases))
        {
            string name = Path.GetFileName(folder);
            if ((name.StartsWith(".stage-", StringComparison.Ordinal) && Guid.TryParseExact(name[7..], "N", out _)) ||
                (name.StartsWith(".damaged-", StringComparison.Ordinal) && Guid.TryParseExact(name[9..], "N", out _)))
                TryDeleteDirectory(folder);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose() => channel.Dispose();
}
