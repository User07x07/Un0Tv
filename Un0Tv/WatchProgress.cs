using System.Text.Json;

namespace Un0Tv;

// One movie or episode the viewer left part way through.
public sealed class WatchItem
{
    public string Key { get; set; } = "";
    public int Id { get; set; }
    public string Type { get; set; } = "movie";
    public int? Season { get; set; }
    public int? Episode { get; set; }
    public double Watched { get; set; }
    public double Duration { get; set; }
    public long Updated { get; set; }
    public string? Title { get; set; }
    public string? Url { get; set; }

    // The name shown on the card.
    public string Label
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title))
                return Type == "tv" && Season is not null && Episode is not null
                    ? $"{Title} S{Season}E{Episode}"
                    : Title!;

            return Type == "tv"
                ? $"Show {Id} S{Season ?? 1}E{Episode ?? 1}"
                : $"Movie {Id}";
        }
    }

    // The second line on the card: time left, or time watched if the length is unknown.
    public string Progress
    {
        get
        {
            if (Duration <= 0)
                return "Watched " + TimeSpan.FromSeconds(Watched).ToString(@"h\:mm");

            var left = TimeSpan.FromSeconds(Math.Max(0, Duration - Watched));
            return $"{(int)left.TotalMinutes} min left";
        }
    }

    // Worth showing: started, and not nearly finished.
    public bool Worth => Watched >= 15 && !(Duration > 0 && Watched > Duration - 60);
}

// Keeps the Continue watching list on the TV. The page sends each saved entry
// through ProgressBridge; the list is kept in the app's own settings, so it
// survives restarts and is there before the site is opened.
public static class WatchProgress
{
    private const string Pref = "watch_progress";
    private const int Max = 30;
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static void Save(string json)
    {
        WatchItem? item;
        try { item = JsonSerializer.Deserialize<WatchItem>(json, Options); }
        catch { return; }

        if (item is null || string.IsNullOrEmpty(item.Key) || item.Id <= 0)
            return;

        // Only links to this site are kept. The bridge is open to every frame on
        // the page, so another frame must not be able to plant a link.
        if (item.Url is null || !item.Url.StartsWith(MainPage.SiteUrl, StringComparison.OrdinalIgnoreCase))
            item.Url = null;

        lock (Gate)
        {
            var all = Load();
            all.RemoveAll(x => x.Key == item.Key);
            all.Insert(0, item);
            if (all.Count > Max)
                all.RemoveRange(Max, all.Count - Max);

            try { Preferences.Set(Pref, JsonSerializer.Serialize(all, Options)); }
            catch { }
        }
    }

    // The newest titles worth picking up again.
    public static List<WatchItem> Recent(int max)
    {
        lock (Gate)
        {
            return Load()
                .Where(x => x.Worth)
                .OrderByDescending(x => x.Updated)
                .Take(max)
                .ToList();
        }
    }

    private static List<WatchItem> Load()
    {
        try
        {
            return JsonSerializer.Deserialize<List<WatchItem>>(Preferences.Get(Pref, "[]"), Options)
                   ?? new List<WatchItem>();
        }
        catch
        {
            return new List<WatchItem>();
        }
    }
}
