using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;

namespace Un0Tv;

// One line in the Activity list on the home screen.
//
// This is the part of Repel's Activity tab that matters on a TV: which address
// was asked for, whether it was let through or refused, and how long ago.
public sealed class LogEntry : INotifyPropertyChanged
{
    public string Host { get; init; } = string.Empty;

    // "Ad or tracker", "HaGeZi block list", "The site", "Video host" ... what
    // the filter knew about the address at the time.
    public string Label { get; init; } = string.Empty;

    public bool Blocked { get; init; }

    public int Hits { get; set; } = 1;

    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;

    public string Kind => Blocked ? "Blocked" : "Allowed";

    public string Badge => Blocked ? "\u2715" : "\u2713";

    public string BadgeColor => Blocked ? "#F0736A" : "#4CD07D";

    public string BadgeBg => Blocked ? "#2C1618" : "#132619";

    public string Detail => $"{Label} \u00b7 {Hits:N0} {(Hits == 1 ? "lookup" : "lookups")} \u00b7 {When}";

    // "12 seconds ago", like the activity list in Repel. Recalculated on every
    // heartbeat so a row left on screen never keeps saying "just now".
    public string When
    {
        get
        {
            var seconds = (int)Math.Max(0, (DateTimeOffset.Now - At).TotalSeconds);
            if (seconds < 5) return "just now";
            if (seconds < 60) return $"{seconds} seconds ago";

            var minutes = seconds / 60;
            if (minutes < 60) return $"{minutes} {(minutes == 1 ? "minute" : "minutes")} ago";

            var hours = minutes / 60;
            if (hours < 24) return $"{hours} {(hours == 1 ? "hour" : "hours")} ago";

            var days = hours / 24;
            return $"{days} {(days == 1 ? "day" : "days")} ago";
        }
    }

    public void Tick() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(When)));

    public void Bump()
    {
        Hits++;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Detail)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

// The live numbers and the Activity list, shared by the filter (which runs on
// the browser's own threads) and the home screen (which must draw on the UI
// thread).
//
// The filter never touches the list directly: it only drops one small record
// into a queue. Drain() is called from the screen's heartbeat, on the UI
// thread, and moves whatever arrived into the list. That keeps a busy movie
// page from touching anything the screen is reading.
//
// The name is AdStats rather than Activity on purpose: "Activity" is Android's
// own screen class, and shadowing it breaks every file that extends it.
internal static class AdStats
{
    // How many rows the list keeps. A TV viewer only ever looks at the newest
    // few, and keeping it short is what makes it cheap to redraw.
    private const int MaxRows = 12;

    // The counters, on display under the shield.
    private static int _blockedToday;
    private static int _allowedToday;
    private static int _checksToday;

    public static int BlockedToday => Volatile.Read(ref _blockedToday);

    public static int AllowedToday => Volatile.Read(ref _allowedToday);

    public static int ChecksToday => Volatile.Read(ref _checksToday);

    // Every row, newest first.
    public static ObservableCollection<LogEntry> Rows { get; } = new();

    // Arrived from the filter, not yet moved into Rows.
    private static readonly List<LogEntry> Pending = new();
    private static readonly object PendingGate = new();

    // The rows being tracked, by address, so the same host folds into one line
    // instead of filling the list with its own repeats.
    private static readonly Dictionary<string, LogEntry> Tracked =
        new(StringComparer.OrdinalIgnoreCase);

    private static DateOnly _day = DateOnly.FromDateTime(DateTime.Now);

    // ---- Called from the browser's threads ---------------------------------

    // One request. "blocked" true means it was refused.
    public static void Record(string host, bool blocked, string label)
    {
        if (string.IsNullOrEmpty(host))
            return;

        RollDay();

        Interlocked.Increment(ref _checksToday);
        if (blocked)
            Interlocked.Increment(ref _blockedToday);
        else
            Interlocked.Increment(ref _allowedToday);

        // Rows are only queued when they would be worth showing. A movie page
        // makes hundreds of requests a minute and every video segment looks
        // exactly alike, so a blocked address always gets a line while an
        // allowed one waits a minute before repeating. The counters above move
        // every time, which is why "blocked today" stays true even while you
        // are watching.
        if (!ShouldLog(host, blocked))
            return;

        lock (PendingGate)
        {
            // Nothing is draining the queue while the movie is on screen, so it
            // is capped rather than left to grow for two hours of watching.
            if (Pending.Count > 200)
                return;

            Pending.Add(new LogEntry
            {
                Host = host,
                Label = label,
                Blocked = blocked,
                At = DateTimeOffset.Now,
            });
        }
    }

    // Set by the page while the Activity list is on screen.
    public static bool IsWatched { get; set; }

    // When each allowed address last reached the list. Blocked addresses skip
    // this entirely.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> LastLogged =
        new(StringComparer.OrdinalIgnoreCase);

    private const long AllowedRepeatMs = 60_000;

    private static bool ShouldLog(string host, bool blocked)
    {
        if (blocked)
            return true;

        var now = Environment.TickCount64;
        var previous = LastLogged.GetOrAdd(host, now);

        if (now - previous < AllowedRepeatMs)
            return false;

        LastLogged[host] = now;
        return true;
    }

    // ---- Called on the UI thread -------------------------------------------

    // Moves whatever arrived into the visible list. Newest first, one line per
    // address, and the list is trimmed to MaxRows.
    public static void Drain()
    {
        List<LogEntry>? arrived = null;

        lock (PendingGate)
        {
            if (Pending.Count > 0)
            {
                arrived = new List<LogEntry>(Pending);
                Pending.Clear();
            }
        }

        if (arrived is not null)
        {
            foreach (var entry in arrived)
            {
                if (Tracked.TryGetValue(entry.Host, out var existing) && existing.Blocked == entry.Blocked)
                {
                    // Already listed: bump its count and float it back to the
                    // top, which is how a viewer expects a busy address to look.
                    existing.At = entry.At;
                    existing.Bump();

                    var at = Rows.IndexOf(existing);
                    if (at > 0)
                        Rows.Move(at, 0);

                    continue;
                }

                Tracked[entry.Host] = entry;
                Rows.Insert(0, entry);
            }

            while (Rows.Count > MaxRows)
            {
                var dropped = Rows[^1];
                Rows.RemoveAt(Rows.Count - 1);
                Tracked.Remove(dropped.Host);
            }
        }

        // Keep the "seconds ago" text honest.
        foreach (var row in Rows)
            row.Tick();
    }

    // Fills "target" with the rows that pass the All / Blocked tab, touching
    // only what actually changed so the on-screen list does not flicker once a
    // second.
    public static void Apply(bool blockedOnly, ObservableCollection<LogEntry> target)
    {
        for (var i = target.Count - 1; i >= 0; i--)
        {
            var row = target[i];
            if (blockedOnly && !row.Blocked)
                target.RemoveAt(i);
            else if (!Rows.Contains(row))
                target.RemoveAt(i);
        }

        for (var i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            if (blockedOnly && !row.Blocked)
                continue;

            var at = target.IndexOf(row);
            if (at == i)
                continue;

            if (at >= 0)
                target.Move(at, i);
            else
                target.Insert(i, row);
        }
    }

    // A tick of the clock, or a new day: the counter on the shield starts again.
    private static void RollDay()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today == _day)
            return;

        _day = today;
        Volatile.Write(ref _blockedToday, 0);
        Volatile.Write(ref _allowedToday, 0);
        Volatile.Write(ref _checksToday, 0);
    }

    // ---- Saved between starts ----------------------------------------------

    private const string KeyDay = "stats_day";
    private const string KeyBlocked = "stats_blocked";
    private const string KeyAllowed = "stats_allowed";
    private const string KeyChecks = "stats_checks";

    public static void Load()
    {
        try
        {
            var saved = Preferences.Get(KeyDay, string.Empty);
            var today = _day.ToString("yyyy-MM-dd");

            Volatile.Write(ref _blockedToday, Preferences.Get(KeyBlocked, 0));
            Volatile.Write(ref _allowedToday, Preferences.Get(KeyAllowed, 0));
            Volatile.Write(ref _checksToday, Preferences.Get(KeyChecks, 0));

            if (saved != today)
            {
                // A new day: the "today" figure starts from nothing.
                Volatile.Write(ref _blockedToday, 0);
                Volatile.Write(ref _allowedToday, 0);
                Volatile.Write(ref _checksToday, 0);
                Save();
            }
        }
        catch
        {
            // No preferences on this device: the counters simply start at zero.
        }
    }

    public static void Save()
    {
        try
        {
            Preferences.Set(KeyDay, _day.ToString("yyyy-MM-dd"));
            Preferences.Set(KeyBlocked, BlockedToday);
            Preferences.Set(KeyAllowed, AllowedToday);
            Preferences.Set(KeyChecks, ChecksToday);
        }
        catch { }
    }
}
