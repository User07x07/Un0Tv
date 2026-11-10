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
// The highlight is a rounded cyan ring drawn over the item, with a soft glow
// and a faint white hairline inside it so it stays readable over a bright
// poster. It is hidden while a movie fills the screen.
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

  // The labels of the top navigation (and the category chips), shared by the
  // matcher below and by "keep the ring on the tab that was pressed" after a
  // page change.
  var NAV_LABELS = ['home', 'movies', 'tv shows', 'browse all', 'continue',
    'trending', 'popular', 'top rated', 'upcoming', 'on air', 'anime',
    'action', 'horror', 'comedies', 'fantasy', 'dramas', 'crime',
    'documentaries', 'documentary', 'sci-fi', 'science fiction', 'thriller',
    'romance', 'mystery', 'war', 'history', 'music', 'western', 'animation',
    'family', 'adventure', 'sport', 'sports', 'nature & planet earth',
    'wild animals & wildlife', 'ocean & marine life',
    // The profile screen's extra card and the channel picker's choices.
    'channels', 'anime channel', 'kids channel', 'cancel'];

  // The nav label of an element, when it is a top-nav item or chip.
  function navLabelOf(el) {
    var text = (el.textContent || '').replace(/\s+/g, ' ').trim().toLowerCase();
    return NAV_LABELS.indexOf(text) >= 0 ? text : null;
  }

  // The nav item with this label. Used after the page changes under a press on
  // a tab, so the ring stays on the tab the viewer chose - Home stays on Home,
  // Movies stays on Movies, and so on. The highest one wins, which is the
  // entry in the site's top bar rather than a same-named chip lower down.
  function navItem(label) {
    if (!label || !items || !items.length) return null;
    var best = null, bestTop = 1e9;
    for (var i = 0; i < items.length; i++) {
      if (navLabelOf(items[i].el) !== label) continue;
      var r = seen(items[i]);
      if (r.top < bestTop) { bestTop = r.top; best = items[i]; }
    }
    return best;
  }

  // Specific navigation elements the cursor should land on.
  // Home, Movies, TV Shows, Browse All, Continue Watching, and movie cards.
  //
  // The site is a React app: its top nav is NOT links but buttons/divs with
  // an onClick handler and a text label, and its posters are divs with a
  // CSS background image rather than <img>. So the match has to look at
  // text and class names, not only at href and <img>.
  function isNavTarget(el) {
    if (!el) return false;
    var tag = (el.tagName || '').toLowerCase();

    // Never a navigation target: boxes the viewer types into and plain
    // interactive controls.
    if (tag === 'input' || tag === 'textarea' || tag === 'select' || tag === 'summary') return false;

    var cls = '';
    try { cls = (el.className && el.className.toString) ? el.className.toString() : ''; }
    catch (e) { cls = ''; }
    var text = (el.textContent || '').replace(/\s+/g, ' ').trim().toLowerCase();

    // Top navigation and the Home category chips, by their label, whatever
    // element the site drew them on (button, div, span, a). The label sits in
    // a child span next to an icon, so compare the whole text, not a substring.
    if (NAV_LABELS.indexOf(text) >= 0) return true;

    // Any chip in a category/filter row - the Movies tab draws its own row of
    // fifteen and the TV Shows tab another - whatever the site called the
    // class. Short text only, so a whole row of prose is not swept in.
    if (text && text.length <= 40
        && (/(chip|pill|category|cat-|filter|genre)/i.test(cls)
            || (el.parentElement && /(categor|chip|filter|genre)/i.test(
                 (el.parentElement.className && el.parentElement.className.toString) || '')))) return true;

    // The numbered page buttons under a list ("1 2 ... 500"), so the ring can
    // walk to them like any other item. The sign-in page's "HOW IT WORKS"
    // steps are numbered circles too, but they are not choices, so those
    // round step badges are left out (see isStepBadge).
    if (/^\d{1,4}$/.test(text)
        && (tag === 'button' || tag === 'li' || tag === 'a'
            || (el.getAttribute('role') || '') === 'button'
            || (cls && CLASS_HINT.test(cls)))) {
      if (isStepBadge(el)) return false;
      return true;
    }

    // The search box and the profile (and anything inside the profile menu).
    if (searchy(el)) return true;
    // The magnifying-glass button in the site's top bar: it is a plain button
    // whose only content is the search icon (an svg), so it has no text and no
    // class to match - it is the button holding that circle-and-line icon.
    if (isSearchIconButton(el)) return true;
    var ident = (el.id || '') + ' ' + cls + ' ' + (el.getAttribute('alt') || '')
              + ' ' + (el.getAttribute('aria-label') || '');
    if (/profile|account|avatar|user/i.test(ident)) return true;

    // Plain links to the same sections.
    if (tag === 'a') {
      var href = el.getAttribute('href') || '';
      if (!href || href === '#') return false;
      if (href.indexOf('/UN0') === 0 || href === '/' || href.indexOf('/home') === 0
          || href.indexOf('/movie') === 0 || href.indexOf('/tv') === 0
          || href.indexOf('/browse') === 0) return true;
      return false;
    }

    // Continue Watching row and movie cards: the site marks them with
    // cv-movie-card / poster-card classes (also used for Continue Watching).
    if (cls && /(movie-card|poster-card|cv-cw|continue|watch-again|resume)/i.test(cls)) return true;

    // Search-result cards: on /UN0/search the results live in a "srch-grid"
    // and each one is a plain <button> whose only content is the poster <img>
    // (plus a small title line). Those buttons carry no class and no href, so
    // they are recognised by their shape: a button holding a picture.
    if (tag === 'button' && el.querySelector('img')
        && (!text || text.length <= 80)) return true;

    // Any other card/poster/tile that actually shows a picture - either an
    // <img> or a CSS background image on itself or a child. The Channels card
    // on the profile screen and the Anime/Kids cards in the channel picker
    // are exactly this: a box with class "pf-card" / "pf-channel-card" holding
    // one image and one short line of text.
    if ((tag === 'div' || tag === 'li' || tag === 'article' || tag === 'button')
        && cls && /(card|poster|tile|pf-card|pf-channel)/i.test(cls)
        && (el.querySelector('img') || hasBackgroundImage(el))) return true;

    return false;
  }

  // The numbered circles on pages like "SIGN IN WITH YOUR PHONE" (the
  // "HOW IT WORKS" steps 1..4): small round badges sitting inside a step
  // list. They look like the page-number buttons, but they are not choices,
  // so the ring must skip them.
  function isStepBadge(el) {
    try {
      var r = el.getBoundingClientRect();
      var style = '';
      try { style = getComputedStyle(el).borderRadius || ''; } catch (e) { }
      // A small near-square box with a fully round corner is a badge circle.
      if (r.width > 0 && r.width < 90
          && Math.abs(r.width - r.height) <= Math.max(6, r.width * 0.3)
          && /%|px/.test(style) && parseFloat(style) >= r.width * 0.4) return true;
      // Or it sits inside a "how it works" / steps list.
      var p = el;
      for (var i = 0; i < 5 && p; i++, p = p.parentElement) {
        var c = ((p.className && p.className.toString) ? p.className.toString() : '')
              + ' ' + (p.id || '') + ' ' + (p.getAttribute('aria-label') || '');
        if (/(step|how-it-works|howitworks|guide|instruction)/i.test(c)) return true;
      }
    } catch (e) { }
    return false;
  }

  // True when the element or one of its children paints a background image,
  // which is how the site draws its posters.
  function hasBackgroundImage(el) {
    try {
      if (/background(-image)?\s*:/.test(el.getAttribute('style') || '')) return true;
      var kids = el.children;
      for (var i = 0; i < kids.length && i < 6; i++)
        if (/background(-image)?\s*:/.test(kids[i].getAttribute('style') || '')) return true;
    } catch (e) { }
    return false;
  }

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

  // Anything a viewer types into directly. A search box that merely wraps an
  // input is not a field itself - it is handled through searchBox below.
  function fieldish(el) {
    if (!el) return false;
    if (fieldTag(el)) return true;
    if (el.isContentEditable) return true;
    return false;
  }

  // Could a viewer press this?
  // Restricted to the navigation targets above: top nav, the category chips,
  // movie cards, the search box and the profile.
  function clickable(el) {
    // Only allow navigation targets
    if (!isNavTarget(el)) return false;
    
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
    // The top-left item on the page is where the ring starts.
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
        + 'box-sizing:border-box;border:3px solid #7DD3FC;border-radius:12px;'
        + 'box-shadow:0 0 0 2px rgba(0,0,0,.6),inset 0 0 0 1px rgba(255,255,255,.35),'
        + '0 0 18px rgba(56,189,248,.55),0 0 2px rgba(125,211,252,.9);'
        + 'transition:transform .1s ease-out,width .1s ease-out,height .1s ease-out;'
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

  // Where the ring was when OK was pressed, so that if the page replaces the
  // box the ring sat on, the ring can land on the nearest new item instead of
  // jumping back to the top of the page.
  var lastRect = null;

  // The nav tab that was last pressed with OK, so the ring can stay on it
  // when the page replaces everything under it.
  var lastNavLabel = null;

  // The ring hides itself once the remote has been idle for a moment, so a
  // ring left on screen does not sit over the page forever. Any key press
  // restarts the timer and brings it back.
  var idleTimer = 0;
  function poke() {
    try {
      if (idleTimer) clearTimeout(idleTimer);
      idleTimer = setTimeout(function () { try { hideBox(); } catch (e) { } }, 2500);
    } catch (e) { }
  }

  function setCur(it) {
    cur = it.el;
    poke();
    draw();
    reveal(cur);
    sayField(fieldish(cur) || searchBox(cur));
  }

  // Every ancestor box that can scroll on its own - the site often scrolls
  // inside a fixed div instead of the window.
  function scrollableAncestors(el) {
    var out = [];
    try {
      var p = el || document.body;
      while (p && p !== document.documentElement) {
        var st = null;
        try { st = getComputedStyle(p); } catch (e) { }
        if (st && /(auto|scroll|overlay)/.test(st.overflowY + ' ' + st.overflowX)
            && (p.scrollHeight > p.clientHeight + 4 || p.scrollWidth > p.clientWidth + 4))
          out.push(p);
        p = p.parentElement;
      }
    } catch (e) { }
    return out;
  }

  // Scroll the window and every scrollable inner box, then send a wheel event
  // for pages that drive their own scrolling from it. Used when the remote is
  // pressed past the last item in a direction, so the rows below (or the next
  // cards in a carousel row) are still reachable.
  function scrollPage(sx, sy) {
    try {
      if (sy) {
        try { window.scrollBy({ top: sy, left: 0, behavior: 'smooth' }); }
        catch (e) { window.scrollBy(0, sy); }
      }
      if (sx) {
        try { window.scrollBy({ top: 0, left: sx, behavior: 'smooth' }); }
        catch (e) { window.scrollBy(sx, 0); }
      }
      var anc = scrollableAncestors(cur);
      for (var i = 0; i < anc.length; i++) {
        try {
          if (sy) anc[i].scrollBy({ top: sy, left: 0, behavior: 'smooth' });
          if (sx) anc[i].scrollBy({ top: 0, left: sx, behavior: 'smooth' });
        } catch (e) { }
      }
      if (cur) fire(cur, 'wheel', WheelEvent,
        { deltaY: sy, deltaX: sx, bubbles: true, cancelable: true });
    } catch (e) { }
    follow();
  }

  // The item whose centre is closest to a point - used after the page replaces
  // the box the ring was sitting on.
  function nearestTo(rect) {
    if (!items || !items.length || !rect) return null;
    var cx = (rect.left + rect.right) / 2, cy = (rect.top + rect.bottom) / 2;
    var best = null, bestD = 1e12;
    for (var i = 0; i < items.length; i++) {
      var r = seen(items[i]);
      var ix = (r.left + r.right) / 2, iy = (r.top + r.bottom) / 2;
      var d = (ix - cx) * (ix - cx) + (iy - cy) * (iy - cy);
      if (d < bestD) { bestD = d; best = items[i]; }
    }
    return best;
  }

  function move(dx, dy) {
    poke();
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

    if (!cur) {
      // After a press the page may have replaced the box the ring was on. A
      // press on a top-nav tab keeps the ring on that same tab; anything else
      // lands on the item nearest to where the ring was, not back at the top.
      var from = lastRect; lastRect = null;
      var f = lastNavLabel ? navItem(lastNavLabel) : null;
      if (!f) f = from ? nearestTo(from) : null;
      if (!f) f = first();
      if (f) setCur(f);
      return;
    }

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
    if (best) { setCur(best); return; }
    // Nothing any further in that direction: scroll the page itself, so the
    // rows lower down (or the next cards in a carousel row) are still
    // reachable with the remote. The site may scroll inside its own box rather
    // than the window, so every scrollable ancestor moves too.
    if (dy !== 0 || dx !== 0) {
      var stepY = dy !== 0 ? Math.round(innerHeight * 0.8) * (dy > 0 ? 1 : -1) : 0;
      var stepX = dx !== 0 ? Math.round(innerWidth * 0.8) * (dx > 0 ? 1 : -1) : 0;
      scrollPage(stepX, stepY);
    }
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

  // The search bar itself (a real input, or a plain box holding one): OK on
  // it opens the keys and must not also press the box, which would open the
  // site's own search page and hide them.
  function searchBox(el) {
    if (!el) return false;
    if (fieldTag(el) || el.isContentEditable) return true;
    if (searchy(el) && el.querySelector('input,textarea,[contenteditable="true"]')) return true;
    return false;
  }

  // The magnifying-glass button: OK presses it, which opens the site's search
  // bar; the keys are then opened on that bar's input.
  function searchToggle(el) {
    if (!el || fieldTag(el) || el.isContentEditable) return false;
    if (isSearchIconButton(el)) return true;
    if (!searchy(el)) return false;
    return !el.querySelector('input,textarea,[contenteditable="true"]');
  }

  // The magnifying-glass button in the site's header (the round 40x40 button
  // whose only child is the search icon svg). It has no text and no class, so
  // it is told apart by its shape and by the icon's circle-and-handle lines.
  function isSearchIconButton(el) {
    if (!el) return false;
    var tag = (el.tagName || '').toLowerCase();
    if (tag !== 'button' && tag !== 'div' && tag !== 'a' && tag !== 'span') return false;
    if (el.querySelector('input,textarea,[contenteditable="true"]')) return false;
    var text = (el.textContent || '').replace(/\s+/g, ' ').trim();
    if (text.length > 0) return false;
    var svg = el.querySelector('svg');
    if (!svg) return false;
    var circ = svg.querySelector('circle');
    if (!circ) return false;
    // The lucide Search icon: one circle plus the handle line. A close enough
    // circle (the profile avatar is drawn the same way) is told apart by the
    // handle, which only the magnifier has.
    var r = parseFloat(circ.getAttribute('r') || '0');
    if (!(r >= 6 && r <= 12)) return false;
    var line = svg.querySelector('line');
    if (line) return true;
    var paths = svg.querySelectorAll('path');
    for (var i = 0; i < paths.length; i++) {
      var d = paths[i].getAttribute('d') || '';
      // 'm21 21-4.35-4.35' style handle, or any short diagonal stroke.
      if (/m\s*2[01]\s*2[01]\s*[-l]/i.test(d)) return true;
    }
    return false;
  }

  // The input the site revealed after its search button was pressed.
  function findSearchInput() {
    var cand = document.querySelectorAll('input[type="search"],input[placeholder],input:not([type])');
    var i, el, r;
    for (i = 0; i < cand.length; i++) {
      el = cand[i];
      var hint = ((el.getAttribute('placeholder') || '') + ' '
                + (el.getAttribute('aria-label') || '')).toLowerCase();
      r = el.getBoundingClientRect();
      if (r.width > 40 && r.height > 10
          && (hint.indexOf('search') >= 0 || hint.indexOf('find') >= 0)) return el;
    }
    for (i = 0; i < cand.length; i++) {
      el = cand[i];
      r = el.getBoundingClientRect();
      if (r.width > 60 && r.height > 12 && r.bottom > 0 && r.top < innerHeight) return el;
    }
    return null;
  }

  function activate() {
    poke();
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

    if (curIsField || fieldish(cur) || searchBox(cur)) { openField(cur); return; }

    // The magnifying glass: press it, then open the app's keys on the search
    // bar it reveals (the "SEARCH MOVIES & TV..." box), so the viewer can type.
    if (searchToggle(cur)) {
      var t = cur;
      try { t.focus({ preventScroll: true }); } catch (e) { }
      var o2 = { bubbles: true, cancelable: true, view: window };
      fire(t, 'pointerdown', PointerEvent, o2);
      fire(t, 'mousedown', MouseEvent, o2);
      fire(t, 'pointerup', PointerEvent, o2);
      fire(t, 'mouseup', MouseEvent, o2);
      try { t.click(); } catch (e) { }
      items = null;   // the page changed under us
      // The site swaps the button for its search box when pressed; wait for
      // the input to appear, then put the ring on it and open the app's keys.
      var tries = 0;
      (function waitForBox() {
        tries++;
        try {
          var f = findSearchInput();
          if (f) { cur = f; setCur({ el: f, r: rectOf(f) }); openField(f); return; }
        } catch (e) { }
        if (tries < 12) setTimeout(waitForBox, 150);
      })();
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

    // A press on a top-nav tab is remembered, so the ring stays on that tab
    // after the page changes under it (Home stays on Home, Movies on Movies).
    lastNavLabel = navLabelOf(cur);
    try { lastRect = rectOf(cur); } catch (e) { }
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
        if (cur && cur.isConnected && (curIsField || fieldish(cur) || searchBox(cur)))
          cur.focus({ preventScroll: true });
      } catch (e) { }
    },
    // Puts the ring on the first item of a freshly loaded page - and only while
    // the viewer has not chosen anything yet, so their own moving around is
    // left alone.
    homeIfNothing: function () {
      try {
        if (moved) return;
        ensure();
        var f = first();
        if (f) { poke(); setCur(f); }
      } catch (e) { }
    },
    // The item the ring is on, so the typing script can look inside it for the
    // site's real search input.
    current: function () { return cur; },
    // Number keys 1..9 then 0: put the ring on the nth selectable item
    // (1-based, 0 is the tenth), so a long row needs no long walk.
    jump: function (n) {
      try {
        moved = true;
        poke();
        ensure();
        if (!items || !items.length) return;
        var i = n <= 0 ? 9 : n - 1;
        if (i >= items.length) i = items.length - 1;
        setCur(items[i]);
      } catch (e) { }
    },
    clear: function () { cur = null; curIsField = false; if (idleTimer) clearTimeout(idleTimer); hideBox(); }
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
