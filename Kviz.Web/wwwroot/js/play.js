/* Kvizo — oyunçu (telefon) ekranı, canlı oyun */
(function () {
  'use strict';
  const { esc, fmt, sfx, countdown, questionInput, confetti } = KG;
  const app = document.getElementById('app');
  const pin = app.dataset.pin;
  const body = document.getElementById('pBody');
  const bar = document.getElementById('pBar');
  const storeKey = 'kviz:p:' + pin;
  const TEAM_COLORS = ['var(--c0)', 'var(--c1)', 'var(--c2)', 'var(--c3)', 'var(--c4)', 'var(--c5)'];

  let saved = null;
  try { saved = JSON.parse(localStorage.getItem(storeKey) || 'null'); } catch { saved = null; }
  let state = null;
  let timer = null;
  let lastKey = '';

  const conn = new signalR.HubConnectionBuilder().withUrl('/gamehub').withAutomaticReconnect([0, 1000, 2000, 4000, 8000, 15000]).build();
  conn.on('state', s => apply(s));
  conn.on('kicked', () => { localStorage.removeItem(storeKey); saved = null; showClosed('Hostun qərarı ilə oyundan çıxarıldınız.'); conn.stop(); });
  conn.onreconnecting(() => banner('Bağlantı kəsildi, yenidən qoşulur…'));
  conn.onreconnected(async () => { banner(null); if (saved) await rejoin(true); });
  conn.onclose(() => { if (state && state.phase !== 'podium' && state.phase !== 'closed') banner('Bağlantı itdi. Səhifəni yeniləyin.'); });

  function banner(text) {
    let b = document.getElementById('connBanner');
    if (!text) { b?.remove(); return; }
    if (!b) { b = document.createElement('div'); b.id = 'connBanner'; b.className = 'conn-banner'; document.body.appendChild(b); }
    b.textContent = text;
  }

  (async function boot() {
    KG.addMuteButton();
    try { await conn.start(); } catch { body.innerHTML = '<div class="p-center"><h1 class="g-h1">Qoşulmaq mümkün olmadı</h1><p class="g-sub">İnternet və ya Wi-Fi bağlantısını yoxlayıb səhifəni yeniləyin.</p></div>'; return; }
    if (saved) { if (await rejoin(false)) return; }
    showJoinForm();
  })();

  async function rejoin(silent) {
    try {
      const r = await conn.invoke('PlayerRejoin', saved.gameId, saved.token);
      if (r.success) { apply(r.state); return true; }
    } catch { /* */ }
    if (!silent) { localStorage.removeItem(storeKey); saved = null; }
    return false;
  }

  // ---------- Avatar seçimi ----------
  const avData = (() => { try { return JSON.parse(document.getElementById('avatar-data').textContent); } catch { return { photos: false, groups: [] }; } })();
  const allPresets = avData.groups.flatMap(g => g.items);
  const randomPreset = () => 'e:' + allPresets[Math.floor(Math.random() * allPresets.length)];
  let joinPick = { preset: randomPreset(), blob: null, preview: null };   // qoşulmazdan əvvəl seçilən avatar

  /**
   * Avatar seçimi: hazır avatarlar + (host icazə veribsə) telefondan şəkil.
   * host: panelin yerləşəcəyi element. opts: { current, onPreset(av), onPhoto(file) }
   */
  function mountAvatarPicker(host, opts) {
    const groups = avData.groups.map(g => `<h4>${esc(g.title)}</h4><div class="av-grid">${g.items.map(e =>
      `<button type="button" data-av="e:${e}" aria-pressed="${opts.current === 'e:' + e}" aria-label="Avatar ${e}">${KG.avatar('e:' + e)}</button>`).join('')}</div>`).join('');
    host.innerHTML = `<div class="av-panel">
      ${avData.photos ? `<label class="av-upload">📷 Telefondan şəkil seç<input type="file" accept="image/*" id="avFile"></label>
        <div class="av-note">Şəkil avtomatik kiçildilir. Yalnız adınızın yanında göstərilir.</div>` : ''}
      ${groups}</div>`;
    host.querySelectorAll('[data-av]').forEach(b => b.addEventListener('click', () => opts.onPreset(b.dataset.av)));
    host.querySelector('#avFile')?.addEventListener('change', e => { const f = e.target.files[0]; if (f) opts.onPhoto(f); e.target.value = ''; });
  }

  /** Şəkli kiçildib serverə göndərir (oyunçu gizli açar ilə tanınır). */
  async function uploadAvatarPhoto(blob, ids) {
    const fd = new FormData();
    fd.append('gameId', ids.gameId); fd.append('playerId', ids.playerId); fd.append('token', ids.token);
    fd.append('file', blob, 'avatar.jpg');
    const res = await fetch('/play/avatar', { method: 'POST', body: fd });
    if (res.status === 429) return { ok: false, error: 'Çox sorğu göndərildi, bir az sonra yenidən cəhd edin.' };
    return res.json();
  }

  function showJoinForm(error) {
    bar.hidden = true;
    body.innerHTML = `<div class="p-center" style="max-width:440px;margin-inline:auto;width:100%">
      <div class="g-sub">${esc(app.dataset.title)}</div>
      <h1 class="g-h1">Adını yaz</h1>
      <form id="jf" style="display:grid;gap:14px;width:100%" novalidate>
        <div class="av-pick"><button type="button" class="av-current" id="avToggle" aria-expanded="false"><span id="avPrev"></span><span>Avatarını seç</span></button><div id="avBox" style="width:100%" hidden></div></div>
        <input class="g-input" id="nick" maxlength="20" autocomplete="off" placeholder="Ləqəbin" aria-label="Ləqəb">
        ${error ? `<div class="g-error" role="alert" style="margin:0">${esc(error)}</div>` : ''}
        <button class="g-btn accent" type="submit">Hazırdır, daxil ol</button>
      </form>
      <div class="g-sub">PIN: <b>${esc(pin)}</b></div></div>`;
    const input = document.getElementById('nick'); input.focus();
    const prev = document.getElementById('avPrev'), box = document.getElementById('avBox'), toggle = document.getElementById('avToggle');
    const drawPrev = () => { prev.innerHTML = joinPick.preview ? `<span class="av lg"><img src="${joinPick.preview}" alt=""></span>` : KG.avatar(joinPick.preset, 'lg'); };
    const drawPicker = () => mountAvatarPicker(box, {
      current: joinPick.blob ? '' : joinPick.preset,
      onPreset: av => { if (joinPick.preview) URL.revokeObjectURL(joinPick.preview); joinPick = { preset: av, blob: null, preview: null }; drawPrev(); drawPicker(); },
      onPhoto: async f => {
        try {
          const blob = await KG.shrinkAvatar(f);
          if (joinPick.preview) URL.revokeObjectURL(joinPick.preview);
          joinPick = { preset: joinPick.preset, blob, preview: URL.createObjectURL(blob) };
          drawPrev(); drawPicker();
        } catch (err) { Kviz.toast(err.message || 'Şəkil hazırlanmadı.', 'error'); }
      }
    });
    drawPrev();
    toggle.addEventListener('click', () => {
      box.hidden = !box.hidden; toggle.setAttribute('aria-expanded', String(!box.hidden));
      if (!box.hidden) drawPicker();
    });
    document.getElementById('jf').addEventListener('submit', async e => {
      e.preventDefault();
      const nick = input.value.trim();
      if (!nick) { input.focus(); return; }
      const btn = e.target.querySelector('button[type=submit]'); btn.disabled = true;
      try {
        const r = await conn.invoke('PlayerJoin', pin, nick, joinPick.preset);
        if (r.success) {
          saved = { gameId: r.gameId, token: r.token };
          localStorage.setItem(storeKey, JSON.stringify(saved));
          sfx.play('join');
          apply(r.state);
          // Seçilmiş şəkil varsa, qoşulandan sonra yüklənir (hazır avatar o vaxta qədər görünür)
          if (joinPick.blob) {
            try {
              const u = await uploadAvatarPhoto(joinPick.blob, { gameId: r.gameId, playerId: r.playerId, token: r.token });
              if (!u.ok) Kviz.toast(u.error || 'Şəkil yüklənmədi, hazır avatar qaldı.', 'error');
            } catch { Kviz.toast('Şəkil yüklənmədi, hazır avatar qaldı.', 'error'); }
            if (joinPick.preview) URL.revokeObjectURL(joinPick.preview);
          }
          joinPick = { preset: randomPreset(), blob: null, preview: null };
        } else showJoinForm(r.error || 'Qoşulmaq mümkün olmadı.');
      } catch { showJoinForm('Bağlantı xətası. Yenidən cəhd edin.'); }
    });
  }

  function showClosed(msg) {
    bar.hidden = true;
    body.innerHTML = `<div class="p-center"><div style="font-size:4rem">👋</div><h1 class="g-h1">${esc(msg || 'Oyun bitdi')}</h1><a class="g-btn" href="/join">Yeni oyuna qoşul</a></div>`;
  }

  // ---------- Render ----------
  function apply(s) {
    const prev = state;
    state = s;
    bar.hidden = false;
    document.getElementById('pNick').textContent = s.you.nickname + (s.you.teamName ? ' · ' + s.you.teamName : '');
    document.getElementById('pAv').innerHTML = KG.avatar(s.you.avatar, 'sm', s.you.nickname);
    document.getElementById('pScore').textContent = s.concealed ? '–' : fmt(s.you.score);

    const key = s.phase + ':' + (s.question ? s.question.index : -1);
    // Eyni sual açıqdırsa və cavab verilməyibsə, yazılan mətn itməsin deyə yalnız güc paneli yenilənir
    if (key === lastKey && s.phase === 'question' && !s.answered && prev && !prev.answered) { refreshPowerUps(); refreshHidden(); return; }
    lastKey = key;
    if (timer) { timer.stop(); timer = null; }

    switch (s.phase) {
      case 'lobby': return renderLobby(s);
      case 'intro': return renderIntro(s);
      case 'question': return s.answered ? renderWaiting() : renderQuestion(s);
      case 'results': return renderResult(s);
      case 'leaderboard': return renderLeaderboard(s);
      case 'podium': return renderPodium(s);
      default: localStorage.removeItem(storeKey); showClosed(s.phase === 'closed' ? 'Oyun ləğv edildi' : 'Oyun bitdi');
    }
  }

  function teamChip(s) {
    return s.you.teamName ? `<div class="g-chip" style="background:${TEAM_COLORS[s.you.teamColor % 6]};color:#fff;font-size:1.1rem">${esc(s.you.teamName)}</div>` : '';
  }

  let lobbyPickerOpen = false;
  function renderLobby(s) {
    body.innerHTML = `<div class="p-center"><div class="av-pick">${KG.avatar(s.you.avatar, 'xl', s.you.nickname)}</div>
      <h1 class="g-h1">Oyundasan!</h1>${teamChip(s)}
      <div class="g-chip" style="font-size:1.3rem;margin-top:6px">${esc(s.you.nickname)}</div>
      <div class="av-pick"><button type="button" class="av-current" id="avToggle" style="padding:10px 18px" aria-expanded="${lobbyPickerOpen}">🎨 Avatarı dəyiş</button><div id="avBox" style="width:100%" ${lobbyPickerOpen ? '' : 'hidden'}></div></div>
      <div class="g-sub" style="margin-top:10px">Host oyunu başlatmasını gözləyirik…</div></div>`;
    const box = document.getElementById('avBox');
    const draw = () => mountAvatarPicker(box, {
      current: s.you.avatar,
      onPreset: async av => { const r = await conn.invoke('PlayerSetAvatar', av); if (!r.ok) Kviz.toast(r.error || 'Avatar dəyişmədi.', 'error'); },
      onPhoto: async f => {
        try {
          const blob = await KG.shrinkAvatar(f);
          const u = await uploadAvatarPhoto(blob, { gameId: saved.gameId, playerId: s.you.id, token: saved.token });
          if (!u.ok) Kviz.toast(u.error || 'Şəkil yüklənmədi.', 'error');
        } catch (err) { Kviz.toast(err.message || 'Şəkil yüklənmədi.', 'error'); }
      }
    });
    if (lobbyPickerOpen) draw();
    document.getElementById('avToggle').addEventListener('click', () => {
      lobbyPickerOpen = box.hidden; box.hidden = !box.hidden;
      document.getElementById('avToggle').setAttribute('aria-expanded', String(lobbyPickerOpen));
      if (lobbyPickerOpen) draw();
    });
  }

  function powerBar(s) {
    if (!s.powerUps) return '';
    const y = s.you;
    const btn = (id, label, used, armed) => `<button type="button" class="pw ${armed ? 'armed' : ''}" data-pw="${id}" ${used ? 'disabled' : ''}>${label}</button>`;
    return `<div class="pw-bar" id="pwBar">${btn('double', '⚡ 2× xal', y.doubleUsed, y.doubleArmed)}${btn('fifty', '✂️ 50/50', y.fiftyUsed, false)}${btn('shield', '🛡️ Qoruyucu', y.shieldUsed, y.shieldArmed)}</div>`;
  }
  function wirePowerUps() {
    document.querySelectorAll('[data-pw]').forEach(b => b.addEventListener('click', async () => {
      const r = await conn.invoke('PlayerPowerUp', b.dataset.pw);
      if (!r.ok) Kviz.toast(r.error || 'Güc istifadə olunmadı.', 'error');
      else sfx.play('start');
    }));
  }
  function refreshPowerUps() {
    const host = document.getElementById('pwBar'); if (!host) return;
    host.outerHTML = powerBar(state); wirePowerUps();
  }
  function refreshHidden() {
    const hid = new Set(state.hiddenOptions || []);
    document.querySelectorAll('.player-answers .ans').forEach((a, i) => a.classList.toggle('hide', hid.has(i)));
  }

  function renderIntro(s) {
    const q = s.question;
    body.innerHTML = `<div class="p-center"><div class="g-sub">Sual ${q.index + 1} / ${q.total}</div><h1 class="g-h1">Hazır ol!</h1>
      <div class="spinner"></div>${powerBar(s)}</div>`;
    wirePowerUps();
  }

  function renderQuestion(s) {
    const q = s.question;
    body.innerHTML = `<div class="p-timer" aria-hidden="true"><i id="pt"></i></div>
      ${q.text ? `<div class="p-qtext">${esc(q.text)}</div>` : ''}
      ${KG.playerImages(q)}
      <div id="inputHost" style="display:flex;flex-direction:column;flex:1"></div>${powerBar(s)}`;
    wirePowerUps();
    const host = document.getElementById('inputHost');
    const mount = () => host.replaceChildren(questionInput(q, { hidden: state.hiddenOptions, onSubmit: submitAnswer }));
    async function submitAnswer(answer) {
      try {
        const r = await conn.invoke('PlayerAnswer', answer);
        if (r.ok) { renderWaiting(); return; }
        Kviz.toast(r.error || 'Cavab göndərilmədi.', 'error');
        if (/Vaxt|qəbul|Artıq/.test(r.error || '')) renderWaiting(); else mount();
      } catch {
        Kviz.toast('Bağlantı xətası, yenidən cəhd edin.', 'error');
        mount();
      }
    }
    mount();
    timer = countdown(s.remainingMs, q.timeLimitSeconds * 1000, (left, f) => { const t = document.getElementById('pt'); if (t) t.style.transform = `scaleX(${f})`; });
  }

  function renderWaiting() {
    if (timer) { timer.stop(); timer = null; }
    body.innerHTML = `<div class="p-center"><div style="font-size:3.4rem">✅</div><h1 class="g-h1">Cavab göndərildi</h1><div class="g-sub">Digərlərini gözləyirik…</div><div class="spinner" style="margin-top:12px"></div></div>`;
  }

  function renderResult(s) {
    const r = s.result;
    let cls = 'neutral', big = 'Cavab qeydə alındı', extra = '';
    if (!r || !r.answered) { cls = r && r.scored ? 'bad' : 'neutral'; big = r && r.scored ? 'Vaxt bitdi ⏰' : 'Cavab yoxdur'; }
    else if (!r.scored) { big = 'Cavabın qeydə alındı 👍'; }
    else if (r.correct) { cls = 'ok'; big = 'Düzgündür! 🎉'; sfx.play('correct'); }
    else { cls = 'bad'; big = 'Yanlış 😕'; sfx.play('wrong'); }
    if (r && r.scored && !r.correct) sfx.play('wrong');
    if (r && r.scored) {
      extra = r.correct ? `<div class="pts">+${fmt(r.points)} xal</div>` + (r.streakBonus ? `<div class="pts" style="font-size:1.1rem">🔥 Seriya bonusu +${fmt(r.streakBonus)}</div>` : '') : '';
      if (r.streak >= 2) extra += `<div class="pts" style="font-size:1.05rem">🔥 ${r.streak} ardıcıl düzgün</div>`;
    }
    body.innerHTML = `<div class="p-center"><div class="feedback ${cls}"><div class="big">${big}</div>${extra}</div>
      ${r && r.scored && !s.concealed ? `<div class="g-sub" style="margin-top:6px">Yerin: <b>${r.rank}</b>${r.pointsBehind != null && r.rank > 1 ? ` · ${esc(r.nextNickname)}-dən ${fmt(r.pointsBehind)} xal geridə` : ''}</div>` : ''}</div>`;
  }

  /** Host cədvəli hələ açmayıb: yer, xal və siyahı serverdən də gəlmir. */
  function renderConcealed(s) {
    const podium = s.phase === 'podium';
    body.innerHTML = `<div class="p-center"><div style="font-size:3.6rem">${podium ? '🏁' : '🔒'}</div>
      <h1 class="g-h1">${podium ? 'Oyun bitdi!' : 'Nəticələr hazırlanır'}</h1>
      <div class="g-sub">${podium ? 'Yekun nəticələri aparıcı elan edəcək' : 'Aparıcı açıqlayana qədər gözləyin'}…</div>
      <div class="spinner" style="margin-top:14px"></div></div>`;
  }

  function renderLeaderboard(s) {
    if (s.concealed) return renderConcealed(s);
    const r = s.result;
    const rows = s.leaderboard.map(p => `<div class="lb-row ${p.playerId === s.you.id ? 'me' : ''}" style="font-size:1.05rem;padding:9px 14px"><span class="r">${p.rank}</span><span class="nm">${KG.avatar(p.avatar, 'sm', p.nickname)}<span>${esc(p.nickname)}</span></span><span class="sc">${fmt(p.score)}</span></div>`).join('');
    body.innerHTML = `<div class="p-center" style="justify-content:flex-start"><div class="g-sub">Sənin yerin</div><div class="rank-big">${s.you.rank}</div>
      <div class="g-sub">${fmt(s.you.score)} xal${r && r.points ? ` · +${fmt(r.points + (r.streakBonus || 0))}` : ''}</div>
      <div class="lb" style="margin-top:14px;width:100%">${rows}</div></div>`;
  }

  function renderPodium(s) {
    if (s.concealed) return renderConcealed(s); // oyunçu səhifəni yeniləsə də qoşulu qalır (storeKey silinmir)
    const rank = s.you.rank;
    const medal = rank === 1 ? '🥇' : rank === 2 ? '🥈' : rank === 3 ? '🥉' : '🎯';
    body.innerHTML = `<div class="p-center"><div style="font-size:4rem">${medal}</div><h1 class="g-h1">${rank === 1 ? 'Qalib sənsən!' : rank <= 3 ? 'Podiumdasan!' : 'Oyun bitdi'}</h1>
      <div class="g-sub">Son yerin</div><div class="rank-big">${rank}</div>
      <div class="g-chip" style="font-size:1.2rem">${fmt(s.you.score)} xal</div>
      <div class="g-sub">${s.you.correct} düzgün cavab</div>
      <a class="g-btn" style="margin-top:14px" href="/join">Yeni oyuna qoşul</a></div>`;
    localStorage.removeItem(storeKey);
    if (rank <= 3) { sfx.play('fanfare'); confetti(); } else sfx.play('end');
  }
})();
