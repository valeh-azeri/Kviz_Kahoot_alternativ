/* Kvizo — host (aparıcı) ekranı */
(function () {
  'use strict';
  const { esc, shape, fmt, sfx, countdown, resultView, animateBars, confetti } = KG;
  const init = JSON.parse(document.getElementById('host-init').textContent);
  const stage = document.getElementById('stage');
  const progress = document.getElementById('gProgress');
  const TEAM_COLORS = ['var(--c0)', 'var(--c1)', 'var(--c2)', 'var(--c3)', 'var(--c4)', 'var(--c5)'];
  const isTeam = init.mode === 'LiveTeam';

  let join = init.join;
  let state = null;
  let players = new Map();
  let timer = null;
  let lastPhaseKey = '';
  let busy = false;
  let podiumShown = false; // açılış səsi və konfeti yalnız bir dəfə (host "göstər" deyəndə təkrar olmasın)

  KG.addMuteButton();

  const conn = new signalR.HubConnectionBuilder()
    .withUrl('/gamehub')
    .withAutomaticReconnect([0, 1000, 2000, 4000, 8000, 15000])
    .build();

  conn.on('state', s => apply(s));
  conn.on('playerJoined', p => {
    const isNew = !players.has(p.id);
    players.set(p.id, p);
    if (state?.phase === 'lobby') { drawPlayers(); if (isNew) sfx.play('join'); }
  });
  conn.on('playerLeft', p => { players.delete(p.id); if (state?.phase === 'lobby') drawPlayers(); updateAnswered(); });
  conn.on('answerCount', c => { if (state) { state.answeredCount = c.answered; state.playerCount = c.total; } updateAnswered(); });
  conn.onreconnecting(() => banner('Bağlantı kəsildi, yenidən qoşulur…'));
  conn.onreconnected(async () => { banner(null); await connectHost(); });
  conn.onclose(() => banner('Bağlantı itdi. Səhifəni yeniləyin.'));

  function banner(text) {
    let b = document.getElementById('connBanner');
    if (!text) { b?.remove(); return; }
    if (!b) { b = document.createElement('div'); b.id = 'connBanner'; b.className = 'conn-banner'; document.body.appendChild(b); }
    b.textContent = text;
  }

  async function connectHost() {
    const s = await conn.invoke('HostConnect', init.gameId);
    if (!s) { stage.innerHTML = '<h1 class="g-h1">Oyun tapılmadı</h1><p class="g-sub">Oyun bitib və ya sizə aid deyil.</p><a class="g-btn" href="/Game/History">Hesabatlar</a>'; return; }
    apply(s);
  }

  (async function boot() {
    try { await conn.start(); await connectHost(); }
    catch (e) { stage.innerHTML = '<h1 class="g-h1">Qoşulmaq mümkün olmadı</h1><p class="g-sub">Serverin işlədiyini yoxlayın və səhifəni yeniləyin.</p>'; console.error(e); }
  })();

  async function cmd(name, ...args) {
    if (busy) return;
    busy = true;
    try {
      const r = await conn.invoke(name, init.gameId, ...args);
      if (r && !r.ok && r.error) Kviz.toast(r.error, 'error');
      return r;
    } catch (e) { Kviz.toast('Əmr göndərilmədi, bağlantını yoxlayın.', 'error'); }
    finally { setTimeout(() => { busy = false; }, 250); }
  }

  document.getElementById('btnEnd').addEventListener('click', async () => {
    const lobby = state?.phase === 'lobby';
    if (state?.phase === 'podium' || state?.phase === 'closed') {
      if (state.phase === 'podium' && !(await confirmSkipUnrevealed())) return;
      location.href = '/Game/Report/' + init.gameId; return;
    }
    const ok = await Kviz.confirm(lobby ? 'Oyun ləğv edilsin?' : 'Oyun bitirilsin?',
      lobby ? 'Gözləmə otağı bağlanacaq.' : 'Yekun nəticələr göstəriləcək və oyun bitəcək.', lobby ? 'Ləğv et' : 'Bitir');
    if (ok) { await cmd('HostFinish'); if (lobby) location.href = '/Game/History'; }
  });

  // ---------- Render ----------
  function apply(s) {
    state = s;
    if (s.phase === 'lobby' && s.players) { players = new Map(s.players.map(p => [p.id, p])); }
    const key = s.phase + ':' + s.questionIndex;
    const phaseChanged = key !== lastPhaseKey;
    lastPhaseKey = key;
    if (timer) { timer.stop(); timer = null; }

    if (s.questionIndex >= 0 && s.phase !== 'podium' && s.phase !== 'closed') {
      progress.hidden = false; progress.textContent = `Sual ${s.questionIndex + 1} / ${s.totalQuestions}`;
    } else progress.hidden = true;

    switch (s.phase) {
      case 'lobby': return renderLobby();
      case 'intro': return renderIntro(s, phaseChanged);
      case 'question': return renderQuestion(s, phaseChanged);
      case 'results': return renderResults(s);
      case 'leaderboard': return renderLeaderboard(s);
      case 'podium': return renderPodium(s);
      default: stage.innerHTML = '<h1 class="g-h1">Oyun bağlandı</h1><a class="g-btn" href="/Game/History">Hesabatlar</a>';
    }
  }

  function joinBox() {
    const alts = join.alternatives || [];
    const picker = alts.length > 1
      ? `<div style="margin-top:10px"><label class="muted" style="font-size:.85rem;font-weight:800">Şəbəkə ünvanı</label><select id="ipSel" class="input" style="margin-top:4px">${alts.map(a => `<option ${a === join.baseUrl ? 'selected' : ''}>${esc(a)}</option>`).join('')}</select><div class="muted" style="font-size:.8rem;margin-top:4px">Telefonlar hansı şəbəkədədirsə, ona uyğun ünvanı seçin.</div></div>` : '';
    return `<div class="pin-box">
      <div class="where">Brauzerdə bu ünvana daxil olun</div>
      <div class="url">${esc(join.shortUrl)}</div>
      <div class="where" style="margin-top:8px">PIN kodunu yazın</div>
      <div class="pin">${esc(init.pin)}</div>
      <img class="qr" src="/qr?data=${encodeURIComponent(join.joinUrl)}" alt="QR kod: ${esc(join.joinUrl)}" width="240" height="240">
      <div class="muted" style="font-size:.85rem;margin-top:6px">və ya telefon kamerası ilə QR kodu oxudun</div>
      ${picker}
    </div>`;
  }

  function renderLobby() {
    stage.className = 'g-main top';
    stage.innerHTML = `<div class="lobby-grid">
      ${joinBox()}
      <div>
        <div class="players-head">
          <div><span class="g-h1" style="font-size:2rem" id="pcount">0 oyunçu</span><div class="g-sub" id="pwait">Oyunçuların qoşulmasını gözləyirik…</div></div>
          <div class="row">
            <label class="g-chip" style="cursor:pointer"><input type="checkbox" id="lockChk" style="accent-color:#ffc933" ${state.locked ? 'checked' : ''}> Qoşulmanı bağla</label>
            <button class="g-btn accent" id="btnStart" disabled>Başla <svg class="icon"><use href="#i-arrow"/></svg></button>
          </div>
        </div>
        <div id="playersBox" class="players-box"></div>
      </div>
    </div>`;
    document.getElementById('btnStart').addEventListener('click', async () => { sfx.play('start'); await cmd('HostStart'); });
    document.getElementById('lockChk').addEventListener('change', e => cmd('HostLock', e.target.checked));
    document.getElementById('ipSel')?.addEventListener('change', async e => {
      const r = await fetch(`/Game/JoinInfo/${init.gameId}?baseUrl=${encodeURIComponent(e.target.value)}`, { credentials: 'same-origin' });
      if (r.ok) { join = await r.json(); renderLobby(); }
    });
    drawPlayers();
  }

  function drawPlayers() {
    const box = document.getElementById('playersBox'); if (!box) return;
    const list = [...players.values()];
    document.getElementById('pcount').textContent = list.length + ' oyunçu';
    document.getElementById('pwait').textContent = list.length ? 'Hazır olanda “Başla” düyməsinə basın.' : 'Oyunçuların qoşulmasını gözləyirik…';
    document.getElementById('btnStart').disabled = list.length === 0;
    const chip = p => `<span class="p-chip ${p.connected ? '' : 'off'}">${KG.avatar(p.avatar, 'sm', p.nickname)}${esc(p.nickname)}<button type="button" data-kick="${p.id}" title="Çıxar" aria-label="${esc(p.nickname)} adlı oyunçunu çıxar">✕</button></span>`;
    if (isTeam && state.teams?.length) {
      box.innerHTML = '<div class="team-cols">' + state.teams.map((t, i) => {
        const members = list.filter(p => p.teamId === t.id);
        return `<div class="team-col" style="--tc:${TEAM_COLORS[t.colorIndex % 6]}"><h4><span>${esc(t.name)}</span><span>${members.length}</span></h4><div class="players-cloud">${members.map(chip).join('')}</div></div>`;
      }).join('') + '</div>';
    } else {
      box.innerHTML = '<div class="players-cloud">' + list.map(chip).join('') + '</div>';
    }
    box.querySelectorAll('[data-kick]').forEach(b => b.addEventListener('click', async () => {
      if (await Kviz.confirm('Oyunçu çıxarılsın?', 'Bu oyunçu oyundan çıxarılacaq.', 'Çıxar')) cmd('HostKick', b.dataset.kick);
    }));
  }

  function qHead(s) {
    const q = s.question;
    const solo = !q.text; // yalnız şəkildən ibarət sual
    const both = q.imageUrl && q.relatedImageUrl;
    // Şəkillər ALT-ALTA: əvvəl sualın şəkli (sualın özü), onun altında sualla bağlı əlavə şəkil
    const images = (q.imageUrl || q.relatedImageUrl) ? `<div class="q-images ${both ? 'two' : ''}">` +
      (q.imageUrl ? `<img class="q-media ${solo && !both ? 'solo' : ''}" src="${esc(q.imageUrl)}" alt="">` : '') +
      (q.relatedImageUrl ? `<img class="q-media related" src="${esc(q.relatedImageUrl)}" alt="">` +
        (q.relatedImageCredit ? `<div class="g-credit">Şəkil: ${esc(q.relatedImageCredit)}</div>` : '') : '') + `</div>` : '';
    return (q.text ? `<div class="q-big">${esc(q.text)}</div>` : '') + images;
  }

  function renderIntro(s, changed) {
    const q = s.question;
    stage.className = 'g-main';
    stage.innerHTML = `<div class="g-sub" style="margin-bottom:10px">Sual ${q.index + 1} / ${q.total} · ${esc(typeName(q.type))}</div>${qHead(s)}
      <div class="g-h1" style="margin-top:22px">Hazır olun!</div>
      <div class="countdown-bar"><i id="introBar"></i></div>`;
    const total = Math.max(s.remainingMs, 1);
    timer = countdown(s.remainingMs, total, (left, f) => { document.getElementById('introBar').style.transform = `scaleX(${f})`; });
    if (changed) sfx.play('start');
  }

  function typeName(t) {
    return ({ MultipleChoice: 'Test', TrueFalse: 'Doğru / Yanlış', TypeAnswer: 'Cavabı yaz', Slider: 'Sürüşdürücü', Puzzle: 'Puzzle', Poll: 'Sorğu', WordCloud: 'Söz buludu', OpenEnded: 'Açıq sual' })[t] || t;
  }

  function renderQuestion(s) {
    const q = s.question;
    stage.className = 'g-main';
    let body = '';
    if (['MultipleChoice', 'TrueFalse', 'Poll'].includes(q.type)) {
      body = `<div class="answers ${q.options.length > 4 ? '' : ''}">` + q.options.map((t, i) => `<div class="ans static c${i % 6}">${shape(i)}<span>${esc(t)}</span></div>`).join('') + '</div>' +
        (q.type === 'MultipleChoice' && q.multiSelect ? '<div class="g-sub" style="margin-top:12px">Bir neçə düzgün cavab var</div>' : '');
    } else if (q.type === 'Puzzle') {
      body = '<div class="g-sub" style="margin-top:14px">Telefonda düzgün ardıcıllıqla düzün</div><div class="seq-order" style="margin-inline:auto">' + q.options.map(t => `<div>${esc(t)}</div>`).join('').replace(/<div>/g, '<div style="--x:0">') + '</div>';
    } else if (q.type === 'Slider') {
      body = `<div class="g-sub" style="margin-top:18px">Telefonda düzgün rəqəmi seçin</div><div class="hist-axis" style="font-size:1.6rem;max-width:600px;margin-inline:auto"><span>${fmt(q.sliderMin)}</span><span>↔</span><span>${fmt(q.sliderMax)}</span></div>`;
    } else {
      body = `<div class="g-sub" style="margin-top:18px">${q.type === 'WordCloud' ? 'Telefonda bir söz yazın' : q.type === 'TypeAnswer' ? 'Telefonda cavabı yazın' : 'Telefonda fikrinizi yazın'}</div>`;
    }
    stage.innerHTML = `<div class="q-head">
        <div class="timer" id="timer"><span id="tsec">${q.timeLimitSeconds}</span></div>
        <div class="answer-count"><b id="ansCount">${s.answeredCount}</b><span class="g-sub">cavab</span></div>
        <button class="g-btn ghost" id="btnSkip">Keç ›</button>
      </div>${qHead(s)}${body}`;
    document.getElementById('btnSkip').addEventListener('click', () => cmd('HostAdvance'));
    const total = q.timeLimitSeconds * 1000;
    let lastSec = -1;
    timer = countdown(s.remainingMs, total, (left, f) => {
      const t = document.getElementById('timer'); if (!t) return;
      t.style.setProperty('--p', f.toFixed(4));
      const sec = Math.ceil(left / 1000);
      document.getElementById('tsec').textContent = sec;
      t.classList.toggle('low', sec <= 5 && left > 0);
      if (sec !== lastSec) { lastSec = sec; if (sec <= 5 && sec > 0) sfx.play('tick'); }
    });
  }

  function updateAnswered() {
    const c = document.getElementById('ansCount');
    if (c && state) c.textContent = state.answeredCount;
  }

  function nextLabel(s) {
    if (s.isLastQuestion) return 'Yekun nəticələr';
    return s.leaderboardNext ? 'Liderlər cədvəli' : 'Növbəti sual';
  }

  function renderResults(s) {
    const q = s.question;
    stage.className = 'g-main';
    stage.innerHTML = `<div class="q-head"><div class="g-chip">${esc(typeName(q.type))}</div><button class="g-btn accent" id="btnNext">${nextLabel(s)} <svg class="icon"><use href="#i-arrow"/></svg></button></div>
      ${q.text ? `<div class="q-big" style="font-size:clamp(1.1rem,2.6vw,1.8rem)">${esc(q.text)}</div>` : (q.imageUrl ? `<img class="q-media" style="max-height:22vh" src="${esc(q.imageUrl)}" alt="">` : '')}
      <div style="width:100%;margin-top:6px" id="resBox">${resultView(s.result)}</div>`;
    document.getElementById('btnNext').addEventListener('click', () => cmd('HostAdvance'));
    animateBars(stage);
    sfx.play('end');
  }

  function lbRows(list) {
    return list.map((p, i) => `<div class="lb-row" style="animation-delay:${i * 0.12}s"><span class="r">${p.rank}</span>
      <span class="nm">${KG.avatar(p.avatar, 'sm', p.nickname)}<span>${esc(p.nickname)}${p.teamName ? `<small>${esc(p.teamName)}</small>` : ''}</span></span>
      <span class="sc">${fmt(p.score)}${p.delta > 0 ? `<small>+${fmt(p.delta)}</small>` : ''}</span></div>`).join('');
  }
  function teamRows(list) {
    return list.map(t => `<div class="tl" style="--tc:${TEAM_COLORS[t.colorIndex % 6]}"><span>${t.rank}</span><span>${esc(t.name)} <small style="opacity:.85">· ${t.members} nəfər</small></span><span>${fmt(t.score)}</span></div>`).join('');
  }

  /** "Əvvəl mən görüm" rejimi: cədvəl oyunçulara açılıb, ya yox. */
  function revealBar(s) {
    if (!s.hostReveals) return '';
    return s.revealed
      ? '<div class="reveal-bar done">✓ Oyunçulara göstərildi</div>'
      : '<div class="reveal-bar"><span>🔒 Cədvəli yalnız siz görürsünüz. Oyunçular telefonlarında gözləyir.</span><button class="g-btn accent" id="btnReveal">Oyunçulara göstər</button></div>';
  }
  function wireReveal() {
    document.getElementById('btnReveal')?.addEventListener('click', async () => { sfx.play('start'); await cmd('HostReveal'); });
  }
  /** Cədvəl açılmadan keçid edilirsə, təsadüfən keçməmək üçün soruşur. */
  async function confirmSkipUnrevealed() {
    if (!state?.hostReveals || state.revealed) return true;
    return Kviz.confirm('Oyunçulara göstərilməyib', 'Cədvəli oyunçulara açmadınız, onlar bunu görməyəcək. Yenə də davam edilsin?', 'Davam et');
  }

  function renderLeaderboard(s) {
    stage.className = 'g-main';
    stage.innerHTML = `<div class="q-head"><h1 class="g-h1" style="margin:0">Liderlər cədvəli</h1><button class="g-btn accent" id="btnNext">Növbəti sual <svg class="icon"><use href="#i-arrow"/></svg></button></div>
      ${revealBar(s)}
      ${s.teamLeaderboard?.length ? `<div class="team-lb">${teamRows(s.teamLeaderboard)}</div><h3 style="margin:18px 0 0" class="g-sub">Fərdi nəticələr</h3>` : ''}
      <div class="lb">${lbRows(s.leaderboard)}</div>`;
    document.getElementById('btnNext').addEventListener('click', async () => { if (await confirmSkipUnrevealed()) cmd('HostAdvance'); });
    wireReveal();
  }

  function renderPodium(s) {
    stage.className = 'g-main';
    const firstTime = !podiumShown; podiumShown = true;
    const top = s.leaderboard.slice(0, 3);
    const pod = (p, n) => p ? `<div class="pod p${n}">${KG.avatar(p.avatar, 'lg', p.nickname)}<div class="who">${esc(p.nickname)}</div><div class="pts">${fmt(p.score)} xal</div><div class="blk">${n}</div></div>` : '';
    const rest = s.leaderboard.slice(3);
    stage.innerHTML = `<h1 class="g-h1">🏆 Yekun nəticələr</h1>
      ${revealBar(s)}
      ${s.teamLeaderboard?.length ? `<div class="team-lb">${teamRows(s.teamLeaderboard)}</div>` : ''}
      <div class="podium">${pod(top[1], 2)}${pod(top[0], 1)}${pod(top[2], 3)}</div>
      ${rest.length ? `<div class="lb" style="margin-top:22px">${lbRows(rest)}</div>` : ''}
      <div class="row" style="justify-content:center;margin-top:26px">
        <a class="g-btn accent" data-leave href="/Game/Report/${init.gameId}">Hesabata bax</a>
        <a class="g-btn ghost" data-leave href="/Quiz/Index">Kvizlərim</a>
      </div>`;
    document.getElementById('btnEnd').textContent = 'Hesabat';
    wireReveal();
    stage.querySelectorAll('[data-leave]').forEach(a => a.addEventListener('click', async e => {
      if (!state?.hostReveals || state.revealed) return;
      e.preventDefault();
      if (await confirmSkipUnrevealed()) location.href = a.href;
    }));
    if (firstTime) { sfx.play('fanfare'); setTimeout(confetti, 600); }
  }
})();
