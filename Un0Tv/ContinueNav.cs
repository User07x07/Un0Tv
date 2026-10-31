namespace Un0Tv;

// The Continue watching strip inside the site's own library.
//
// The movie player saves its progress in the page's own storage, so the app
// already knows where each title stopped. The site's library knows nothing
// about that - the player runs in a frame from another website, so its stored
// progress sits under a different address and the library cannot read it.
//
// This script puts it back on the site: on the library (and the home) page it
// draws a row of the films that were left part way through, taken from the same
// saved entries. Pressing one opens it where it stopped.
//
// It is deliberately careful: it only draws on pages whose address looks like
// the library or the home page, it never removes anything the site drew, and
// every step is wrapped so a change on the site can only mean "no strip today",
// never a broken page.
public static class ContinueNav
{
    public static readonly string Js = """
(function () {
  if (window.top !== window) return;
  if (window.__un0tvContinue) return;

  var STORE = 'un0tvProgress';
  var META = 'vidFastProgress';
  var ID = 'un0tv-continue-row';
  var lastPath = '';
  var busy = false;

  function readStore() {
    try { return JSON.parse(localStorage.getItem(STORE) || '{}') || {}; } catch (e) { return {}; }
  }

  function readMeta() {
    try { return JSON.parse(localStorage.getItem(META) || '{}') || {}; } catch (e) { return {}; }
  }

  // Only the pages where a row of films belongs.
  function wantedPath() {
    var p = (location.pathname || '/').toLowerCase();
    if (p === '/' || p === '') return true;
    var words = ['library', 'home', 'browse', 'movies', 'tv', 'discover', 'search'];
    for (var i = 0; i < words.length; i++) {
      if (p.indexOf(words[i]) >= 0) return true;
    }
    return false;
  }

  function label(item, meta) {
    var m = meta[(item.type === 'tv' ? 't' : 'm') + item.id];
    var name = m && m.title ? m.title : null;
    if (!name) name = (item.type === 'tv' ? 'Show ' : 'Movie ') + item.id;
    if (item.type === 'tv' && item.season && item.episode)
      name += ' S' + item.season + 'E' + item.episode;
    return name;
  }

  function poster(item, meta) {
    var m = meta[(item.type === 'tv' ? 't' : 'm') + item.id];
    if (m && m.poster_path) return 'https://image.tmdb.org/t/p/w342' + m.poster_path;
    return '';
  }

  function left(item) {
    if (!item.duration) return Math.round(item.watched / 60) + ' min watched';
    var mins = Math.max(0, Math.round((item.duration - item.watched) / 60));
    return mins + ' min left';
  }

  function worth(item) {
    if (!item || typeof item.watched !== 'number') return false;
    if (item.watched < 15) return false;
    if (item.duration && item.watched > item.duration - 60) return false;
    return true;
  }

  function cards() {
    var store = readStore();
    var meta = readMeta();
    var out = [];
    for (var k in store) {
      var item = store[k];
      if (!worth(item)) continue;
      item.key = k;
      item.label = label(item, meta);
      item.poster = poster(item, meta);
      item.left = left(item);
      out.push(item);
    }
    out.sort(function (a, b) { return (b.updated || 0) - (a.updated || 0); });
    return out.slice(0, 12);
  }

  function open(item) {
    try {
      if (item.url && item.url.indexOf('http') === 0) { location.href = item.url; return; }
      // No saved address: look the title up on the site itself.
      var q = item.type === 'tv' ? 'tv' : 'movie';
      location.href = '/' + q + '/' + item.id;
    } catch (e) { }
  }

  function build(list) {
    var row = document.createElement('div');
    row.id = ID;
    row.style.cssText = 'box-sizing:border-box;width:100%;padding:10px 4vw 18px;'
      + 'font-family:"Segoe UI",system-ui,sans-serif;color:#E6F1FF;'
      + 'background:transparent;';

    var head = document.createElement('div');
    head.textContent = 'Continue watching';
    head.style.cssText = 'font-size:20px;font-weight:700;letter-spacing:.3px;margin:6px 0 10px;';
    row.appendChild(head);

    var strip = document.createElement('div');
    strip.style.cssText = 'display:flex;gap:12px;overflow-x:auto;overflow-y:hidden;'
      + 'padding-bottom:6px;scrollbar-width:none;';

    for (var i = 0; i < list.length; i++) {
      (function (item) {
        var card = document.createElement('div');
        card.setAttribute('role', 'button');
        card.setAttribute('tabindex', '0');
        card.className = 'un0tv-continue-card';
        card.style.cssText = 'flex:0 0 auto;width:150px;cursor:pointer;'
          + 'border-radius:10px;overflow:hidden;background:#101827;'
          + 'border:1px solid #1E3A5F;';

        var art = document.createElement('div');
        art.style.cssText = 'position:relative;width:150px;height:212px;background:#0B1220;';
        if (item.poster) {
          var img = document.createElement('img');
          img.src = item.poster;
          img.loading = 'lazy';
          img.setAttribute('referrerpolicy', 'no-referrer');
          img.style.cssText = 'width:100%;height:100%;object-fit:cover;display:block;';
          art.appendChild(img);
        } else {
          var glyph = document.createElement('div');
          glyph.textContent = '▶';
          glyph.style.cssText = 'position:absolute;left:0;right:0;top:44%;text-align:center;'
            + 'font-size:30px;color:#2B3F63;';
          art.appendChild(glyph);
        }
        card.appendChild(art);

        var bar = document.createElement('div');
        var pct = item.duration ? Math.min(100, Math.round(item.watched / item.duration * 100)) : 0;
        bar.style.cssText = 'height:4px;background:#16233A;';
        var fill = document.createElement('div');
        fill.style.cssText = 'height:4px;width:' + pct + '%;background:#22D3EE;';
        bar.appendChild(fill);
        card.appendChild(bar);

        var body = document.createElement('div');
        body.style.cssText = 'padding:8px 10px 10px;';
        var name = document.createElement('div');
        name.textContent = item.label;
        name.style.cssText = 'font-size:13px;font-weight:600;line-height:1.25;'
          + 'white-space:nowrap;overflow:hidden;text-overflow:ellipsis;';
        var sub = document.createElement('div');
        sub.textContent = item.left;
        sub.style.cssText = 'font-size:11px;color:#8A93A8;margin-top:3px;';
        body.appendChild(name);
        body.appendChild(sub);
        card.appendChild(body);

        card.addEventListener('click', function (ev) {
          try { ev.preventDefault(); ev.stopPropagation(); } catch (e) { }
          open(item);
        }, true);

        card.addEventListener('keydown', function (ev) {
          if (ev.key === 'Enter' || ev.key === ' ') {
            try { ev.preventDefault(); } catch (e) { }
            open(item);
          }
        });

        strip.appendChild(card);
      })(list[i]);
    }

    row.appendChild(strip);
    return row;
  }

  function container() {
    return document.querySelector('main') || document.querySelector('#root')
        || document.querySelector('#__next') || document.body;
  }

  function render() {
    if (busy) return;
    if (!wantedPath()) { remove(); return; }

    var list = cards();
    if (!list.length) { remove(); return; }
    if (document.getElementById(ID)) return;

    busy = true;
    try {
      var row = build(list);
      var box = container();
      if (!box) return;
      if (box === document.body) document.body.insertBefore(row, document.body.firstChild);
      else box.insertBefore(row, box.firstChild);
      lastPath = location.pathname;
    } catch (e) {
      remove();
    } finally {
      busy = false;
    }
  }

  function remove() {
    var old = document.getElementById(ID);
    if (old && old.parentNode) { try { old.parentNode.removeChild(old); } catch (e) { } }
  }

  function tick() {
    try {
      // The site swaps pages without reloading, so the address is the signal.
      if (location.pathname !== lastPath && document.getElementById(ID)) remove();
      render();
    } catch (e) { }
  }

  window.__un0tvContinue = { render: function () { lastPath = ''; tick(); } };

  setTimeout(tick, 1200);
  setInterval(tick, 2500);
})();
""";
}
