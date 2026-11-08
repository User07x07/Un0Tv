namespace Un0Tv;

// Tells the app which page of the site is on screen.
//
// The site is one page that swaps its content as the viewer moves around
// (React's history navigation), so the browser's own "page finished" event
// fires only once. Without a report, the app would keep believing a movie
// is still open after the viewer went back to the dashboard - which is why
// the movie command panel used to appear on the dashboard as well.
//
// Every move - a link press, the back button, a full page load - sends the
// address to the app through window.Un0TvNative.route.
public static class PageRoute
{
    public static readonly string Js = """
(function () {
  // Only the page the viewer is looking at reports; the movie player's own
  // frame runs this script too and must be left alone.
  if (window.top !== window) return;
  if (window.__un0tvRoute) return;

  function send() {
    try {
      var p = (location.pathname || '') + (location.search || '');
      if (window.Un0TvNative && window.Un0TvNative.route)
        window.Un0TvNative.route(p);
    } catch (e) { }
  }

  try {
    var push = history.pushState;
    history.pushState = function () {
      var r = push.apply(this, arguments);
      setTimeout(send, 0);
      return r;
    };
    var replace = history.replaceState;
    history.replaceState = function () {
      var r = replace.apply(this, arguments);
      setTimeout(send, 0);
      return r;
    };
  } catch (e) { }

  window.addEventListener('popstate', function () { setTimeout(send, 0); });
  window.addEventListener('hashchange', function () { setTimeout(send, 0); });

  window.__un0tvRoute = send;

  // Straight away, and once more after the site has settled: the first
  // address is what tells the app whether this is a movie page.
  setTimeout(send, 0);
  setTimeout(send, 1200);
})();
""";
}
