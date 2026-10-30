namespace Un0Tv;

// The movie player runs a "popup capability check": on the first click it calls
// window.open('','_blank','width=1,height=1') and looks at the returned handle.
// If the handle is null it decides the page is sandboxed and stops the movie.
//
// The app answers that call by opening a real window - and the player's
// monetisation, and the pop-under networks riding on it, use the very same call
// to open their own advert windows. Those windows were being shown on the TV as
// a full white page with a QR code, worded "confirm you are not a robot".
//
// This script takes over window.open inside EVERY frame before the page's own
// code runs:
//   - a blank probe is answered with a harmless stand-in, so the check passes
//     and nothing appears on screen,
//   - a sign-in window (Google / Firebase / the site) is left alone,
//   - anything else is answered with the same stand-in, so no advert window is
//     ever created.
//
// The stand-in is a plain object shaped like a window (blur, focus, close,
// closed, location), which is all the player's check ever touches.
internal static class PopupGuard
{
    public static readonly string Js = """
(function () {
  if (window.__un0tvOpen) return;
  window.__un0tvOpen = 1;

  var realOpen = window.open;

  // Windows the viewer may legitimately need: the site itself, and the Google /
  // Firebase sign-in that "Continue with Google" opens.
  var ALLOW = [
    /(^|\.)google\.com$/i,
    /(^|\.)googleusercontent\.com$/i,
    /(^|\.)gstatic\.com$/i,
    /(^|\.)firebaseapp\.com$/i,
    /(^|\.)firebaseio\.com$/i,
    /(^|\.)youtube\.com$/i
  ];

  function allowed(host) {
    if (!host) return false;
    for (var i = 0; i < ALLOW.length; i++) {
      if (ALLOW[i].test(host)) return true;
    }
    return false;
  }

  // A window-shaped object that does nothing. Enough for the player's check,
  // which only reads it and calls blur/focus/close.
  function standIn() {
    var noop = function () { };
    var w = {
      closed: true,
      closed_placeholder: true,
      focus: noop,
      blur: noop,
      close: noop,
      postMessage: noop,
      moveTo: noop,
      resizeTo: noop,
      print: noop,
      location: { href: "about:blank", replace: noop, assign: noop, reload: noop }
    };
    w.window = w;
    w.self = w;
    w.top = w;
    w.parent = w;
    w.document = { write: noop, writeln: noop, open: noop, close: noop, body: null };
    return w;
  }

  window.open = function (url, name, features) {
    try {
      var u = (url === null || url === undefined) ? "" : String(url).trim();

      // The capability probe: a blank window asked for at 1x1 pixels.
      if (!u || u === "about:blank") return standIn();

      var host = "";
      try {
        host = new URL(u, location.href).hostname;
      } catch (e) {
        host = "";
      }

      // The site's own windows - a window opened at the same host as the page
      // that asked for it - are shown for real, as is the sign-in window.
      var sameHost = false;
      try { sameHost = !!host && host.toLowerCase() === (location.hostname || "").toLowerCase(); } catch (e) { sameHost = false; }

      if (allowed(host) || sameHost) return realOpen.apply(window, arguments);

      // Everything else is a pop-up advert. Answer it, show nothing.
      return standIn();
    } catch (e) {
      return standIn();
    }
  };
})();
""";
}
