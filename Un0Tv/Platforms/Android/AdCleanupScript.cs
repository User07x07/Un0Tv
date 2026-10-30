namespace Un0Tv;

// The QR advert seen on the TV is drawn INSIDE the movie player's own frame.
// The site's page cannot touch it, and blocking requests does not stop it
// either, because the player itself draws it.
//
// This script is registered to run at "document start" in EVERY frame of the
// page - the site, and the player's frame too - so it can reach in there and
// take the advert away before it is ever shown.
//
// Three shapes of cover are handled:
//   1) a floating layer sitting on top of the player (step 1 and 1b),
//   2) a whole wall that replaces the player's page - the "confirm you are not
//      a robot" screen with a QR on it (step 2b), and
//   3) a second QR layer that counts down and then reveals a close (X) button
//      (step 1c). It leaves after about ten seconds by itself, but it hides
//      the movie until then, so its close button is pressed at once.
//
// The rule that keeps playback safe throughout: nothing that contains a
// <video> is ever removed, and nothing at all is done to the site's own page
// (only frames nested inside a player are ever cleared).
internal static class AdCleanupScript
{
    public static readonly string Js = """
(function () {
  if (window.__un0tvAds) return;
  window.__un0tvAds = 1;
  window.__un0tvKilled = 0;

  // Wording that only ever appears on an advert, a fake "human check", or a
  // "install our extension" nag. deliberately narrow: a real player control is
  // never worded like this.
  // NOTE: "you are" is accepted as well as "you're" - the cover on the TV says
  // "CONFIRM YOU ARE NOT A ROBOT", which the old "you're" - only rule missed.
  var AD_TEXT = [
    /scan\s+(this\s+|the\s+)?qr/i,
    /scan\s+qr/i,
    /scan\s+the\s+code/i,
    /qr\s*code\s+(to|and)\s+/i,
    /(confirm|verify|prove)\s+you\s+are\s+(not\s+)?(a\s+)?(robot|human)/i,
    /(confirm|verify|prove)\s+you.{0,3}re\s+(not\s+)?(a\s+)?(robot|human)/i,
    /(not|n.t)\s+a\s+robot/i,
    /are\s+you\s+a\s+(robot|human)/i,
    /complete\s+the\s+captcha/i,
    /human\s+verification/i,
    /security\s+check\s+required/i,
    /install\s+(the\s+|our\s+)?extension/i,
    /get\s+(the\s+|our\s+)?extension/i,
    /download\s+this\s+video/i,
    /say\s+goodbye\s+to\s+popups/i,
    /stop\s+popups/i,
    /clean\s+browsing/i
  ];

  // A shortlist used only when a whole frame turns out to be the advert.
  // Deliberately NOT /qr\s*code/ on its own: the site's own login screen has a
  // "QR CODE LOGIN" button, and a plain match wiped that page black.
  var STRONG_AD_TEXT = [
    /scan\s+(this\s+|the\s+)?qr/i,
    /scan\s+qr/i,
    /scan\s+the\s+code/i,
    /(confirm|verify|prove)\s+you\s+are\s+(not\s+)?(a\s+)?(robot|human)/i,
    /(confirm|verify|prove)\s+you.{0,3}re\s+(not\s+)?(a\s+)?(robot|human)/i,
    /(not|n.t)\s+a\s+robot/i,
    /are\s+you\s+a\s+(robot|human)/i,
    /complete\s+the\s+captcha/i,
    /human\s+verification/i
  ];

  // A real account page is never an advert wall, even if it happens to show a
  // QR (the site's own login shows one).
  var REAL_PAGE = /log\s*in|sign\s*in|password|e-?mail|username/i;

  // A countdown on a cover: "10", "10s", "10 seconds", "skip in 5". Only ever
  // acted on together with a close button on a layer that covers the movie.
  var COUNTDOWN = /\b\d{1,2}\s*(s|sec|secs|second|seconds)\b|\bskip\s+(the\s+)?ad\b|\b(skip|close)\s+in\s+\d/i;

  // A bare small number, the way a "30" countdown is drawn. Only ever used
  // together with a short piece of text and a close control on a layer that
  // covers the movie, so a normal page is never caught by it.
  var COUNTDOWN_LOOSE = /^[^0-9]{0,12}\d{1,2}[^0-9]{0,12}$/;

  var lastSweep = 0;

  function matches(list, text) {
    if (!text) return false;
    for (var i = 0; i < list.length; i++) {
      if (list[i].test(text)) return true;
    }
    return false;
  }

  function textOf(el) {
    try { return (el.innerText || "").trim(); } catch (e) { return ""; }
  }

  // The player itself, and anything that holds it, is untouchable.
  function hasVideo(el) {
    try {
      if (!el) return false;
      if (el.tagName && el.tagName.toLowerCase() === "video") return true;
      return !!el.querySelector("video");
    } catch (e) { return false; }
  }

  function videoCount() {
    try { return document.querySelectorAll("video").length; } catch (e) { return 0; }
  }

  // An advert cover is a big floating layer.
  function isOverlay(el) {
    try {
      var s = getComputedStyle(el);
      if (s.position !== "fixed" && s.position !== "absolute") return false;
      if (s.display === "none" || s.visibility === "hidden") return false;
      var w = el.offsetWidth, h = el.offsetHeight;
      if (w < 150 || h < 120) return false;
      return w >= innerWidth * 0.30 || h >= innerHeight * 0.30;
    } catch (e) { return false; }
  }

  // Our own controls must never be swept away as "adverts".
  function isOurs(el) {
    try {
      return !!(el && el.getAttribute && el.getAttribute("data-un0tv-own") === "1");
    } catch (e) { return false; }
  }

  function remove(el) {
    try {
      if (!el || !el.parentNode) return;
      if (isOurs(el)) return;                         // the viewer's own controls
      if (hasVideo(el)) return;                       // never the player
      el.remove();
      window.__un0tvKilled++;
    } catch (e) { }
  }

  // Empties this frame's page but keeps its scripts, styles and any video, so
  // the player's own code is still there to carry on once the wall is gone.
  function clearFrame() {
    try {
      var kids = document.body.children;
      for (var m = kids.length - 1; m >= 0; m--) {
        var c = kids[m];
        var tag = (c.tagName || "").toLowerCase();
        if (tag === "script" || tag === "style" || tag === "link" || tag === "video") continue;
        if (isOurs(c)) continue;                       // the viewer's own controls
        if (hasVideo(c)) continue;
        try { c.remove(); } catch (e) { }
      }
      window.__un0tvKilled++;
    } catch (e) { }
  }

  // Tidy away what the cover leaves behind: a locked scroll bar and a page
  // that refuses to scroll are the usual leftovers.
  function unlockScroll() {
    try {
      if (document.body && document.body.style) {
        if (document.body.style.overflow === "hidden") document.body.style.overflow = "";
        if (document.body.style.position === "fixed" && document.body.style.top) {
          document.body.style.position = "";
          document.body.style.top = "";
        }
      }
      var de = document.documentElement;
      if (de && de.style && de.style.overflow === "hidden") de.style.overflow = "";
    } catch (e) { }
  }

  function isCover(el) {
    try {
      var s = getComputedStyle(el);
      if (s.position !== "fixed" && s.position !== "absolute") return false;
      if (s.display === "none" || s.visibility === "hidden") return false;
      if (parseFloat(s.opacity) === 0) return false;
      var w = el.offsetWidth, h = el.offsetHeight;
      if (w < 200 || h < 150) return false;
      return w >= innerWidth * 0.45 && h >= innerHeight * 0.40;
    } catch (e) { return false; }
  }

  // A square picture or canvas inside a layer - that is how a QR is drawn.
  function bigSquare(el) {
    try {
      var parts = el.querySelectorAll("canvas, img, svg");
      for (var i = 0; i < parts.length; i++) {
        var p = parts[i];
        var w = p.offsetWidth || p.width, h = p.offsetHeight || p.height;
        if (w < 100 || h < 100) continue;
        if (Math.abs(w - h) > w * 0.15) continue;
        return true;
      }
    } catch (e) { }
    return false;
  }

  // Is this element the cover's own close control - an X, "close", or "skip"?
  function isCloseControl(el) {
    try {
      if (!el || !el.tagName) return false;
      var tag = el.tagName.toLowerCase();
      if (tag === "video" || tag === "canvas") return false;
      var t = (el.innerText || el.textContent || "").replace(/\s+/g, " ").trim().toLowerCase();
      var aria = "", cls = "";
      try { aria = ((el.getAttribute("aria-label") || "") + " " + (el.getAttribute("title") || "")).toLowerCase(); } catch (e) { }
      try { cls = (el.className && el.className.toString ? el.className.toString() : "").toLowerCase(); } catch (e) { }
      if (t === "x" || t === "\u00d7" || t === "\u2715" || t === "\u2716" || t === "\u2573") return true;
      if (/^(close|skip|dismiss|no thanks|not now)\b/.test(t) && t.length <= 14) return true;
      if (/\b(close|dismiss|skip)\b/.test(aria)) return true;
      if (/(^|[\s_-])(close|dismiss)([\s_-]|$)/.test(cls)) return true;
      return false;
    } catch (e) { return false; }
  }

  function findClose(root) {
    try {
      var list = root.querySelectorAll("button,[role=button],a,span,div,svg,i,[aria-label],[title],[class*=close],[class*=Close]");
      for (var i = 0; i < list.length; i++) {
        var el = list[i];
        if (!el.isConnected) continue;
        var r = el.getBoundingClientRect();
        if (r.width < 10 || r.height < 10) continue;
        if (isCloseControl(el)) return el;
      }
    } catch (e) { }
    return null;
  }

  // Press the cover's own close button.
  function press(el) {
    try {
      var opts = { bubbles: true, cancelable: true, view: window };
      el.dispatchEvent(new MouseEvent("mousedown", opts));
      el.dispatchEvent(new MouseEvent("mouseup", opts));
      el.click();
    } catch (e) { }
  }

  // Ad frames that should never be allowed to load at all.
  var AD_FRAME = /(doubleclick|googlesyndication|googleadservices|adsterra|propellerads|popads|popcash|popunder|magsrv|realsrv|onclickads|clickadu|hilltopads|exoclick|juicyads|trafficjunky|adcash|monetag|profitableratecpm|highperformanceformat|clickaine|adsterra|adskeeper|bidvertiser|zeropark|trafficstars|plugrush|adtrue|clkmon|acscdn)/i;

  // A frame nested inside the page: this is where movie players live.
  function isNestedFrame() {
    try {
      // The site's own page is never treated as a player frame.
      if (location.hostname.indexOf("netlify.app") >= 0) return false;
      if (window.top === window) return false;
      return true;
    } catch (e) {
      // Cross-origin access to window.top throws in some browsers; a page that
      // cannot even look at its parent is by definition not the top page.
      return true;
    }
  }

  function sweep() {
    lastSweep = Date.now();
    try {
      // 1) Take out any large floating cover that talks like an advert.
      var all = document.querySelectorAll("body *");
      for (var i = 0; i < all.length; i++) {
        var el = all[i];
        if (!el.isConnected) continue;
        if (el.children && el.children.length > 40) continue;
        if (!isOverlay(el)) continue;
        if (!matches(AD_TEXT, textOf(el))) continue;

        // Climb to the outermost covering layer, stopping before the player.
        var target = el, up = el;
        for (var k = 0; k < 8; k++) {
          up = up.parentElement;
          if (!up || up === document.body) break;
          if (hasVideo(up)) break;
          if (isOverlay(up)) target = up;
        }
        remove(target);
      }

      // 1b) A QR advert is often drawn as a picture or a canvas, which has no
      //     text to read. A big, square, floating one with no video in it is
      //     the signature of a QR cover. A player control is never
      //     square-and-huge like that, and a frame with a video is left alone.
      if (videoCount() === 0) {
        var squares = document.querySelectorAll("canvas, img, svg");
        for (var q = 0; q < squares.length; q++) {
          var cv = squares[q];
          var cw = cv.offsetWidth || cv.width, ch = cv.offsetHeight || cv.height;
          if (cw < 120 || ch < 120) continue;
          if (Math.abs(cw - ch) > cw * 0.15) continue;   // QR codes are square

          // Only remove one inside a real floating overlay. A square picture
          // drawn in a normal card (a poster, a logo) is left alone.
          var holder = cv, box = null;
          for (var r = 0; r < 8; r++) {
            holder = holder.parentElement;
            if (!holder || holder === document.body) break;
            if (hasVideo(holder)) { box = null; break; }
            if (isOverlay(holder)) box = holder;
          }
          if (box) remove(box);
        }
      }

      // 1c) A cover over the movie that counts down before it shows its close
      //     (X) button. It leaves after a few seconds on its own, but it hides
      //     the movie until then - so press its close button at once and take
      //     the layer off. Only a big floating cover, only one that counts
      //     down, admits to being an advert, or shows a QR - and never over a
      //     video, and never on an account page.
      for (var c = 0; c < all.length; c++) {
        var ov = all[c];
        if (!ov.isConnected) continue;
        if (!isCover(ov)) continue;
        var ot = textOf(ov);
        if (ot.length > 500) continue;                        // a real page, not a cover
        if (REAL_PAGE.test(ot)) continue;                     // login / account page
        var closerNow = findClose(ov);
        var shortCover = ot.length < 220;

        if (hasVideo(ov)) continue;

        if (!(COUNTDOWN.test(ot) || matches(AD_TEXT, ot) || bigSquare(ov)
              || (shortCover && closerNow)
              || (shortCover && COUNTDOWN_LOOSE.test(ot)))) continue;
        if (closerNow) press(closerNow);
        remove(ov);
      }

      // 2) If this whole frame is nothing but the advert, clear it out. Only
      //    the strong wording counts here, so a player frame waiting to load
      //    its video is never emptied by mistake.
      //    The site's own page is never cleared, and a frame with a button,
      //    link, field or form in it is a real page, not an advert cover.
      if (videoCount() === 0 && document.body
          && window.top !== window
          && !document.querySelector("input, textarea, select, button, a[href], form")) {
        var frameText = textOf(document.body);
        if (frameText.length > 0 && frameText.length < 400 && matches(STRONG_AD_TEXT, frameText)) {
          clearFrame();
        }
      }

      // 2b) The "confirm you are not a robot" wall. This one is a whole page
      //     inside the player's frame, showing a QR and a button - so step 1
      //     cannot see it (it is not floating) and step 2 skips it (it holds a
      //     button). It is cleared here instead, but only ever inside a
      //     player's frame, and never on a real account page.
      if (videoCount() === 0 && document.body && isNestedFrame()) {
        var wallText = textOf(document.body);
        if (wallText.length > 0 && wallText.length < 300
            && matches(STRONG_AD_TEXT, wallText)
            && !REAL_PAGE.test(wallText)) {

          // Some of these gates let the movie through as soon as any click
          // happens; a tap costs nothing and often skips the gate outright.
          try {
            var tap = new MouseEvent("mousedown", { bubbles: true, cancelable: true, view: window });
            document.dispatchEvent(tap);
            window.dispatchEvent(tap);
          } catch (e) { }

          // Press the wall's own close control first - usually a bare X in the
          // corner - so the player itself records that the gate was dismissed.
          var wallCloser = findClose(document.body);
          if (wallCloser) press(wallCloser);

          clearFrame();

          // These gates record in the frame that they have been shown, so one
          // reload after clearing usually loads the movie itself. Done at most
          // once per frame, so a gate that comes back cannot cause a loop.
          try {
            if (!sessionStorage.getItem("__un0tvWall")) {
              sessionStorage.setItem("__un0tvWall", "1");
              setTimeout(function () { try { location.reload(); } catch (e) { } }, 1600);
            }
          } catch (e) { }
        }
      }

      // 3) Drop advert frames by address.
      var frames = document.querySelectorAll("iframe");
      for (var n = 0; n < frames.length; n++) {
        var f = frames[n];
        var src = (f.src || "").toLowerCase();
        if (src && AD_FRAME.test(src)) {
          try { f.remove(); window.__un0tvKilled++; } catch (e) { }
        }
      }

      unlockScroll();
    } catch (e) { }
  }

  function start() {
    sweep();
    // The player builds its cover after the page loads, so keep looking -
    // but at most once every 700ms, no matter how busy the page is.
    try {
      setInterval(function () {
        if (Date.now() - lastSweep > 700) sweep();
      }, 700);
    } catch (e) { }

    // Page changes also trigger a sweep, throttled so a busy video player
    // (which changes its page many times a second) cannot cause stutter.
    try {
      new MutationObserver(function () {
        if (Date.now() - lastSweep > 700) sweep();
      }).observe(document.documentElement, { childList: true, subtree: true });
    } catch (e) { }
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", start, { once: true });
  } else {
    start();
  }
})();
""";
}
