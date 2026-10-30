using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Un0Tv;

// The big public ad block list, HaGeZi's "Multi PRO", built into the app.
//
// This is the job the separate TV-wide blocker app was doing, done inside
// Un0Tv instead: about 214,000 advert, tracker, phishing and malware domains,
// matched before anything is downloaded.
//
// The list is published in five different file formats that all carry the same
// domains. All five are fetched and merged, because each one also catches a
// handful the others miss; the duplicates collapse into one set.
//
//   adblock/pro.txt          ||adsite.com^
//   dnsmasq/pro.txt          local=/adsite.com/
//   wildcard/pro.txt         *.adsite.com
//   wildcard/pro-onlydomains.txt   adsite.com
//   rpz/pro.txt              adsite.com CNAME .
//
// The merged list is saved once to the app's own folder, so every start after
// the first loads from disk instead of the network.
internal static class Hagezi
{
    // The five published lists, as given.
    public static readonly string[] Sources =
    {
        "https://cdn.jsdelivr.net/gh/hagezi/dns-blocklists@latest/adblock/pro.txt",
        "https://cdn.jsdelivr.net/gh/hagezi/dns-blocklists@latest/dnsmasq/pro.txt",
        "https://cdn.jsdelivr.net/gh/hagezi/dns-blocklists@latest/wildcard/pro.txt",
        "https://cdn.jsdelivr.net/gh/hagezi/dns-blocklists@latest/wildcard/pro-onlydomains.txt",
        "https://cdn.jsdelivr.net/gh/hagezi/dns-blocklists@latest/rpz/pro.txt",
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(180) };

    // Where the merged list is kept on the TV.
    private static string SavePath =>
        Path.Combine(FileSystem.AppDataDirectory, "hagezi-pro.txt");

    // The loaded list, or null until Load succeeds.
    private static HashSet<string>? _set;

    public static bool IsLoaded => _set is { Count: > 0 };

    public static int Count => _set?.Count ?? 0;

    // True when the merged list is already saved on this TV.
    public static bool IsSaved => File.Exists(SavePath);

    // When the saved list was last written, for the "updated" line.
    public static DateTimeOffset? SavedAt
    {
        get
        {
            try
            {
                return File.Exists(SavePath) ? File.GetLastWriteTime(SavePath) : null;
            }
            catch
            {
                return null;
            }
        }
    }

    // ---------------------------------------------------------------
    // Loading the saved list
    // ---------------------------------------------------------------

    // Reads the saved list into memory. Returns false when nothing is saved
    // yet, which is what makes the startup check offer to download it.
    public static Task<bool> LoadAsync()
    {
        if (IsLoaded)
            return Task.FromResult(true);

        if (!IsSaved)
            return Task.FromResult(false);

        return Task.Run(() =>
        {
            try
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadLines(SavePath))
                {
                    var d = line.Trim();
                    if (d.Length > 2)
                        set.Add(d);
                }

                if (set.Count == 0)
                    return false;

                _set = set;
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    // ---------------------------------------------------------------
    // Downloading and merging all five lists
    // ---------------------------------------------------------------

    // Fetches all five lists, merges them and saves the result.
    // Returns how many distinct domains were saved.
    public static async Task<int> DownloadAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report("Downloading the block lists …");

        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // How many lists have landed, for the progress line.
        var done = 0;

        foreach (var url in Sources)
        {
            var name = ShortName(url);
            progress?.Report($"Downloading {name} ({done + 1} of {Sources.Length}) …");

            try
            {
                var text = await Http.GetStringAsync(url, ct).ConfigureAwait(false);

                // Read line by line rather than splitting the whole file: the
                // RPZ list alone is 11 MB and half a million lines.
                using var reader = new StringReader(text);
                while (reader.ReadLine() is { } raw)
                {
                    var domain = ParseLine(raw);
                    if (domain is not null)
                        all.Add(domain);
                }

                done++;
                progress?.Report($"{all.Count:N0} domains from {done} of {Sources.Length} lists …");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One list failing must not lose the other four. The union of
                // what did arrive is still a working block list.
                progress?.Report($"{name} could not be fetched — carrying on …");
            }
        }

        if (all.Count == 0)
            throw new InvalidOperationException(
                "None of the block lists could be downloaded. Check the TV's internet connection.");

        progress?.Report("Saving the block list …");
        Save(all);

        _set = all;
        return all.Count;
    }

    // Writes the merged list, one domain per line, in chunks so a big list does
    // not build one giant string in memory.
    private static void Save(HashSet<string> domains)
    {
        Directory.CreateDirectory(FileSystem.AppDataDirectory);

        using var writer = new StreamWriter(SavePath, false, new UTF8Encoding(false));
        var chunk = new StringBuilder(64 * 1024);

        foreach (var d in domains)
        {
            chunk.Append(d).Append('\n');
            if (chunk.Length >= 60 * 1024)
            {
                writer.Write(chunk);
                chunk.Clear();
            }
        }

        writer.Write(chunk);
    }

    // ---------------------------------------------------------------
    // Reading one line of any of the five formats
    // ---------------------------------------------------------------

    // Turns a line from any of the five lists into a bare domain name, or null
    // when the line is a comment, a header or something that is not a host.
    private static string? ParseLine(string raw)
    {
        var line = raw.Trim();
        if (line.Length < 4)
            return null;

        // Comments and headers: #, ! and ; start them in all five formats.
        var first = line[0];
        if (first is '#' or '!' or ';')
            return null;

        // adblock:  ||adsite.com^
        if (line.StartsWith("||", StringComparison.Ordinal))
        {
            line = line[2..];
            var caret = line.IndexOf('^');
            if (caret >= 0)
                line = line[..caret];
            return Clean(line);
        }

        // dnsmasq:  local=/adsite.com/
        if (line.StartsWith("local=/", StringComparison.OrdinalIgnoreCase))
        {
            line = line[7..];
            var slash = line.IndexOf('/');
            if (slash >= 0)
                line = line[..slash];
            return Clean(line);
        }

        // RPZ has a "domain CNAME ." suffix; wildcard and bare lists do not.
        var space = line.IndexOf(' ');
        if (space >= 0)
            line = line[..space];

        // wildcard:  *.adsite.com
        if (line.StartsWith("*.", StringComparison.Ordinal))
            line = line[2..];

        return Clean(line);
    }

    // Accepts only something that really is a host name. Anything else - zone
    // directives, IP addresses, wildcards in the middle, paths - is dropped, so
    // a malformed line can never turn into a rule that blocks the wrong thing.
    private static string? Clean(string s)
    {
        s = s.Trim().TrimEnd('.').ToLowerInvariant();

        if (s.Length is < 4 or > 253)
            return null;

        // Must be a dotted name, and must not contain anything that is not part
        // of a host name.
        var dots = 0;
        foreach (var c in s)
        {
            if (c == '.')
            {
                dots++;
                continue;
            }

            if (!(c is >= 'a' and <= 'z' || c is >= '0' and <= '9' || c is '-' or '_'))
                return null;
        }

        if (dots == 0)
            return null;

        // A leading, trailing or doubled dot is a broken name.
        if (s[0] == '.' || s.Contains("..", StringComparison.Ordinal))
            return null;

        return s;
    }

    // The tail of a list address, for the progress line.
    private static string ShortName(string url)
    {
        var at = url.IndexOf("dns-blocklists@latest/", StringComparison.Ordinal);
        return at >= 0 ? url[(at + 22)..] : url;
    }

    // ---------------------------------------------------------------
    // The question the browser asks for every request
    // ---------------------------------------------------------------

    // True when this host, or any name it sits under, is on the list.
    //
    // "a.b.adsite.com" is caught by an entry for "adsite.com", which is how the
    // wildcard lists are meant to work. Each parent name is tried in turn, so
    // the answer never depends on how deep the subdomain is.
    public static bool Blocks(string host)
    {
        var set = _set;
        if (set is not { Count: > 0 } || host.Length == 0)
            return false;

        var name = host;

        while (name.Length > 0)
        {
            if (set.Contains(name))
                return true;

            var dot = name.IndexOf('.');
            if (dot < 0)
                break;

            name = name[(dot + 1)..];
        }

        return false;
    }
}
