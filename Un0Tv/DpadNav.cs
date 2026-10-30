namespace Un0Tv;

// D-pad navigation for the site, which was built for phones and has no TV
// navigation of its own.
//
// The app catches the remote's arrows and calls into this script, which keeps
// its own idea of "where you are" and moves it to the nearest clickable thing
// in the direction pressed. It deliberately does not rely on the browser's
// focus movement, because most of this site's items are plain boxes rather
// than real buttons and never take focus.
//
// The highlight is a thin blue outline drawn over the item, not a heavy
// glowing box, and it is hidden while a movie fills the screen.
//
// Speed matters: the list of clickable things is built at most once every
// 0.9s, the outline is moved with a transform (no layout work), and scrolling
// only repositions it once per frame.
public static class DpadNav
{
    public static readonly string Js = """
(function () {
  // Only the page the viewer is looking at gets the highlight; the movie
  // player's own frame runs this script too and must be left alone.
  if (window.top !== window) return;
  if (window.__un0tvNav) return;

  var box = null;        // the highlight
  var cur = null;        // the item it sits on
  var items = null;      // clickable things, with their boxes at build time
  var builtAt = 0;
  var builtSx = 0, builtSy = 0;
  var frame = 0;

  // Class names that mark a clickable box on a site built from plain divs.
  var CLASS_HINT = /(btn|button|card|item|link|tab|chip|tile|option|menu|poster|play|episode|season)/i;

  function own(el) {
    try { return !!(el && el.getAttribute && el.getAttribute('data-un0tv-own') === '1'); }
    catch (e) { return false; }
  }

  function rectOf(el) {
    try { return el.getBoundingClientRect(); } catch (e) { return null; }
  }

  // Big enough to aim at, and actually on the screen.
  function usable(r) {
    if (!r) return false;
    if (r.width < 16 || r.height < 12) return false;
    if (r.width > innerWidth * 0.99 && r.height > innerHeight * 0.99) return false;
    if (r.bottom < -20 || r.top > innerHeight + 20) return false;
    if (r.right < -20 || r.left > innerWidth + 20) return false;
    return true;
  }

  // Could a viewer press this?
  function clickable(el) {
    var tag = (el.tagName || '').toLowerCase();
    if (tag === 'button' || tag === 'select' || tag === 'textarea' || tag === 'summary') return true;
    if (tag === 'input') {
      var t = (el.type || 'text').toLowerCase();
      return t !== 'hidden';
    }
    if (tag === 'a') {
      var href = el.getAttribute('href') || '';
      return href !== '' && href !== '#';
    }
    var role = (el.getAttribute('role') || '').toLowerCase();
    if (role === 'button' || role === 'link' || role === 'tab'
        || role === 'menuitem' || role === 'option') return true;
    var ti = el.getAttribute('tabindex');
    if (ti !== null && ti !== '' && parseInt(ti, 10) >= 0) return true;
    if (el.hasAttribute('onclick')) return true;
    var cls = '';
    try { cls = (el.className && el.className.toString) ? el.className.toString() : ''; }
    catch (e) { cls = ''; }
    if (cls && CLASS_HINT.test(cls)) return true;
    return false;
  }

  // A wrapper and the item it holds can both look clickable. Keep the inner
  // one, so pressing Down steps item to item instead of landing twice.
  function prune(list) {
    var out = [];
    for (var i = 0; i < list.length; i++) {
      var a = list[i], keep = true;
      var area = a.r.width * a.r.height;
      for (var j = 0; j < list.length; j++) {
        if (i === j) continue;
        var b = list[j];
        try {
          if (!a.el.contains(b.el)) continue;
        } catch (e) { continue; }
        var barea = b.r.width * b.r.height;
        if (barea >= area * 0.80 && barea <= area * 1.05) { keep = false; break; }
      }
      if (keep) out.push(a);
    }
    return out;
  }

  function gather() {
    if (!document.body) return [];
    var all = document.body.getElementsByTagName('*');
    var n = all.length > 6000 ? 6000 : all.length;
    var list = [];
    for (var i = 0; i < n; i++) {
      var el = all[i];
      if (own(el)) continue;
      var tag = el.tagName;
      if (tag === 'SCRIPT' || tag === 'STYLE' || tag === 'LINK'
          || tag === 'NOSCRIPT' || tag === 'META' || tag === 'svg') continue;
      if (!clickable(el)) continue;
      var r = rectOf(el);
      if (!usable(r)) continue;
      list.push({ el: el, r: r });
    }
    return prune(list);
  }

  function ensure() {
    var now = Date.now();
    if (items && now - builtAt < 900) return;
    items = gather();
    builtAt = now;
    builtSy = window.scrollY || 0;
    builtSx = window.scrollX || 0;
  }

  // A stored box, corrected for any scrolling since it was measured.
  function seen(it) {
    var dx = (window.scrollX || 0) - builtSx;
    var dy = (window.scrollY || 0) - builtSy;
    var r = it.r;
    return {
      left: r.left - dx, right: r.right - dx,
      top: r.top - dy, bottom: r.bottom - dy
    };
  }

  function first() {
    var best = null, bestTop = 1e9, bestLeft = 1e9;
    for (var i = 0; i < items.length; i++) {
      var r = seen(items[i]);
      if (r.top < bestTop - 4 || (Math.abs(r.top - bestTop) <= 4 && r.left < bestLeft)) {
        best = items[i]; bestTop = r.top; bestLeft = r.left;
      }
    }
    return best;
  }

  function hideBox() {
    if (box) box.style.display = 'none';
  }

  function draw() {
    if (!cur || !cur.isConnected) { hideBox(); return; }
    var r = rectOf(cur);
    if (!r || r.width < 4 || r.height < 4) { hideBox(); return; }
    if (!box) {
      box = document.createElement('div');
      box.setAttribute('data-un0tv-own', '1');
      box.style.cssText =
        'position:fixed;left:0;top:0;pointer-events:none;z-index:2147483600;'
        + 'box-sizing:border-box;border:3px solid #4da3ff;border-radius:10px;'
        + 'box-shadow:0 0 0 2px rgba(0,0,0,.55),0 0 14px rgba(77,163,255,.7);'
        + 'transition:transform .1s linear,width .1s linear,height .1s linear;'
        + 'will-change:transform;display:none;';
      (document.body || document.documentElement).appendChild(box);
    }
    box.style.width = Math.max(8, Math.round(r.width)) + 'px';
    box.style.height = Math.max(8, Math.round(r.height)) + 'px';
    box.style.transform = 'translate3d(' + Math.round(r.left) + 'px,'
                        + Math.round(r.top) + 'px,0)';
    box.style.display = 'block';
  }

  // Keep the highlight on screen without a burst of layout on every key.
  function follow() {
    if (frame) return;
    frame = requestAnimationFrame(function () { frame = 0; try { draw(); } catch (e) { } });
  }

  function reveal(el) {
    try {
      var r = el.getBoundingClientRect();
      if (r.top < 10 || r.bottom > innerHeight - 10
          || r.left < 10 || r.right > innerWidth - 10) {
        el.scrollIntoView({ block: 'center', inline: 'center', behavior: 'smooth' });
      }
    } catch (e) { }
  }

  function setCur(it) {
    cur = it.el;
    draw();
    reveal(cur);
  }

  function move(dx, dy) {
    ensure();
    if (!items || !items.length) return;
    if (!cur || !cur.isConnected) cur = null;

    if (!cur) { var f = first(); if (f) setCur(f); return; }

    var cr = rectOf(cur);
    if (!cr) { cur = null; return; }
    var cx = (cr.left + cr.right) / 2;
    var cy = (cr.top + cr.bottom) / 2;

    var best = null, bestScore = 1e12;
    for (var i = 0; i < items.length; i++) {
      var it = items[i];
      if (it.el === cur) continue;
      var r = seen(it);
      var ix = (r.left + r.right) / 2;
      var iy = (r.top + r.bottom) / 2;
      var along, across, score;
      if (dy !== 0) {
        along = (iy - cy) * dy;
        if (along <= 8) continue;
        across = Math.abs(ix - cx);
        score = along + across * 1.6;
      } else {
        along = (ix - cx) * dx;
        if (along <= 8) continue;
        across = Math.abs(iy - cy);
        score = along + across * 1.6;
      }
      if (score < bestScore) { bestScore = score; best = it; }
    }
    if (best) setCur(best);
  }

  function fire(el, name, Ctor, opts) {
    try { el.dispatchEvent(new Ctor(name, opts)); } catch (e) { }
  }

  function activate() {
    ensure();
    if (!cur || !cur.isConnected) {
      var f = first();
      if (!f) return;
      setCur(f);
      return;
    }
    try { cur.focus({ preventScroll: true }); } catch (e) { }

    var tag = (cur.tagName || '').toLowerCase();
    if (tag === 'input' || tag === 'textarea' || tag === 'select') {
      // A field: Enter should submit it, not press it.
      try {
        var ev = new KeyboardEvent('keydown', {
          key: 'Enter', code: 'Enter', keyCode: 13, which: 13,
          bubbles: true, cancelable: true
        });
        cur.dispatchEvent(ev);
        if (cur.form && cur.form.requestSubmit) cur.form.requestSubmit();
      } catch (e) { }
      return;
    }

    var o = { bubbles: true, cancelable: true, view: window };
    fire(cur, 'pointerdown', PointerEvent, o);
    fire(cur, 'mousedown', MouseEvent, o);
    fire(cur, 'pointerup', PointerEvent, o);
    fire(cur, 'mouseup', MouseEvent, o);
    try { cur.click(); } catch (e) { }

    items = null;   // the page probably changed under us
  }

  window.__un0tvNav = {
    move: function (dx, dy) { try { move(dx, dy); } catch (e) { } },
    activate: function () { try { activate(); } catch (e) { } },
    clear: function () { cur = null; hideBox(); }
  };

  window.addEventListener('scroll', follow, true);
  window.addEventListener('resize', follow);

  // A movie in fullscreen gets the whole screen, so the highlight must not sit
  // over the picture.
  function onFull() {
    try {
      if (document.fullscreenElement || document.webkitFullscreenElement) hideBox();
      else if (cur) draw();
    } catch (e) { }
  }
  document.addEventListener('fullscreenchange', onFull);
  document.addEventListener('webkitfullscreenchange', onFull);
})();
""";
}
