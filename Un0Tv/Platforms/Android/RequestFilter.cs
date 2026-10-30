#if ANDROID
using System;
using System.Collections.Generic;
using System.IO;
using Android.Webkit;

namespace Un0Tv;

// The app's own filter, doing the job a TV-wide blocker such as Repel does,
// but inside this one app and without a VPN.
//
// It runs twice for every request the page or a movie player makes:
//
//   Decide(url)  - is this request an advert, a tracker or malware host?
//                  Called inside ShouldInterceptRequest, i.e. BEFORE anything
//                  is downloaded. This is what a DNS blocker on the TV does,
//                  except it applies to the player's own frames too.
//
//   Empty()      - the answer given back for a blocked request: an empty
//                  reply, so the advert's script simply never arrives.
//
// Two rules keep it from breaking the app:
//   1) A keep list wins over everything. The site, its sign-in, the movie
//      players and their video hosts are never blocked.
//   2) Only whole host names are matched, never plain words. A host called
//      "adserver.com" is blocked, a channel page named "shadow" is not.
internal static class RequestFilter
{
    // ---- Advert, tracker and malware hosts -------------------------------
    // Matched by whole host name or by ending, so "x.magsrv.com" matches
    // "magsrv.com" and "ads.example.com" matches "example.com" only if that
    // full name is listed. Nothing here is matched by substring, which is
    // what keeps a harmless name from being caught by accident.
    private static readonly string[] BlockedHosts =
    {
        // --- Advert exchanges and networks ---
        "doubleclick.net", "googlesyndication.com", "googleadservices.com",
        "adservice.google.com", "pagead2.googlesyndication.com",
        "amazon-adsystem.com", "media.net", "adnxs.com", "adnxs-simple.com",
        "rubiconproject.com", "pubmatic.com", "openx.net", "casalemedia.com",
        "smartadserver.com", "adform.net", "advertising.com", "indexexchange.com",
        "spotxchange.com", "spotx.tv", "sovrn.com", "lijit.com", "33across.com",
        "tremorhub.com", "rhythmone.com", "yieldmo.com", "yieldlab.net",
        "sharethrough.com", "teads.tv", "freewheel.tv", "springserve.com",
        "connatix.com", "freestar.com", "minutemedia.com", "primis.tech",
        "vidazoo.com", "virool.com", "unrulymedia.com", "narrative.io",
        "criteo.com", "criteo.net", "taboola.com", "outbrain.com", "mgid.com",
        "revcontent.com", "zergnet.com", "content.ad", "bidswitch.net",
        "iponweb.net", "adtech.de", "adtechus.com", "sizmek.com",
        "flashtalking.com", "serving-sys.com", "adroll.com", "adsrvr.org",
        "googletagservices.com", "googletagmanager.com", "zedo.com",
        "adnium.com", "adpushup.com", "adpushup.net", "setupad.com",
        "seedtag.com", "multiadsexchange.com", "ad-stir.com", "xapads.com",
        "adbrite.com", "adbutler.com", "adblade.com", "adhigh.net",
        "onclicka.com", "onclickads.net", "adskeeper.com", "adskeeper.co.uk",
        "bidvertiser.com", "zeropark.com", "clickaine.com", "adcash.com",
        "exoclick.com", "trafficjunky.com", "juicyads.com", "a-ads.com",
        "trafficstars.com", "plugrush.com", "hilltopads.com", "hilltopads.net",
        "powered-by.net", "moonicorn.net", "technoratimedia.com",
        "streamadserver.com", "nadtech.com", "aduptech.de", "acscdn.com",
        "clkmon.com", "clkrev.com", "adclickmonitor.com", "track.adtrue.com",
        "propellerads.com", "propellerads.net", "onclickalgo.com",

        // --- Pop-under / "player" adverts the movie sites lean on ---
        "magsrv.com", "realsrv.com", "popads.net", "popcash.net",
        "popunder.net", "popundermedia.com", "popmyads.com", "adsterra.com",
        "clickadu.com", "adspyglass.com", "adspyglass.net", "ad2up.com",
        "exdynsrv.com", "tsyndicate.com", "bngmadjd.com", "bngprm.com",
        "pushnami.com", "izooto.com",
        "go.pub2srv.com", "pub2srv.com", "clksite.com", "clk.team",
        "revenuehits.com", "adf.ly", "shorte.st", "mgcash.com", "cpmlink.net",
        "ouo.io", "ouo.press", "bc.vc", "boost.ink", "gplinks.co", "za.gl",
        "affialb.com", "linkvertise.com", "adfoc.us", "yohoho.io",

        // --- Trackers, analytics and fingerprinting ---
        "lotame.com", "demdex.net", "omtrdc.net", "everesttech.net",
        "agkn.com", "rlcdn.com", "bluekai.com", "exelator.com", "tidaltv.com",
        "tapad.com", "semasio.net", "quantserve.com", "scorecardresearch.com",
        "comscore.com", "nielsen.com", "tynt.com", "parsely.com",
        "moatads.com", "doubleverify.com", "adsafeprotected.com",
        "hotjar.com", "clarity.ms", "fullstory.com", "mouseflow.com",
        "smartlook.com", "inspectlet.com", "luckyorange.com", "crazyegg.com",
        "segment.io", "segment.com", "mixpanel.com", "amplitude.com",
        "heap.io", "kissmetrics.com", "statcounter.com", "histats.com",
        "newrelic.com", "bugsnag.com", "sentry-cdn.com", "branch.io",
        "appsflyer.com", "adjust.com", "kochava.com", "singular.net",
        "ads-twitter.com", "analytics.twitter.com", "bat.bing.com",
        "clarity.ms", "ads.linkedin.com", "px.ads.linkedin.com",
        "onscroll.com", "postrelease.com", "plista.com", "veinteractive.com",
        "wunderkind.co", "richrelevance.com", "monetate.net",

        // --- App-install adverts (kept for completeness) ---
        "adbuddiz.com", "adcolony.com", "admob.com", "inmobi.com",
        "chartboost.com", "vungle.com", "applovin.com", "ironsrc.com",
        "mopub.com", "supersonicads.com", "tapjoy.com", "fyber.com",
        "smaato.net", "startapp.com", "leadbolt.com", "airpush.com",

        // --- Malware, scam and "your device is infected" hosts ---
        "coinhive.com", "coin-hive.com", "crypto-loot.com", "cryptoloot.pro",
        "webminepool.com", "coinimp.com", "jsecoin.com", "minero.cc",
        "2giga.link", "2giga.download", "getfile.io", "trackingclick.net",
        "voluum.com", "voluumtrk.com", "clickmagick.com", "bemobtrk.com",
        "keitaro.io", "binom.org",
        "updatemicrosoft.com", "microsoft-support-alert.com",
        "your-device-infected.com", "system-warning-alert.com",
    };

    // ---- Never blocked, whatever else says otherwise ---------------------
    // The site, its sign-in, and everything a movie needs in order to play.
    // A blocked video host would show as "the movie will not start", which is
    // far worse than an advert.
    private static readonly string[] KeepHosts =
    {
        // The site itself and any address it may move to on Netlify.
        "netlify.app", "netlify.com", "secondframe.netlify.app",

        // Sign-in (Google / Firebase), which the site's login depends on.
        "google.com", "googleapis.com", "gstatic.com", "googleusercontent.com",
        "accounts.google.com", "firebaseapp.com", "firebaseio.com",
        "firebaseinstallations.googleapis.com", "identitytoolkit.googleapis.com",
        "securetoken.googleapis.com", "recaptcha.net",

        // Movie players this site can embed, and their video hosts.
        "vidrift.net", "vidrift.in", "embed.vidrift.net", "reelvault.click",
        "vidsrc.me", "vidsrc.to", "vidsrc.pm", "vidsrc-embed.ru",
        "vidlink.pro", "videasy.net", "cinezo.live", "peachify.pro",
        "nxsha.app", "youtube.com", "youtube-nocookie.com", "ytimg.com",
        "googlevideo.com", "yt3.ggpht.com",

        // Film data, images and the libraries the page loads.
        "themoviedb.org", "tmdb.org", "image.tmdb.org", "themovedb.org",
        "jsdelivr.net", "cdnjs.cloudflare.com", "unpkg.com",
        "cloudflare.com", "cloudflareinsights.com", "cloudfront.net",
        "akamaihd.net", "fastly.net", "bunnycdn.com", "b-cdn.net",
        "jwplayer.com", "jwplatform.com", "jwpsrv.com", "jwpcdn.com",
        "mux.com", "videojs.com", "hlsjs.org", "dashjs.org",
    };

    public static bool IsBlocked(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        string host;
        try
        {
            host = new Uri(url).Host;
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrEmpty(host))
            return false;

        host = host.ToLowerInvariant().TrimEnd('.');

        // A keep rule always wins. Checked first so nothing below can undo it.
        if (Matches(host, KeepHosts))
            return false;

        if (host == "localhost" || host.StartsWith("[") || IsIp(host))
            return false;

        return Matches(host, BlockedHosts);
    }

    // True when "host" is exactly one of the names, or a subdomain of it.
    // ".example.com" is not matched by "notexample.com", which is the whole
    // point of matching the dot as well as the name.
    private static bool Matches(string host, string[] names)
    {
        foreach (var name in names)
        {
            var n = name.Trim();
            if (n.Length == 0)
                continue;
            if (host.Length == n.Length && host == n)
                return true;
            if (host.Length > n.Length
                && host.EndsWith(n, StringComparison.Ordinal)
                && host[host.Length - n.Length - 1] == '.')
                return true;
        }
        return false;
    }

    // A camera stream or a local box can use a bare IP; those are never ads.
    private static bool IsIp(string host)
    {
        if (host.Length < 7 || !char.IsDigit(host[0]))
            return false;
        foreach (var c in host)
        {
            if (!char.IsDigit(c) && c != '.' && c != ':')
                return false;
        }
        return true;
    }

    // The reply for a blocked request: an empty "no content" answer, so the
    // advert's script, image or video segment simply never arrives. The page
    // keeps running, which matters inside a player's own frame.
    public static WebResourceResponse Empty()
        => new(
            "text/plain",
            "utf-8",
            204,
            "No Content",
            new Dictionary<string, string>(),
            new MemoryStream(Array.Empty<byte>()));
}
#endif
