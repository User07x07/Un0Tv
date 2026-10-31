namespace Un0Tv;

// Typing into the site's search box from the app's own on-screen keyboard.
//
// A TV has no touch screen, so pressing a search box with the remote never
// brings up Android's keyboard. This script remembers which box the viewer is
// on and accepts letters from the app, typed into that box the same way a
// person typing would fill it - including telling the site that the text
// changed, which is what makes its search react.
public static class FieldInput
{
    public static readonly string Js = """
(function () {
  if (window.top !== window) return;
  if (window.__un0tvField) return;

  var active = null;

  function editable(el) {
    if (!el) return false;
    var t = (el.tagName || '').toLowerCase();
    if (t === 'textarea') return true;
    if (t === 'input') {
      var k = (el.type || 'text').toLowerCase();
      return k !== 'hidden' && k !== 'checkbox' && k !== 'radio'
          && k !== 'submit' && k !== 'button' && k !== 'file';
    }
    return !!el.isContentEditable;
  }

  function report() {
    try {
      if (window.Un0TvNative && window.Un0TvNative.fieldFocused)
        window.Un0TvNative.fieldFocused();
    } catch (e) { }
  }

  document.addEventListener('focusin', function (e) {
    if (editable(e.target)) { active = e.target; report(); }
  }, true);

  document.addEventListener('focusout', function (e) {
    if (e.target !== active) return;
    setTimeout(function () {
      if (document.activeElement !== active) active = null;
    }, 300);
  }, true);

  // Filling a box by setting .value alone is ignored by sites built with
  // React, which watch the real input event. The native setter plus the event
  // is what a person typing produces.
  function setValue(el, text) {
    var proto = el.tagName === 'TEXTAREA'
      ? HTMLTextAreaElement.prototype
      : HTMLInputElement.prototype;
    try {
      Object.getOwnPropertyDescriptor(proto, 'value').set.call(el, text);
    } catch (e) { el.value = text; }
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
  }

  function target() {
    if (editable(active)) return active;
    if (editable(document.activeElement)) { active = document.activeElement; return active; }
    return null;
  }

  function insert(text) {
    var el = target();
    if (!el) return false;
    if (el.isContentEditable) {
      try { document.execCommand('insertText', false, text); }
      catch (e) { el.textContent = (el.textContent || '') + text; }
      el.dispatchEvent(new Event('input', { bubbles: true }));
      return true;
    }
    var v = el.value || '';
    var start = el.selectionStart == null ? v.length : el.selectionStart;
    var end = el.selectionEnd == null ? v.length : el.selectionEnd;
    setValue(el, v.slice(0, start) + text + v.slice(end));
    try { el.selectionStart = el.selectionEnd = start + text.length; } catch (e) { }
    return true;
  }

  function backspace() {
    var el = target();
    if (!el) return false;
    if (el.isContentEditable) {
      try { document.execCommand('delete'); } catch (e) { }
      return true;
    }
    var v = el.value || '';
    var start = el.selectionStart == null ? v.length : el.selectionStart;
    var end = el.selectionEnd == null ? v.length : el.selectionEnd;
    if (start === end && start > 0) start -= 1;
    setValue(el, v.slice(0, start) + v.slice(end));
    try { el.selectionStart = el.selectionEnd = start; } catch (e) { }
    return true;
  }

  function clear() {
    var el = target();
    if (!el) return false;
    if (el.isContentEditable) el.textContent = '';
    else setValue(el, '');
    return true;
  }

  function submit() {
    var el = target();
    if (!el) return false;
    try {
      var o = { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true, cancelable: true };
      el.dispatchEvent(new KeyboardEvent('keydown', o));
      el.dispatchEvent(new KeyboardEvent('keypress', o));
      el.dispatchEvent(new KeyboardEvent('keyup', o));
    } catch (e) { }
    try { if (el.form && el.form.requestSubmit) el.form.requestSubmit(); } catch (e) { }
    return true;
  }

  window.__un0tvField = {
    insert: function (t) { try { return insert(t); } catch (e) { return false; } },
    backspace: function () { try { return backspace(); } catch (e) { return false; } },
    clear: function () { try { return clear(); } catch (e) { return false; } },
    submit: function () { try { return submit(); } catch (e) { return false; } },
    blur: function () { try { if (active) active.blur(); } catch (e) { } }
  };
})();
""";
}
