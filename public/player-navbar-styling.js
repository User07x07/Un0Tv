/**
 * Media Player Navigation Bar Styling
 * 30% Opacity White Navigation Bar
 * 
 * Arranges controls in order: Home, Beginning, Back 10s, Play/Pause, Forward 10s
 */

// Add this to DpadNav.cs or inject as CSS into the page

const PLAYER_NAV_CSS = `
/* Media Player Navigation Bar - 30% Opacity White */

/* Main control bar container */
.vidfast-controls,
.player-controls,
[class*="player"][class*="control"],
[class*="media"][class*="control"],
.player-navbar,
.controls-bar,
.media-controls {
  background-color: rgba(255, 255, 255, 0.3) !important;
  opacity: 0.7 !important;
  backdrop-filter: blur(5px);
}

/* Individual control buttons */
.vidfast-controls button,
.player-controls button,
[class*="player"][class*="control"] button,
[class*="media"][class*="control"] button,
.player-navbar button,
.controls-bar button,
.media-controls button {
  background-color: transparent !important;
  color: white !important;
  border: 1px solid rgba(255, 255, 255, 0.5) !important;
  border-radius: 4px !important;
  padding: 8px 12px !important;
  margin: 0 4px !important;
  transition: all 0.2s ease !important;
  opacity: 1 !important;
}

/* Hover state */
.vidfast-controls button:hover,
.player-controls button:hover,
[class*="player"][class*="control"] button:hover,
[class*="media"][class*="control"] button:hover,
.player-navbar button:hover,
.controls-bar button:hover,
.media-controls button:hover {
  background-color: rgba(255, 255, 255, 0.2) !important;
  border-color: rgba(255, 255, 255, 0.8) !important;
}

/* Active/pressed state */
.vidfast-controls button:active,
.vidfast-controls button.active,
.player-controls button:active,
.player-controls button.active,
[class*="player"][class*="control"] button:active,
[class*="player"][class*="control"] button.active,
[class*="media"][class*="control"] button:active,
[class*="media"][class*="control"] button.active,
.player-navbar button:active,
.player-navbar button.active,
.controls-bar button:active,
.controls-bar button.active,
.media-controls button:active,
.media-controls button.active {
  background-color: rgba(255, 255, 255, 0.4) !important;
  border-color: rgba(255, 255, 255, 1) !important;
  box-shadow: 0 0 8px rgba(255, 255, 255, 0.6) !important;
}

/* Text/Icon color in buttons */
.vidfast-controls button *,
.player-controls button *,
[class*="player"][class*="control"] button *,
[class*="media"][class*="control"] button *,
.player-navbar button *,
.controls-bar button *,
.media-controls button * {
  color: white !important;
  fill: white !important;
}

/* SVG icons */
.vidfast-controls svg,
.player-controls svg,
[class*="player"][class*="control"] svg,
[class*="media"][class*="control"] svg,
.player-navbar svg,
.controls-bar svg,
.media-controls svg {
  color: white !important;
  fill: white !important;
  stroke: white !important;
}

/* Control order arrangement using flexbox */
.vidfast-controls,
.player-controls,
[class*="player"][class*="control"],
[class*="media"][class*="control"],
.player-navbar,
.controls-bar,
.media-controls {
  display: flex !important;
  flex-direction: row !important;
  justify-content: center !important;
  align-items: center !important;
  gap: 8px !important;
  padding: 12px !important;
}

/* Specific button ordering using CSS order property */
/* Home button - First */
.vidfast-controls button:nth-child(1),
.player-controls button:nth-child(1),
[class*="player"][class*="control"] button:nth-child(1),
[class*="media"][class*="control"] button:nth-child(1),
.player-navbar button:nth-child(1),
.controls-bar button:nth-child(1),
.media-controls button:nth-child(1),
[class*="home"],
[aria-label*="home"],
[title*="home"] {
  order: 1 !important;
}

/* Beginning/Start button - Second */
[class*="beginning"],
[class*="start"],
[class*="rewind"],
[aria-label*="beginning"],
[aria-label*="start"],
[title*="beginning"],
[title*="start"] {
  order: 2 !important;
}

/* Back 10s button - Third */
[class*="back"],
[class*="rewind"],
[aria-label*="back"],
[aria-label*="rewind"],
[title*="back"],
[title*="backward"],
[title*="-10"] {
  order: 3 !important;
}

/* Play/Pause button - Middle/Center */
[class*="play"],
[class*="pause"],
[aria-label*="play"],
[aria-label*="pause"],
[title*="play"],
[title*="pause"] {
  order: 4 !important;
  padding: 10px 16px !important;
}

/* Forward 10s button - Fourth */
[class*="forward"],
[class*="progress"],
[aria-label*="forward"],
[aria-label*="forward"],
[title*="forward"],
[title*="+10"] {
  order: 5 !important;
}

/* Disabled state */
.vidfast-controls button:disabled,
.player-controls button:disabled,
[class*="player"][class*="control"] button:disabled,
[class*="media"][class*="control"] button:disabled,
.player-navbar button:disabled,
.controls-bar button:disabled,
.media-controls button:disabled {
  opacity: 0.5 !important;
  cursor: not-allowed !important;
  color: rgba(255, 255, 255, 0.6) !important;
}

/* Focus state for keyboard navigation */
.vidfast-controls button:focus,
.player-controls button:focus,
[class*="player"][class*="control"] button:focus,
[class*="media"][class*="control"] button:focus,
.player-navbar button:focus,
.controls-bar button:focus,
.media-controls button:focus {
  outline: 2px solid rgba(255, 255, 255, 0.8) !important;
  outline-offset: 2px !important;
  box-shadow: 0 0 12px rgba(255, 255, 255, 0.8) !important;
}

/* Additional styling for time display and other elements */
.vidfast-controls span,
.vidfast-controls label,
.player-controls span,
.player-controls label,
[class*="player"][class*="control"] span,
[class*="player"][class*="control"] label,
[class*="media"][class*="control"] span,
[class*="media"][class*="control"] label,
.player-navbar span,
.player-navbar label,
.controls-bar span,
.controls-bar label,
.media-controls span,
.media-controls label {
  color: white !important;
  opacity: 1 !important;
}

/* Progress bar styling */
.vidfast-controls input[type="range"],
.player-controls input[type="range"],
[class*="player"][class*="control"] input[type="range"],
[class*="media"][class*="control"] input[type="range"],
.player-navbar input[type="range"],
.controls-bar input[type="range"],
.media-controls input[type="range"] {
  accent-color: white !important;
}

/* Track styling */
.vidfast-controls input[type="range"]::-webkit-slider-track,
.player-controls input[type="range"]::-webkit-slider-track {
  background: rgba(255, 255, 255, 0.2) !important;
}

.vidfast-controls input[type="range"]::-webkit-slider-thumb,
.player-controls input[type="range"]::-webkit-slider-thumb {
  background: white !important;
  border-color: white !important;
}

/* Volume control styling */
[class*="volume"],
[aria-label*="volume"],
[title*="volume"] {
  color: white !important;
  border-color: rgba(255, 255, 255, 0.5) !important;
}

/* Fullscreen button styling */
[class*="fullscreen"],
[aria-label*="fullscreen"],
[title*="fullscreen"] {
  order: 6 !important;
}

/* Responsive adjustments */
@media (max-width: 1920px) {
  .vidfast-controls,
  .player-controls,
  [class*="player"][class*="control"],
  [class*="media"][class*="control"],
  .player-navbar,
  .controls-bar,
  .media-controls {
    padding: 8px !important;
    gap: 6px !important;
  }

  .vidfast-controls button,
  .player-controls button,
  [class*="player"][class*="control"] button,
  [class*="media"][class*="control"] button,
  .player-navbar button,
  .controls-bar button,
  .media-controls button {
    padding: 6px 10px !important;
    margin: 0 2px !important;
  }
}

/* TV Remote Focus Styling */
.vidfast-controls button:focus-visible,
.player-controls button:focus-visible,
[class*="player"][class*="control"] button:focus-visible,
[class*="media"][class*="control"] button:focus-visible,
.player-navbar button:focus-visible,
.controls-bar button:focus-visible,
.media-controls button:focus-visible {
  outline: 3px solid cyan !important;
  outline-offset: 3px !important;
  transform: scale(1.05) !important;
  box-shadow: 0 0 16px rgba(0, 255, 255, 0.8) !important;
}
`;

// Function to inject the CSS into the page
function injectPlayerNavbarCSS() {
  try {
    const style = document.createElement('style');
    style.textContent = PLAYER_NAV_CSS;
    document.head.appendChild(style);
    console.log('[PlayerNav] Applied white 30% opacity navbar styling');
  } catch (error) {
    console.error('[PlayerNav] Error injecting CSS:', error);
  }
}

// Inject when DOM is ready
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', injectPlayerNavbarCSS);
} else {
  injectPlayerNavbarCSS();
}

// Re-inject if page dynamically loads new player
const observer = new MutationObserver(() => {
  if (document.querySelector('.vidfast-controls, .player-controls')) {
    injectPlayerNavbarCSS();
  }
});

observer.observe(document.body, { childList: true, subtree: true });
