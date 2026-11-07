/* Kvizo — tapşırıq rejimi: oyunçu sualları öz tempi ilə keçir (HTTP API) */
(function () {
  'use strict';
  const { esc, fmt, sfx, countdown, questionInput, confetti } = KG;
  const app = document.getElementById('app');
  const pin = app.dataset.pin;
  const body = document.getElementById('pBody');
  const bar = document.getElementById('pBar');
  const storeKey = 'kviz:c:' + pin;
  let token = localStorage.getItem(storeKey);
  let timer = null;
  let current = null;

  KG.addMuteButton();

  async function api(path, payload) {
    const res = await fetch('/api/challenge/' + path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
    let data = null; try { data = await res.json(); } catch { /* */ }
    return { ok: res.ok, status: res.status, data };
  }

  function showJoin(error) {
    bar.hidden = true;
    body.innerHTML = `<div class="p-center" style="max-width:440px;margin-inline:auto;width:100%">
      <div class="g-chip">📝 Tapşırıq</div>
      <div class="g-sub" style="margin-top:6px">${esc(app.dataset.title)}</div>
      <h1 class="g-h1">Adını yaz</h1>
      <form id="jf" style="display:grid;gap:14px;width:100%" novalidate>
        <input class="g-input" id="nick" maxlength="20" autocomplete="off" placeholder="Ləqəbin" aria-label="Ləqəb">
        ${error ? `<div class="g-error" role="alert" style="margin:0">${esc(error)}</div>` : ''}
        <button class="g-btn accent" type="submit">Başla</button>
      </form></div>`;
    const input = document.getElementById('nick'); input.focus();
    document.getElementById('jf').addEventListener('submit', async e => {
      e.preventDefault();
      const nick = input.value.trim(); if (!nick) { input.focus(); return; }
      e.target.querySelector('button').disabled = true;
      const r = await api('join', { pin, nickname: nick });
      if (r.ok && r.data.success) { token = r.data.token; localStorage.setItem(storeKey, token); sfx.play('start'); load(); }
      else showJoin(r.data?.error || (r.status === 429 ? 'Çox sayda cəhd. Bir az gözləyin.' : 'Qoşulmaq mümkün olmadı.'));
    });
  }

  async function load() {
    if (timer) { timer.stop(); timer = null; }
    const r = await api('state', { token });
    if (r.status === 404) { localStorage.removeItem(storeKey); token = null; showJoin(); return; }
    if (!r.ok) { body.innerHTML = '<div class="p-center"><h1 class="g-h1">Bağlantı xətası</h1><button class="g-btn" id="retry">Yenidən yoxla</button></div>'; document.getElementById('retry').onclick = load; return; }
    current = r.data;
    bar.hidden = false;
    document.getElementById('pNick').textContent = current.nickname;
    document.getElementById('pScore').textContent = fmt(current.score);
    if (current.phase === 'question') renderQuestion(current);
    else if (current.phase === 'finished') renderFinished(current);
    else renderClosed(current);
  }

  // Sualdan əvvəl qısa geri sayım: vaxt sual göstərildiyi anda başlayır
  function renderQuestion(s) {
    const q = s.question;
    body.innerHTML = `<div class="p-timer" aria-hidden="true"><i id="pt"></i></div>
      <div class="row between" style="font-weight:800"><span class="g-chip">Sual ${q.index + 1} / ${q.total}</span><span class="g-chip">🔥 ${s.streak}</span></div>
      ${q.text ? `<div class="p-qtext">${esc(q.text)}</div>` : ''}
      ${KG.playerImages(q)}
      <div id="inputHost" style="display:flex;flex-direction:column;flex:1"></div>`;
    const host = document.getElementById('inputHost');
    host.appendChild(questionInput(q, { onSubmit: answer => submit(answer) }));
    timer = countdown(s.remainingMs, q.timeLimitSeconds * 1000, (left, f) => {
      const t = document.getElementById('pt'); if (t) t.style.transform = `scaleX(${f})`;
    }, () => { Kviz.toast('Vaxt bitdi', 'error'); sfx.play('wrong'); setTimeout(load, 900); });
  }

  async function submit(answer) {
    if (timer) { timer.stop(); timer = null; }
    body.innerHTML = '<div class="p-center"><div class="spinner"></div></div>';
    const r = await api('answer', { token, answer });
    if (!r.ok || !r.data.accepted) {
      Kviz.toast(r.data?.error || 'Cavab göndərilmədi.', 'error');
      setTimeout(load, 600); return;
    }
    renderFeedback(r.data);
  }

  function correctText(rv) {
    if (!rv) return '';
    switch (rv.type) {
      case 'MultipleChoice': case 'TrueFalse': return rv.correctIndexes.map(i => rv.optionTexts[i]).join(' / ');
      case 'Puzzle': return rv.correctOrder.join(' → ');
      case 'TypeAnswer': return rv.acceptedAnswers.join(' / ');
      case 'Slider': return fmt(rv.sliderCorrect) + (rv.sliderTolerance ? ` (±${fmt(+rv.sliderTolerance.toFixed(2))})` : '');
      default: return '';
    }
  }

  function renderFeedback(f) {
    document.getElementById('pScore').textContent = fmt(f.score);
    let cls = 'neutral', big = 'Cavab qeydə alındı 👍', extra = '';
    if (f.scored) {
      if (f.timedOut) { cls = 'bad'; big = 'Vaxt bitdi ⏰'; sfx.play('wrong'); }
      else if (f.correct) { cls = 'ok'; big = 'Düzgündür! 🎉'; sfx.play('correct'); extra = `<div class="pts">+${fmt(f.points)} xal</div>` + (f.streakBonus ? `<div class="pts" style="font-size:1.1rem">🔥 Seriya bonusu +${fmt(f.streakBonus)}</div>` : ''); }
      else { cls = 'bad'; big = 'Yanlış 😕'; sfx.play('wrong'); }
      if (!f.correct) { const ct = correctText(f.reveal); if (ct) extra += `<div class="pts" style="font-size:1.05rem">Düzgün cavab: ${esc(ct)}</div>`; }
    }
    body.innerHTML = `<div class="p-center"><div class="feedback ${cls}"><div class="big">${big}</div>${extra}</div>
      <button class="g-btn accent" id="nextBtn" style="margin-top:10px">Davam et <svg class="icon"><use href="#i-arrow"/></svg></button></div>`;
    document.getElementById('nextBtn').addEventListener('click', load);
    const auto = setTimeout(load, f.correct ? 2600 : 4200);
    document.getElementById('nextBtn').addEventListener('click', () => clearTimeout(auto));
  }

  function lbRows(list, meId) {
    return list.map(p => `<div class="lb-row ${p.playerId === meId ? 'me' : ''}" style="font-size:1.05rem;padding:9px 14px"><span class="r">${p.rank}</span><span class="nm">${esc(p.nickname)}</span><span class="sc">${fmt(p.score)}</span></div>`).join('');
  }

  function renderFinished(s) {
    const medal = s.rank === 1 ? '🥇' : s.rank === 2 ? '🥈' : s.rank === 3 ? '🥉' : '🎯';
    body.innerHTML = `<div class="p-center" style="justify-content:flex-start"><div style="font-size:3.6rem">${medal}</div><h1 class="g-h1">Tapşırıq tamamlandı!</h1>
      <div class="g-sub">Yerin</div><div class="rank-big">${s.rank}</div>
      <div class="row" style="justify-content:center"><span class="g-chip" style="font-size:1.15rem">${fmt(s.score)} xal</span><span class="g-chip" style="font-size:1.15rem">${s.correct} / ${s.totalQuestions} düzgün</span></div>
      ${s.showLeaderboard && s.leaderboard.length ? `<h3 class="g-sub" style="margin:18px 0 0">Liderlər</h3><div class="lb" style="width:100%">${lbRows(s.leaderboard, s.playerId)}</div>` : ''}
      <div class="g-sub" style="margin-top:12px">${s.playerCount} nəfər iştirak edir${s.deadline ? ' · son tarix ' + new Date(s.deadline).toLocaleString('az', { dateStyle: 'short', timeStyle: 'short' }) : ''}</div>
      <button class="g-btn ghost sm" id="refresh">Nəticəni yenilə</button></div>`;
    document.getElementById('refresh').addEventListener('click', load);
    if (s.rank <= 3) { sfx.play('fanfare'); confetti(); }
  }

  function renderClosed(s) {
    bar.hidden = false;
    body.innerHTML = `<div class="p-center"><div style="font-size:3.4rem">🔒</div><h1 class="g-h1">Tapşırıq bağlanıb</h1>
      <div class="g-sub">Sənin nəticən: ${fmt(s.score)} xal, ${s.rank}-ci yer</div>
      ${s.showLeaderboard && s.leaderboard.length ? `<div class="lb" style="width:100%">${lbRows(s.leaderboard, s.playerId)}</div>` : ''}
      <a class="g-btn" href="/join">Yeni oyuna qoşul</a></div>`;
  }

  token ? load() : showJoin();
})();
