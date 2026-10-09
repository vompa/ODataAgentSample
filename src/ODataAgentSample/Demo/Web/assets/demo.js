(function () {
  'use strict';

  var MAX = 300;
  var $ = function (id) { return document.getElementById(id); };
  var messages = $('messages'), trace = $('trace'), traceEmpty = $('trace-empty');
  var prompt = $('prompt'), counter = $('counter'), sendBtn = $('send'), busy = $('busy');
  var composer = $('composer'), chips = $('chips');
  var cards = {};
  var busyTimer = null, slowTimer = null, working = false;

  function toLogin() { window.location.href = '/'; }

  function updateQuota(l) {
    if (!l) return;
    $('q-hour').textContent = 'Stunde: noch ' + l.hourRemaining + ' von ' + l.hourLimit;
    $('q-session').textContent = 'Sitzung: noch ' + l.sessionHourRemaining + ' von ' + l.sessionHourLimit;
    $('q-day').textContent = 'Tag: noch ' + l.dayRemaining + ' von ' + l.dayLimit;
  }

  function addMsg(kind, text) {
    var d = document.createElement('div');
    d.className = 'msg ' + kind;
    d.textContent = text;
    messages.appendChild(d);
    messages.scrollTop = messages.scrollHeight;
    return d;
  }

  function updateCounter() {
    counter.textContent = prompt.value.length + '/' + MAX;
    counter.className = 'counter' + (prompt.value.length >= MAX ? ' full' : '');
  }

  function setWorking(on) {
    working = on;
    sendBtn.disabled = on;
    var cs = chips.querySelectorAll('button');
    for (var i = 0; i < cs.length; i++) cs[i].disabled = on;
    clearTimeout(slowTimer);
    busy.textContent = '';
    if (on) {
      var sp = document.createElement('span');
      sp.className = 'spinner';
      sp.setAttribute('aria-hidden', 'true');
      var tx = document.createElement('span');
      tx.textContent = 'Agent arbeitet …';
      busy.appendChild(sp);
      busy.appendChild(tx);
      slowTimer = setTimeout(function () {
        tx.textContent = 'Agent arbeitet … Der erste Aufruf kann wegen eines Kaltstarts einige Sekunden dauern.';
      }, 5000);
    }
  }

  function resetSlow() {
    // Sobald etwas eintrifft, Hinweistext zurücksetzen
    var spans = busy.querySelectorAll('span');
    if (spans.length > 1) spans[1].textContent = 'Agent arbeitet …';
    clearTimeout(slowTimer);
  }

  function addCard(ev) {
    traceEmpty.classList.add('hidden');
    var c = document.createElement('div');
    c.className = 'tcard run';
    var top = document.createElement('div'); top.className = 'top';
    var name = document.createElement('span'); name.className = 'tname'; name.textContent = ev.name || '?';
    var dur = document.createElement('span'); dur.className = 'dur'; dur.textContent = '';
    top.appendChild(name); top.appendChild(dur);
    var args = document.createElement('div'); args.className = 'targs';
    try { args.textContent = JSON.stringify(ev.input === undefined ? {} : ev.input); } catch (e) { args.textContent = '{}'; }
    var st = document.createElement('span'); st.className = 'tstatus'; st.textContent = '… läuft';
    var sum = document.createElement('div'); sum.className = 'tsummary';
    c.appendChild(top); c.appendChild(args); c.appendChild(st); c.appendChild(sum);
    trace.appendChild(c);
    trace.scrollTop = trace.scrollHeight;
    cards[ev.id] = { el: c, st: st, dur: dur, sum: sum };
  }

  function finishCard(ev) {
    var c = cards[ev.id];
    if (!c) return;
    c.el.className = 'tcard ' + (ev.ok ? 'ok' : 'err');
    c.st.textContent = ev.ok ? '✓ OK' : '✕ Fehler';
    c.sum.textContent = ev.summary || '';
    if (typeof ev.durationMs === 'number') c.dur.textContent = ev.durationMs + ' ms';
  }

  function handleEvent(ev) {
    resetSlow();
    switch (ev.type) {
      case 'tool_call': addCard(ev); break;
      case 'tool_result': finishCard(ev); break;
      case 'text': addMsg('bot', ev.text || ''); break;
      case 'error': addMsg('error', ev.message || 'Es ist ein Fehler aufgetreten.'); break;
      case 'done': updateQuota(ev.limits); break;
    }
  }

  function processBuffer(buf) {
    var idx;
    while ((idx = buf.search(/\r?\n\r?\n/)) >= 0) {
      var block = buf.slice(0, idx);
      buf = buf.slice(idx).replace(/^\r?\n\r?\n/, '');
      var data = block.split(/\r?\n/).filter(function (l) { return l.indexOf('data:') === 0; })
        .map(function (l) { return l.slice(5).replace(/^ /, ''); }).join('\n');
      if (!data) continue;
      try { handleEvent(JSON.parse(data)); } catch (e) { /* defekte Zeile ignorieren */ }
    }
    return buf;
  }

  function readStream(res) {
    var reader = res.body.getReader();
    var dec = new TextDecoder();
    var buf = '';
    function pump() {
      return reader.read().then(function (r) {
        if (r.done) { buf += dec.decode(); processBuffer(buf + '\n\n'); return; }
        buf += dec.decode(r.value, { stream: true });
        buf = processBuffer(buf);
        return pump();
      });
    }
    return pump();
  }

  function minutes(sec) {
    var m = Math.max(1, Math.ceil((sec || 60) / 60));
    return m + (m === 1 ? ' Minute' : ' Minuten');
  }

  function send(text) {
    text = text.trim();
    if (!text || working) return;
    if (text.length > MAX) text = text.slice(0, MAX);
    addMsg('user', text);
    prompt.value = '';
    updateCounter();
    cards = {};
    trace.querySelectorAll('.tcard').forEach(function (n) { n.remove(); });
    traceEmpty.classList.remove('hidden');
    setWorking(true);
    fetch('/api/demo/chat', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'X-Demo': '1' },
      body: JSON.stringify({ prompt: text })
    }).then(function (res) {
      if (res.status === 401) { toLogin(); return; }
      if (res.status === 429) {
        return res.json().catch(function () { return {}; }).then(function (d) {
          addMsg('error', 'Limit erreicht. Nächste Abfrage in ' + minutes(d.retryAfterSeconds) + '.' + (d.error ? ' (' + d.error + ')' : ''));
        });
      }
      if (!res.ok) {
        return res.json().catch(function () { return {}; }).then(function (d) {
          addMsg('error', d.error || 'Die Anfrage wurde abgelehnt (Status ' + res.status + ').');
        });
      }
      var ct = res.headers.get('Content-Type') || '';
      if (ct.indexOf('text/event-stream') < 0 || !res.body) {
        addMsg('error', 'Unerwartete Antwort des Servers.');
        return;
      }
      return readStream(res);
    }).catch(function () {
      addMsg('error', 'Verbindung unterbrochen. Bitte erneut versuchen.');
    }).then(function () {
      setWorking(false);
      prompt.focus();
    });
  }

  composer.addEventListener('submit', function (e) { e.preventDefault(); send(prompt.value); });
  prompt.addEventListener('input', updateCounter);
  prompt.addEventListener('keydown', function (e) {
    if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) { e.preventDefault(); send(prompt.value); }
  });
  chips.addEventListener('click', function (e) {
    var b = e.target.closest('button');
    if (b && !working) send(b.textContent);
  });
  $('logout').addEventListener('click', function () {
    fetch('/api/demo/logout', { method: 'POST', credentials: 'same-origin', headers: { 'X-Demo': '1' } })
      .catch(function () {}).then(toLogin);
  });

  updateCounter();
  fetch('/api/demo/status', { credentials: 'same-origin' })
    .then(function (r) { return r.ok ? r.json() : null; })
    .then(function (s) {
      if (!s || !s.authenticated) { toLogin(); return; }
      updateQuota(s.limits);
    })
    .catch(function () { addMsg('error', 'Status konnte nicht geladen werden.'); });
})();
