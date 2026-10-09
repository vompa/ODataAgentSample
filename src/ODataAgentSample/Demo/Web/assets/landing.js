(function () {
  'use strict';

  var form = document.getElementById('login-form');
  var input = document.getElementById('code');
  var btn = document.getElementById('login-btn');
  var statusEl = document.getElementById('login-status');
  var already = document.getElementById('already');

  function setStatus(text, isError) {
    statusEl.textContent = text;
    statusEl.className = 'status ' + (isError ? 'err' : 'info');
  }

  fetch('/api/demo/status', { credentials: 'same-origin' })
    .then(function (r) { return r.ok ? r.json() : null; })
    .then(function (s) {
      if (s && s.authenticated) {
        form.classList.add('hidden');
        already.classList.remove('hidden');
      }
    })
    .catch(function () { /* Status ist optional */ });

  form.addEventListener('submit', function (ev) {
    ev.preventDefault();
    var code = input.value.trim();
    if (!code) {
      setStatus('Bitte gib den Zugangscode ein.', true);
      input.focus();
      return;
    }
    btn.disabled = true;
    setStatus('Prüfe Zugangscode …', false);
    fetch('/api/demo/login', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'X-Demo': '1' },
      body: JSON.stringify({ code: code })
    }).then(function (r) {
      if (r.ok) {
        setStatus('Angemeldet. Weiterleitung …', false);
        window.location.href = '/demo';
        return null;
      }
      return r.json().catch(function () { return {}; }).then(function (data) {
        if (r.status === 429) {
          var min = Math.max(1, Math.ceil((data.retryAfterSeconds || 60) / 60));
          setStatus('Zu viele Versuche. Bitte warte etwa ' + min + (min === 1 ? ' Minute.' : ' Minuten.'), true);
        } else if (r.status === 401) {
          setStatus(data.error || 'Der Zugangscode ist nicht korrekt.', true);
        } else {
          setStatus(data.error || 'Anmeldung fehlgeschlagen (Status ' + r.status + ').', true);
        }
        btn.disabled = false;
        input.select();
      });
    }).catch(function () {
      setStatus('Der Server ist nicht erreichbar. Bitte später erneut versuchen.', true);
      btn.disabled = false;
    });
  });
})();
