namespace Un0Tv;

// Remote control for the VidFast movie player. The player sits in a frame from
// another website, so the app cannot touch it directly. VidFast's PostMessage
// API lets the page send it commands (play, pause, seek) and reports its state
// back (current time, playing or paused).
//
// This script runs in the page the viewer sees, finds the VidFast player frame,
// and exposes four simple actions the remote's keys call: play, pause, forward
// 10 seconds and back 10 seconds.
//
// It also saves watch progress from the player's events into the page's own
// browser storage (localStorage), so a movie or episode can resume where the
// viewer stopped the next time it is opened.
public static class PlayerControl
{
    public static readonly string Js = """
(function () {
  // Only the page the viewer sees drives the player; the player's own frame
  // is left alone.
  if (window.top !== window) return;
  if (window.__un0tvPlayer) return;

  // The VidFast domains, for example vidfast.vc or vidfast.pro.
  var VIDFAST = /(^|\.)vidfast\.[a-z]+$/i;
  var STORE = 'un0tvProgress';
  var time = 0;
  var playing = false;
  var resumedFor = null;
  var holdSaves = false;

  // Every VidFast player frame on the page.
  function players() {
    var out = [];
    var list = document.querySelectorAll('iframe');
    for (var i = 0; i < list.length; i++) {
      var f = list[i];
      try {
        var host = new URL(f.getAttribute('src') || '', location.href).hostname;
        if (VIDFAST.test(host) && f.contentWindow) out.push(f);
      } catch (e) { }
    }
    return out;
  }

  function send(msg) {
    var list = players();
    for (var i = 0; i < list.length; i++) {
      try { list[i].contentWindow.postMessage(msg, '*'); } catch (e) { }
    }
  }

  // ---- Saved progress ----

  function readStore() {
    try { return JSON.parse(localStorage.getItem(STORE) || '{}') || {}; } catch (e) { return {}; }
  }

  function writeStore(s) {
    try { localStorage.setItem(STORE, JSON.stringify(s)); } catch (e) { }
  }

  // One key per movie, and one per episode for shows.
  function keyFor(info) {
    if (!info.tmdbId) return null;
    if (info.mediaType === 'tv') {
      return 'tv' + info.tmdbId + ':s' + (info.season || 1) + 'e' + (info.episode || 1);
    }
    return 'movie' + info.tmdbId;
  }

  // The title VidFast has sent for this movie or show, if any.
  function titleFor(type, id) {
    try {
      var meta = JSON.parse(localStorage.getItem('vidFastProgress') || '{}') || {};
      var m = meta[(type === 'tv' ? 't' : 'm') + id];
      return m && m.title ? m.title : null;
    } catch (e) { return null; }
  }

  // Hands the saved entry to the app for the Continue watching row. Routine
  // updates go at most every 10 seconds per title; pauses and seeks go at once.
  var appKey = null;
  var appAt = 0;
  function forwardToApp(entry, key, force) {
    if (!window.Un0TvNative || !entry) return;
    var now = Date.now();
    if (!force && key === appKey && now - appAt < 10000) return;
    appKey = key;
    appAt = now;
    try {
      var copy = {};
      for (var k in entry) copy[k] = entry[k];
      copy.key = key;
      copy.title = titleFor(entry.type, entry.id);
      copy.url = location.href;
      window.Un0TvNative.saveProgress(JSON.stringify(copy));
    } catch (e) { }
  }

  function saveProgress(info) {
    var key = keyFor(info);
    if (!key || typeof info.currentTime !== 'number') return;
    var s = readStore();
    s[key] = {
      id: info.tmdbId,
      type: info.mediaType || 'movie',
      season: info.season || null,
      episode: info.episode || null,
      watched: Math.floor(info.currentTime),
      duration: Math.floor(info.duration || 0),
      url: location.href,
      updated: Date.now()
    };
    // Keep only the 60 most recent titles so storage never grows without limit.
    var keys = Object.keys(s);
    if (keys.length > 60) {
      keys.sort(function (a, b) { return s[a].updated - s[b].updated; });
      for (var i = 0; i < keys.length - 60; i++) delete s[keys[i]];
    }
    writeStore(s);
    forwardToApp(s[key], key, info.event === 'pause' || info.event === 'ended' || info.event === 'seeked');
  }

  // The first time a title is seen on this page, jump to the saved spot.
  // Saves are held back meanwhile, so the player's early zero time does not
  // overwrite the progress we are about to resume from.
  function resumeIfSaved(info) {
    var key = keyFor(info);
    if (!key || resumedFor === key) return;
    resumedFor = key;
    var saved = readStore()[key];
    if (!saved || saved.watched < 15) return;
    // Nearly finished: start again from the beginning.
    if (saved.duration && saved.watched > saved.duration - 60) return;
    holdSaves = true;
    setTimeout(function () {
      seek(saved.watched);
      holdSaves = false;
    }, 2000);
  }

  // The player reports its state back: current time, playing or paused.
  window.addEventListener('message', function (event) {
    var d = event.data;
    if (!d) return;

    // The movie's own frame reports whether it has a movie on screen, so the
    // app knows when the Left key should open its command panel.
    if (d.__un0tvState) {
      try {
        if (window.Un0TvNative && window.Un0TvNative.mediaState)
          window.Un0TvNative.mediaState(d.__un0tvState.on ? '1' : '0');
      } catch (e) { }
      return;
    }

    try {
      if (!VIDFAST.test(new URL(event.origin).hostname)) return;
    } catch (e) { return; }
    // The title and poster details, kept for the Continue watching row.
    if (d.type === 'MEDIA_DATA' && d.data) {
      try { localStorage.setItem('vidFastProgress', JSON.stringify(d.data)); } catch (e) { }
      return;
    }
    if (d.type !== 'PLAYER_EVENT' || !d.data) return;
    var info = d.data;
    if (typeof info.currentTime === 'number') time = info.currentTime;
    if (typeof info.playing === 'boolean') playing = info.playing;
    if (info.event === 'play') playing = true;
    if (info.event === 'pause' || info.event === 'ended') playing = false;

    if (info.tmdbId) {
      var key = keyFor(info);
      reportNext(info, key);
      resumeIfSaved(info);
      if (!holdSaves) saveProgress(info);
    }
  });

  function play() { playing = true; send({ command: 'play' }); }
  function pause() { playing = false; send({ command: 'pause' }); }

  function seek(to) {
    if (to < 0) to = 0;
    time = to;
    send({ command: 'seek', time: Math.floor(to) });
  }

  // Episode-only Next state. Movies never call the bridge with "1".
  var nextKey = null;
  var nextAt = 0;
  function reportNext(info, key) {
    var isTv = info.mediaType === 'tv';
    var ready = isTv && info.duration > 0 && info.currentTime / info.duration >= 0.90;
    if (!isTv) { ready = false; key = null; }
    var now = Date.now();
    if (key === nextKey && ready === (nextAt > 0) && now - nextAt < 10000) return;
    if (!ready && !nextKey) return;
    nextKey = ready ? key : null;
    nextAt = ready ? now : 0;
    try {
      if (window.Un0TvNative && window.Un0TvNative.nextEpisode)
        window.Un0TvNative.nextEpisode(ready ? '1' : '0');
    } catch (e) { }
  }

  function nextEpisode() {
    // Use the site's own episode selector: it knows season boundaries and
    // specials, unlike adding one to an episode number in the address.
    var own = document.querySelector('.wt-an-skip');
    if (own) { own.click(); return true; }
    var params = new URLSearchParams(location.search);
    if (params.get('type') !== 'tv') return false;
    var id = parseInt(params.get('id') || '', 10);
    var season = parseInt(params.get('season') || '1', 10);
    var episode = parseInt(params.get('episode') || '1', 10);
    var title = params.get('title') || '';
    if (!id || !season || !episode) return false;
    function go(s, e) {
      params.set('type', 'tv');
      params.set('season', String(s));
      params.set('episode', String(e));
      location.href = location.pathname + '?' + params.toString();
    }
    // The site's episode list is the source of truth when it is available.
    var current = document.querySelector('.wt-fs-episode-item.active');
    var following = current && current.nextElementSibling;
    if (following) { following.click(); return true; }
    // Keep the site's own query values (title, kids mode and so on). The
    // normal path above is preferred because it also handles season changes.
    params.set('episode', String(episode + 1));
    location.href = location.pathname + '?' + params.toString();
    return true;
  }

  function beginning() {
    seek(0);  }

  window.__un0tvPlayer = {
    play: play,
    pause: pause,
    next: nextEpisode,
    toggle: function () { if (playing) pause(); else play(); },
    forward: function () { seek(time + 10); },
    back: function () { seek(time - 10); },
    backward: function () { seek(time - 10); },
    beginning: beginning,
  };

  // Keep the current time fresh, so forward and back start from the right place.
  setInterval(function () { send({ command: 'getStatus' }); }, 1500);
})();
""";
}
