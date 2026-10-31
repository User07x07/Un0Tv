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
// Pressing OK on a box the viewer can type in tells the app to open its own
// on-screen keyboard, and the box is kept as the place the letters go - a TV
// has no touch screen, so a box alone can never raise Android's keyboard.
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
  var curIsField = false;// the item is a box the viewer can type in
  var items = null;      // clickable things, with their boxes at build time
  var builtAt = 0;
  var builtSx = 0, builtSy = 0;
  var frame = 0;

  // Class names that mark a clickable box on a site built from plain divs.
  var CLASS_HINT = /(btn|button|card|item|link|tab|chip|tile|option|menu|poster|play|episode|season|continue|search|row|title|hero)/i;

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

  function fieldTag(el) {
    var tag = (el.tagName || '').toLowerCase();
    if (tag === 'textarea') return true;
    if (tag === 'input') {
      var t = (el.type || 'text').toLowerCase();
      return t !== 'hidden' && t !== 'checkbox' && t !== 'radio'
          && t !== 'submit' && t !== 'button' && t !== 'file';
    }
    return false;
  }

  // A box the viewer types a search into. The site draws its search box as a
  // plain div holding the real input, so the box on its own is not an input
  // and would never ask for the app's keyboard.
  function searchy(el) {
    if (!el) return false;
    var hint = '';
    try {
      hint = ((el.getAttribute('placeholder') || '') + ' '
            + (el.getAttribute('aria-label') || '') + ' '
            + (el.getAttribute('name') || '') + ' '
            + (el.id || '') + ' '
            + (el.className && el.className.toString ? el.className.toString() : '')).toLowerCase();
    } catch (e) { hint = ''; }
    if (hint.indexOf('search') >= 0 || hint.indexOf('find') >= 0) return true;
    try {
      if (el.querySelector
          && el.querySelector('input[type="search"],input[placeholder],input:not([type])'))
        return true;
    } catch (e) { }
    return false;
  }

  // Anything a viewer types into, whether the site wrapped it or not.
  function fieldish(el) {
    if (!el) return false;
    if (fieldTag(el)) return true;
    if (el.isContentEditable) return true;
    return searchy(el);
  }

  // Could a viewer press this?
  function clickable(el) {
    var tag = (el.tagName || '').toLowerCase();
    // The Continue watching cards are ours, so they can always be pressed.
    if (el.hasAttribute('data-un0tv-continue')) return true;
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
    // Last resort, so a plain box drawn by the site can still be chosen: the
    // site marks anything pressable with a pointer cursor, and a box holding
    // a picture and a line of words reads as a card.
    if (tag === 'div' || tag === 'li' || tag === 'article' || tag === 'span' || tag === 'section') {
      if (el.children.length >= 1 && el.children.length <= 4) {
        var txt = (el.textContent || '').replace(/\s+/g, ' ').trim();
        if (el.querySelector('img') && txt.length <= 80) return true;
      }
      try { if (getComputedStyle(el).cursor === 'pointer') return true; } catch (e) { }
    }
    return false;
  }

  // A wrapper and the item it holds can both look clickable. Keep the inner
  // one, so pressing Down steps item to item instead of landing twice.
  function prune(list) {
    var out = [];
    for (var i = 0; i < list.length; i++) {
      var a = list[i], keep = true;
      // One card is one stop: its picture and its words belong to it.
      if (a.el.hasAttribute('data-un0tv-continue')) { out.push(a); continue; }
      var inCard = false;
      try { inCard = !!a.el.closest('[data-un0tv-continue]'); } catch (e) { inCard = false; }
      if (inCard) continue;
      var area = Math.max(1, a.r.width * a.r.height);
      for (var j = 0; j < list.length; j++) {
        if (i === j) continue;
        var b = list[j];
        try {
          if (!a.el.contains(b.el)) continue;
        } catch (e) { continue; }
        var barea = b.r.width * b.r.height;
        // The inner box covers nearly the whole wrapper: the wrapper is only a
        // holder, so it goes.
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
    // Where the viewer left off is the first thing the ring should find - the
    // Continue watching cards. They are looked for on the page itself as well,
    // because the strip is drawn after the list was last built.
    try {
      var card = document.querySelector('[data-un0tv-continue]');
      if (card) {
        var rc = rectOf(card);
        if (usable(rc)) return { el: card, r: rc };
      }
    } catch (e) { }
    for (var c = 0; c < items.length; c++) {
      if (items[c].el.hasAttribute('data-un0tv-continue'))
        return items[c];
    }
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
    // A box the viewer types a search into must not be outlined: it only needs
    // the keys, and the outline over it looks like a fault.
    if (curIsField) { hideBox(); return; }
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

  // Tell the app the ring is on a box the viewer can type in, so it knows a
  // box is waiting for letters.
  function sayField(on) {
    try {
      if (on === curIsField) return;
      curIsField = on;
    } catch (e) { }
  }

  // Has the viewer moved the ring at all yet? The Continue cards only take the
  // ring on their own while nothing has been chosen.
  var moved = false;

  function setCur(it) {
    cur = it.el;
    draw();
    reveal(cur);
    sayField(fieldish(cur));
  }

  function move(dx, dy) {
    // The first arrow press on a search box is the one that clears it: the box
    // changes nothing on screen, so Down would look like a dead button.
    if (cur && curIsField && cur.isConnected) {
      curIsField = false;
      moved = true;
      draw();
      return;
    }
    moved = true;
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

  // A field: the TV has no touch screen, so the app's own keyboard is asked
  // for and the letters are sent into this box (see FieldInput / the app's
  // keyboard panel).
  function openField(el) {
    try {
      cur = el;
      curIsField = true;
      draw();
      // The site's search box is a plain box holding the real input, so the
      // input itself is what has to take the letters. FieldInput finds it.
      var took = false;
      try {
        if (window.__un0tvField && window.__un0tvField.focusBest)
          took = window.__un0tvField.focusBest() === true;
      } catch (e) { }
      if (!took) el.focus({ preventScroll: true });
    } catch (e) { }
    // The app's own keyboard, raised on OK. A TV has no touch screen, so the
    // box alone can never bring up Android's keyboard.
    try {
      if (window.Un0TvNative && window.Un0TvNative.openKeyboard)
        window.Un0TvNative.openKeyboard();
    } catch (e) { }
  }

  // The site's search box is a plain box, so OK on it must not also press the
  // box: that would open the site's own search page and hide the keys.
  function searchBox(el) {
    if (!el) return false;
    if (fieldTag(el) || el.isContentEditable) return true;
    return searchy(el);
  }

  function activate() {
    ensure();
    // Remember that the viewer just pressed OK, so a box that only takes
    // typing after a press knows it was meant to be typed in.
    try { window.__un0tvActedAt = Date.now(); } catch (e) { }
    if (!cur || !cur.isConnected) {
      var f = first();
      if (!f) return;
      setCur(f);
      return;
    }

    if (curIsField || fieldish(cur)) { openField(cur); return; }

    if (searchBox(cur)) { openField(cur); return; }

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
    // The app's keyboard asks for the box once more when it types, because the
    // page may have moved under the outline in the meantime.
    refocus: function () {
      // The box the letters go into, not the box around it: the site's search
      // is a plain div holding the real input.
      try {
        if (window.__un0tvField && window.__un0tvField.focusBest)
          if (window.__un0tvField.focusBest() === true) return;
      } catch (e) { }
      try {
        if (cur && cur.isConnected && (curIsField || fieldish(cur)))
          cur.focus({ preventScroll: true });
      } catch (e) { }
    },
    // The Continue strip is drawn after the page has settled, so it asks the
    // ring to move onto the first card once - and only while the viewer has not
    // chosen anything yet, so their own moving around is left alone.
    homeIfNothing: function () {
      try {
        if (moved) return;
        if (cur && cur.isConnected && cur.hasAttribute
            && cur.hasAttribute('data-un0tv-continue')) return;
        ensure();
        var f = first();
        if (f) setCur(f);
      } catch (e) { }
    },
    // The item the ring is on, so the typing script can look inside it for the
    // site's real search input.
    current: function () { return cur; },
    clear: function () { cur = null; curIsField = false; hideBox(); }
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
