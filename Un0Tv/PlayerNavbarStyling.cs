namespace Un0Tv;

/// <summary>
/// Styles the media player navigation bar with 30% opacity white color
/// and arranges controls in the order: Home, Beginning, Back 10s, Play/Pause, Forward 10s
/// </summary>
internal static class PlayerNavbarStyling
{
    public static readonly string Js = """
(function () {
  // Inject CSS styling for the media player navigation bar
  if (window.__un0tvPlayerNavbar) return;
  window.__un0tvPlayerNavbar = true;

  // The player is a VidFast frame, and its own bar lives inside that frame.
  // The style is scoped to the frame's body so the site's own pages keep
  // their own buttons and their own look.
  var PLAYER_HOST = /(^|\.)vidfast\.[a-z]+$/i;

  function inPlayerFrame() {
    try {
      return PLAYER_HOST.test(location.hostname);
    } catch (e) { return false; }
  }

  // On the page the viewer sees the player is only a frame, so there is no
  // player bar here to paint: the styling belongs to the frame itself.
  if (!inPlayerFrame()) return;

  // This script runs at document start, so the page may not have a head yet.
  // The style is added as soon as one exists; until then it waits.
  function addStyle() {
    try {
      var style = document.getElementById('__un0tvPlayerNavbarStyle');
      if (!style) {
        style = document.createElement('style');
        style.id = '__un0tvPlayerNavbarStyle';
        style.textContent = PLAYER_NAVBAR_CSS;
      }
      (document.head || document.documentElement).appendChild(style);
      return true;
    } catch (e) { return false; }
  }

  // Player navbar styling with 30% opacity white color
  const PLAYER_NAVBAR_CSS = `
    /* Media Player Navigation Bar - 30% Opacity White */

    /* Main control bar container - 30% opacity white */
    .media-player-controls,
    .player-controls-container,
    .video-controls,
    [class*="player"][class*="controls"],
    [class*="video"][class*="controls"],
    .controls-wrapper,
    .player-bar,
    .playback-controls,
    *[role="toolbar"][class*="player"],
    *[role="toolbar"][class*="video"] {
      background-color: rgba(255, 255, 255, 0.3) !important;
      opacity: 1 !important;
      backdrop-filter: blur(5px) !important;
      display: flex !important;
      flex-direction: row !important;
      justify-content: center !important;
      align-items: center !important;
      gap: 8px !important;
      padding: 12px 16px !important;
      border-radius: 4px !important;
    }

    /* Control buttons - white with transparent background */
    .media-player-controls button,
    .player-controls-container button,
    .video-controls button,
    [class*="player"][class*="controls"] button,
    [class*="video"][class*="controls"] button,
    .controls-wrapper button,
    .player-bar button,
    .playback-controls button,
    *[role="toolbar"][class*="player"] button,
    *[role="toolbar"][class*="video"] button,
    .player-button,
    .control-button,
    [class*="play-button"],
    [class*="pause-button"],
    [class*="forward-button"],
    [class*="backward-button"],
    [class*="skip-button"],
    [class*="seekback"],
    [class*="seekforward"] {
      background-color: transparent !important;
      background: transparent !important;
      color: white !important;
      border: 1px solid rgba(255, 255, 255, 0.6) !important;
      border-radius: 4px !important;
      padding: 8px 12px !important;
      margin: 0 4px !important;
      transition: all 0.2s ease !important;
      opacity: 1 !important;
      cursor: pointer !important;
      font-size: 14px !important;
      font-weight: 500 !important;
    }

    /* Button hover state */
    .media-player-controls button:hover,
    .player-controls-container button:hover,
    .video-controls button:hover,
    [class*="player"][class*="controls"] button:hover,
    [class*="video"][class*="controls"] button:hover,
    .controls-wrapper button:hover,
    .player-bar button:hover,
    .playback-controls button:hover,
    *[role="toolbar"][class*="player"] button:hover,
    *[role="toolbar"][class*="video"] button:hover {
      background-color: rgba(255, 255, 255, 0.2) !important;
      border-color: rgba(255, 255, 255, 0.9) !important;
      box-shadow: 0 0 8px rgba(255, 255, 255, 0.4) !important;
    }

    /* Button active/pressed state */
    .media-player-controls button:active,
    .media-player-controls button.active,
    .media-player-controls button[aria-pressed="true"],
    .player-controls-container button:active,
    .player-controls-container button.active,
    .video-controls button:active,
    .video-controls button.active,
    [class*="player"][class*="controls"] button:active,
    [class*="player"][class*="controls"] button.active,
    [class*="video"][class*="controls"] button:active,
    [class*="video"][class*="controls"] button.active,
    .controls-wrapper button:active,
    .controls-wrapper button.active,
    .player-bar button:active,
    .player-bar button.active,
    .playback-controls button:active,
    .playback-controls button.active {
      background-color: rgba(255, 255, 255, 0.35) !important;
      border-color: rgba(255, 255, 255, 1) !important;
      box-shadow: 0 0 12px rgba(255, 255, 255, 0.6) !important;
      transform: scale(0.98) !important;
    }

    /* Button text and icons - white */
    .media-player-controls button *,
    .player-controls-container button *,
    .video-controls button *,
    [class*="player"][class*="controls"] button *,
    [class*="video"][class*="controls"] button *,
    .controls-wrapper button *,
    .player-bar button *,
    .playback-controls button * {
      color: white !important;
      fill: white !important;
      stroke: white !important;
    }

    /* SVG icons - white */
    .media-player-controls svg,
    .player-controls-container svg,
    .video-controls svg,
    [class*="player"][class*="controls"] svg,
    [class*="video"][class*="controls"] svg,
    .controls-wrapper svg,
    .player-bar svg,
    .playback-controls svg {
      color: white !important;
      fill: white !important;
      stroke: white !important;
    }

    /* Button ordering - arrange in: Home, Beginning, Back 10s, Play/Pause, Forward 10s */

    /* Home button - Order 1 */
    button[aria-label*="home" i],
    button[title*="home" i],
    button[class*="home"],
    *[aria-label*="home" i] button,
    *[title*="home" i] button {
      order: 1 !important;
    }

    /* Beginning/Start button - Order 2 */
    button[aria-label*="beginning" i],
    button[aria-label*="start" i],
    button[aria-label*="skip to beginning" i],
    button[title*="beginning" i],
    button[title*="start" i],
    button[class*="beginning"],
    button[class*="start-button"],
    *[aria-label*="beginning" i] button,
    *[aria-label*="skip to beginning" i] button {
      order: 2 !important;
    }

    /* Back 10 seconds button - Order 3 */
    button[aria-label*="back" i],
    button[aria-label*="rewind" i],
    button[aria-label*="backward" i],
    button[aria-label*="-10" i],
    button[aria-label*="10 second" i],
    button[title*="back" i],
    button[title*="rewind" i],
    button[title*="-10" i],
    button[class*="back"],
    button[class*="rewind"],
    button[class*="backward"],
    button[class*="skip-back"],
    button[class*="seek-back"],
    *[aria-label*="back 10" i] button,
    *[aria-label*="rewind 10" i] button {
      order: 3 !important;
    }

    /* Play/Pause button - Order 4 (Center) */
    button[aria-label*="play" i],
    button[aria-label*="pause" i],
    button[aria-label*="toggle play" i],
    button[title*="play" i],
    button[title*="pause" i],
    button[class*="play"],
    button[class*="pause"],
    button[class*="toggle-play"],
    button[class*="play-pause"],
    *[aria-label*="play" i] button,
    *[aria-label*="pause" i] button {
      order: 4 !important;
      padding: 10px 16px !important;
      font-weight: 600 !important;
      border-width: 2px !important;
    }

    /* Forward 10 seconds button - Order 5 */
    button[aria-label*="forward" i],
    button[aria-label*="fast forward" i],
    button[aria-label*="+10" i],
    button[aria-label*="10 second forward" i],
    button[title*="forward" i],
    button[title*="fast forward" i],
    button[title*="+10" i],
    button[class*="forward"],
    button[class*="fast-forward"],
    button[class*="skip-forward"],
    button[class*="seek-forward"],
    *[aria-label*="forward 10" i] button,
    *[aria-label*="skip forward" i] button {
      order: 5 !important;
    }

    /* Volume control - Order 6 */
    button[aria-label*="volume" i],
    button[aria-label*="mute" i],
    button[title*="volume" i],
    button[title*="mute" i],
    button[class*="volume"],
    button[class*="mute"] {
      order: 6 !important;
    }

    /* Fullscreen button - Order 7 */
    button[aria-label*="fullscreen" i],
    button[aria-label*="full screen" i],
    button[title*="fullscreen" i],
    button[class*="fullscreen"] {
      order: 7 !important;
    }

    /* Additional control styling */
    .media-player-controls input[type="range"],
    .player-controls-container input[type="range"],
    .video-controls input[type="range"] {
      accent-color: white !important;
      opacity: 1 !important;
    }

    /* Track styling for range inputs */
    .media-player-controls input[type="range"]::-webkit-slider-track,
    .player-controls-container input[type="range"]::-webkit-slider-track {
      background: rgba(255, 255, 255, 0.2) !important;
      border-radius: 2px !important;
    }

    .media-player-controls input[type="range"]::-webkit-slider-thumb,
    .player-controls-container input[type="range"]::-webkit-slider-thumb {
      background: white !important;
      border-color: white !important;
      border-radius: 50% !important;
    }

    /* Time display styling */
    .media-player-controls span,
    .media-player-controls label,
    .player-controls-container span,
    .player-controls-container label,
    .video-controls span,
    .video-controls label,
    [class*="time-display"],
    [class*="current-time"],
    [class*="duration"] {
      color: white !important;
      opacity: 1 !important;
      font-size: 12px !important;
    }

    /* Disabled button state */
    .media-player-controls button:disabled,
    .player-controls-container button:disabled,
    .video-controls button:disabled,
    [class*="player"][class*="controls"] button:disabled,
    [class*="video"][class*="controls"] button:disabled {
      opacity: 0.5 !important;
      cursor: not-allowed !important;
      color: rgba(255, 255, 255, 0.6) !important;
      border-color: rgba(255, 255, 255, 0.3) !important;
    }

    /* Focus state for keyboard/remote navigation */
    .media-player-controls button:focus,
    .media-player-controls button:focus-visible,
    .player-controls-container button:focus,
    .player-controls-container button:focus-visible,
    .video-controls button:focus,
    .video-controls button:focus-visible,
    [class*="player"][class*="controls"] button:focus,
    [class*="player"][class*="controls"] button:focus-visible {
      outline: 2px solid cyan !important;
      outline-offset: 2px !important;
      box-shadow: 0 0 16px rgba(0, 255, 255, 0.8) !important;
    }

    /* TV remote focus - cyan highlight */
    button:focus-visible[class*="control"],
    button:focus-visible[class*="play"],
    button:focus-visible[aria-label*="play" i],
    button:focus-visible[aria-label*="pause" i] {
      outline: 3px solid cyan !important;
      outline-offset: 3px !important;
      box-shadow: 0 0 20px rgba(0, 255, 255, 1) !important;
      transform: scale(1.08) !important;
    }
  `;

  // Create and inject the style element
  try {
    addStyle();
    console.log('[PlayerNavbar] Media player navbar styling applied - 30% opacity white');
  } catch (error) {
    console.error('[PlayerNavbar] Error injecting CSS:', error);
  }

  // At document start the page may not have a head yet, so the style is put
  // in as soon as the page is ready.
  try {
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', addStyle, { once: true });
    }
  } catch (e) { }

  // Re-inject if page dynamically loads new player controls
  const observer = new MutationObserver(() => {
    var navbarCSS = document.getElementById('__un0tvPlayerNavbarStyle');
    if (!navbarCSS && document.querySelector('[class*="player"][class*="controls"], .video-controls')) {
      addStyle();
    }
  });

  try {
    if (document.head) {
      observer.observe(document.head, { childList: true });
    }
    if (document.body) {
      observer.observe(document.body, { childList: true, subtree: true });
    }
  } catch (e) { }
})();
""";
}
