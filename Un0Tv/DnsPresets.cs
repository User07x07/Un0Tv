namespace Un0Tv;

// The Private DNS presets offered on the Private DNS screen. Android only lets
// the viewer switch Private DNS in its own settings, so the app shows the chosen
// name, copies it to the clipboard and opens that page.
public static class DnsPresets
{
    public readonly record struct Preset(string Name, string Host, string Note);

    public static readonly Preset[] All =
    {
        new("AdGuard DNS", "dns.adguard-dns.com", "Blocks ads and trackers"),
        new("NextDNS", "your-id.dns.nextdns.io", "Filters ads, trackers and malware; replace your-id with your NextDNS ID"),
        new("Mullvad", "adblock.dns.mullvad.net", "Blocks ads and trackers"),
        new("Quad9", "dns.quad9.net", "Blocks malware and phishing"),
        new("Cloudflare", "one.one.one.one", "Fast resolver, no ad blocking"),
        new("Control D", "dns.controld.com", "Filtering resolver, set up in its dashboard"),
        new("CleanBrowsing", "security-filter-dns.cleanbrowsing.org", "Blocks malware and phishing"),
        new("Google", "dns.google", "Fast resolver, no ad blocking"),
    };

    private const string Pref = "dns_preset";

    // The preset the viewer picked last time. Kept on the TV.
    public static int SelectedIndex
    {
        get
        {
            try
            {
                var i = Preferences.Get(Pref, 0);
                return i >= 0 && i < All.Length ? i : 0;
            }
            catch
            {
                return 0;
            }
        }
        set
        {
            try { Preferences.Set(Pref, value); } catch { }
        }
    }

    public static Preset Current => All[SelectedIndex];
}

// The state of the whole-TV DNS filter, read by the home screen. Set by the
// service itself, so the screen always shows whether it really runs.
public static class DnsSettings
{
    public static bool VpnRunning { get; set; }
}
