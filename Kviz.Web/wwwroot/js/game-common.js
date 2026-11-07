/* Kvizo — oyun ekranları üçün ortaq kitabxana (host, oyunçu, tapşırıq) */
(function () {
  'use strict';

  const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const shape = (i, cls) => `<svg class="shape ${cls || ''}" aria-hidden="true"><use href="#shape-${i % 6}"/></svg>`;
  const fmt = n => Number(n || 0).toLocaleString('az');
  const ordinal = n => n;

  // ---------- Səs effektləri (internet və fayl tələb etmir) ----------
  const sfx = (() => {
    let ctx = null;
    let muted = localStorage.getItem('kviz:mute') === '1';
    const ac = () => {
      if (!ctx) { try { ctx = new (window.AudioContext || window.webkitAudioContext)(); } catch { return null; } }
      if (ctx.state === 'suspended') ctx.resume();
      return ctx;
    };
    function tone(freq, start, dur, type, vol) {
      const a = ac(); if (!a) return;
      const o = a.createOscillator(), g = a.createGain();
      o.type = type || 'sine'; o.frequency.value = freq;
      const t0 = a.currentTime + start;
      g.gain.setValueAtTime(0.0001, t0);
      g.gain.exponentialRampToValueAtTime(vol || 0.18, t0 + 0.02);
      g.gain.exponentialRampToValueAtTime(0.0001, t0 + dur);
      o.connect(g).connect(a.destination);
      o.start(t0); o.stop(t0 + dur + 0.05);
    }
    const sounds = {
      tick: () => tone(880, 0, 0.08, 'square', 0.06),
      start: () => { tone(523, 0, 0.12, 'triangle'); tone(659, 0.12, 0.12, 'triangle'); tone(784, 0.24, 0.25, 'triangle'); },
      join: () => { tone(660, 0, 0.08, 'sine', 0.12); tone(990, 0.08, 0.12, 'sine', 0.12); },
      correct: () => { tone(659, 0, 0.12, 'triangle'); tone(880, 0.12, 0.12, 'triangle'); tone(1175, 0.24, 0.3, 'triangle'); },
      wrong: () => { tone(220, 0, 0.25, 'sawtooth', 0.1); tone(165, 0.2, 0.35, 'sawtooth', 0.1); },
      end: () => tone(440, 0, 0.3, 'sine', 0.12),
      fanfare: () => { [523, 659, 784, 1047, 784, 1047, 1319].forEach((f, i) => tone(f, i * 0.14, 0.22, 'triangle', 0.16)); }
    };
    return {
      play(name) { if (!muted && sounds[name]) { try { sounds[name](); } catch { /* */ } } },
      get muted() { return muted; },
      toggle() { muted = !muted; localStorage.setItem('kviz:mute', muted ? '1' : '0'); return muted; },
      unlock() { ac(); }
    };
  })();

  function addMuteButton() {
    const b = document.createElement('button');
    b.className = 'mute-btn'; b.type = 'button'; b.setAttribute('aria-label', 'Səsi aç/bağla');
    const paint = () => { b.innerHTML = `<svg class="icon"><use href="#${sfx.muted ? 'i-mute' : 'i-volume'}"/></svg>`; };
    b.addEventListener('click', () => { sfx.toggle(); paint(); sfx.unlock(); });
    paint(); document.body.appendChild(b);
  }
  document.addEventListener('click', () => sfx.unlock(), { once: true });
  document.addEventListener('touchstart', () => sfx.unlock(), { once: true, passive: true });

  // ---------- Geri sayım ----------
  /** ms müddətində geri sayım; onTick(qalanMs, nisbət 0..1). Dayandırmaq üçün stop() qaytarır. */
  function countdown(remainingMs, totalMs, onTick, onDone) {
    const end = performance.now() + remainingMs;
    let raf = 0, stopped = false;
    const loop = () => {
      if (stopped) return;
      const left = Math.max(0, end - performance.now());
      onTick(left, totalMs > 0 ? left / totalMs : 0);
      if (left <= 0) { onDone && onDone(); return; }
      raf = requestAnimationFrame(loop);
    };
    loop();
    return { stop() { stopped = true; cancelAnimationFrame(raf); } };
  }

  function confetti() {
    const box = document.createElement('div'); box.className = 'confetti'; document.body.appendChild(box);
    const colors = ['#ff4d63', '#2d7dff', '#ffb400', '#17b26a', '#9a5cff', '#ff5fa8'];
    for (let i = 0; i < 90; i++) {
      const p = document.createElement('i');
      p.style.left = Math.random() * 100 + 'vw'; p.style.background = colors[i % colors.length];
      p.style.animationDuration = 2.4 + Math.random() * 2.6 + 's'; p.style.animationDelay = Math.random() * 1.2 + 's';
      p.style.transform = `rotate(${Math.random() * 360}deg)`;
      box.appendChild(p);
    }
    setTimeout(() => box.remove(), 7500);
  }

  // ---------- Söz buludu ----------
  function wordCloud(words) {
    if (!words || !words.length) return '<div class="g-sub">Hələ cavab yoxdur</div>';
    const max = Math.max(...words.map(w => w.count)), min = Math.min(...words.map(w => w.count));
    const colors = ['#ff6b81', '#5aa2ff', '#ffc933', '#3ddc97', '#c09bff', '#ff8fc4', '#ffffff'];
    return '<div class="wordcloud">' + [...words].sort(() => Math.random() - .5).map((w, i) => {
      const k = max === min ? 0.5 : (w.count - min) / (max - min);
      const size = 1.1 + k * 3.2;
      return `<span style="font-size:${size.toFixed(2)}rem;color:${colors[i % colors.length]}" title="${w.count}">${esc(w.word)}</span>`;
    }).join('') + '</div>';
  }

  // ---------- Avatarlar ----------
  // Format: "e:🐱" (hazır emoji) və ya "/uploads/avatars/….jpg" (oyunçunun yüklədiyi, kiçildilmiş şəkil)
  function avatarColor(s) { let h = 0; for (const ch of s) h = (h * 31 + ch.codePointAt(0)) >>> 0; return h % 6; }
  function avatar(av, cls, nick) {
    cls = 'av ' + (cls || '');
    if (av && av.startsWith('/uploads/avatars/')) return `<span class="${cls}"><img src="${esc(av)}" alt="" loading="lazy" decoding="async"></span>`;
    if (av && av.startsWith('e:')) { const e = av.slice(2); return `<span class="${cls}" style="--avc:var(--c${avatarColor(e)})" aria-hidden="true">${esc(e)}</span>`; }
    const letter = nick ? esc(Array.from(nick)[0].toUpperCase()) : '';
    return `<span class="${cls} none" aria-hidden="true">${letter}</span>`;
  }
  /** Seçilən şəkli 160×160 kvadrata kəsib JPEG edir (adətən 5-20 KB). Orijinal fayl heç vaxt serverə getmir. */
  async function shrinkAvatar(file, side = 160) {
    if (!file || !/^image\//.test(file.type || 'image/jpeg')) throw new Error('Şəkil faylı seçin.');
    let src;
    try { src = await createImageBitmap(file, { imageOrientation: 'from-image' }); }
    catch {
      src = await new Promise((ok, no) => { const im = new Image(); im.onload = () => ok(im); im.onerror = () => no(new Error('Şəkil açıla bilmədi.')); im.src = URL.createObjectURL(file); });
    }
    const w = src.width || src.naturalWidth, h = src.height || src.naturalHeight;
    const s = Math.min(w, h);
    const c = document.createElement('canvas'); c.width = c.height = side;
    const ctx = c.getContext('2d');
    ctx.fillStyle = '#fff'; ctx.fillRect(0, 0, side, side);          // şəffaf PNG üçün ağ fon
    ctx.drawImage(src, (w - s) / 2, (h - s) / 2, s, s, 0, 0, side, side);
    if (src.close) src.close();
    for (const q of [0.82, 0.7, 0.55]) {
      const blob = await new Promise(r => c.toBlob(r, 'image/jpeg', q));
      if (blob && blob.size <= 60 * 1024) return blob;
    }
    throw new Error('Şəkil çox böyük qaldı, başqasını seçin.');
  }

  // ---------- Variantı seçənlərin siyahısı (host üçün; maus üstünə gələndə və ya toxunanda açılır) ----------
  const VOTER_COLORS = ['var(--c0)', 'var(--c1)', 'var(--c2)', 'var(--c3)', 'var(--c4)', 'var(--c5)'];
  const VOTER_CAP = 12;
  function voterNames(list) {
    const shown = list.slice(0, VOTER_CAP).map(esc).join(', ');
    return shown + (list.length > VOTER_CAP ? ` <i>+ ${list.length - VOTER_CAP} nəfər</i>` : '');
  }
  function voterPanel(v, count) {
    if (!v || !count) return '';
    const teams = (v.teams || []).filter(t => t.players.length);
    const body = teams.length
      ? teams.map(t => `<div class="vt" style="--tc:${VOTER_COLORS[t.colorIndex % 6]}"><b>${esc(t.name)} <span>· ${t.players.length}</span></b><div>${voterNames(t.players)}</div></div>`).join('')
      : `<div class="vt plain"><div>${voterNames(v.players || [])}</div></div>`;
    const head = teams.length ? `${teams.length} komanda · ${count} nəfər` : `${count} nəfər seçdi`;
    return `<div class="voters" role="tooltip"><div class="vh">${head}</div>${body}</div>`;
  }
  // Toxunuşlu ekranlarda hover olmur: toxunanda açılır, başqa yerə toxunanda bağlanır
  document.addEventListener('click', e => {
    const bar = e.target.closest && e.target.closest('.rb.has-voters');
    document.querySelectorAll('.rb.has-voters.open').forEach(x => { if (x !== bar) x.classList.remove('open'); });
    if (bar) bar.classList.toggle('open');
  });

  // ---------- Nəticə görünüşü (host nəticələri və tapşırıq cavabı üçün) ----------
  function resultView(r, opts) {
    opts = opts || {};
    const stats = opts.hideStats ? '' : `<div class="g-sub" style="margin-bottom:6px">${r.answered} cavab${r.totalPlayers > 1 ? ' / ' + r.totalPlayers : ''}</div>`;
    switch (r.type) {
      case 'MultipleChoice': case 'TrueFalse': case 'Poll': {
        const max = Math.max(1, ...r.optionCounts);
        const hasCorrect = r.correctIndexes.length > 0;
        const n = r.optionTexts.length;
        return stats + '<div class="result-bars">' + r.optionTexts.map((t, i) => {
          const ok = r.correctIndexes.includes(i);
          const h = Math.max(4, Math.round(r.optionCounts[i] / max * 100));
          const voters = voterPanel(r.optionVoters && r.optionVoters[i], r.optionCounts[i]);
          const edge = n > 2 && i === 0 ? ' edge-l' : n > 2 && i === n - 1 ? ' edge-r' : '';
          return `<div class="rb c${i} ${hasCorrect && !ok ? 'dim' : ''}${voters ? ' has-voters' : ''}${edge}" style="--ac:var(--c${i % 6})"${voters ? ' tabindex="0" role="button" aria-label="Bu variantı seçənləri göstər"' : ''}><div class="num">${r.optionCounts[i]}${ok ? ' ✓' : ''}</div><div class="col" data-h="${h}">${voters}</div><div class="lbl">${shape(i)}<span>${esc(t)}</span></div></div>`;
        }).join('') + '</div>';
      }
      case 'Puzzle':
        return stats + '<div class="correct-banner">Düzgün ardıcıllıq</div><div class="seq-order" style="margin-inline:auto">' + r.correctOrder.map(t => `<div>${esc(t)}</div>`).join('') + `</div><div class="g-sub" style="margin-top:12px">${r.correct} nəfər düzgün düzdü</div>`;
      case 'TypeAnswer':
        return stats + `<div class="correct-banner">Düzgün cavab: ${r.acceptedAnswers.map(esc).join(' / ')}</div>` +
          (r.words.length ? '<div class="g-sub" style="margin-top:14px">Verilən cavablar</div><div class="players-cloud" style="justify-content:center;margin-top:8px">' + r.words.map(w => `<span class="p-chip">${esc(w.word)} <b>×${w.count}</b></span>`).join('') + '</div>' : '');
      case 'Slider': {
        const lo = r.sliderMin, hi = r.sliderMax, span = hi - lo || 1, B = 20;
        const buckets = new Array(B).fill(0);
        r.sliderValues.forEach(v => { buckets[Math.min(B - 1, Math.floor((v - lo) / span * B))]++; });
        const mx = Math.max(1, ...buckets);
        const cLo = r.sliderCorrect - r.sliderTolerance, cHi = r.sliderCorrect + r.sliderTolerance;
        return stats + `<div class="correct-banner">Düzgün dəyər: ${fmt(r.sliderCorrect)}${r.sliderTolerance ? ' (±' + fmt(+r.sliderTolerance.toFixed(2)) + ')' : ''}</div><div class="hist">` +
          buckets.map((c, i) => {
            const bl = lo + span * i / B, bh = lo + span * (i + 1) / B;
            return `<i class="${bh >= cLo && bl <= cHi ? 'in' : ''}" style="height:${Math.round(c / mx * 100)}%" title="${c}"></i>`;
          }).join('') + `</div><div class="hist-axis"><span>${fmt(lo)}</span><span>${fmt(hi)}</span></div>`;
      }
      case 'WordCloud':
        return stats + wordCloud(r.words);
      default:
        return stats + (r.texts.length ? '<div class="open-answers">' + r.texts.map(t => `<div>${esc(t)}</div>`).join('') + '</div>' : '<div class="g-sub">Cavab yoxdur</div>');
    }
  }
  /** Sütunların hündürlüyünü animasiya ilə qaldırır. */
  function animateBars(root) {
    requestAnimationFrame(() => requestAnimationFrame(() =>
      root.querySelectorAll('.col[data-h]').forEach(c => { c.style.height = c.dataset.h + '%'; })));
  }

  // ---------- Oyunçu cavab daxiletməsi (canlı oyun və tapşırıq üçün ortaq) ----------
  /** q: QuestionPublicDto. opts: { hidden: number[], onSubmit(answer) }. Qaytarır: DOM düyünü. */
  function questionInput(q, opts) {
    opts = opts || {};
    const hidden = new Set(opts.hidden || []);
    const root = document.createElement('div');
    root.style.cssText = 'display:flex;flex-direction:column;gap:12px;flex:1;width:100%';
    let sent = false;
    const send = ans => { if (sent) return; sent = true; root.querySelectorAll('button,input,textarea').forEach(x => x.disabled = true); opts.onSubmit(ans); };

    switch (q.type) {
      case 'MultipleChoice': case 'TrueFalse': case 'Poll': {
        const multi = q.type === 'MultipleChoice' && q.multiSelect;
        const grid = document.createElement('div');
        grid.className = 'answers player-answers has-text' + (q.options.length > 4 ? ' cols-1' : '');
        const picked = new Set();
        q.options.forEach((t, i) => {
          const b = document.createElement('button');
          b.type = 'button'; b.className = `ans c${i % 6}` + (hidden.has(i) ? ' hide' : '');
          b.innerHTML = `${shape(i)}<span>${esc(t)}</span>`;
          b.addEventListener('click', () => {
            if (!multi) { send({ choices: [i] }); return; }
            picked.has(i) ? picked.delete(i) : picked.add(i);
            b.classList.toggle('sel', picked.has(i)); go.disabled = picked.size === 0;
          });
          grid.appendChild(b);
        });
        root.appendChild(grid);
        var go = document.createElement('button');
        if (multi) {
          go.type = 'button'; go.className = 'g-btn accent'; go.textContent = 'Göndər'; go.disabled = true;
          go.addEventListener('click', () => send({ choices: [...picked] }));
          const hint = document.createElement('div'); hint.className = 'g-sub'; hint.textContent = 'Bir neçə düzgün cavab var, hamısını seçin';
          root.prepend(hint); root.appendChild(go);
        }
        break;
      }
      case 'Puzzle': {
        const order = [];
        const slots = document.createElement('div'); slots.className = 'seq-pick';
        const pool = document.createElement('div'); pool.className = 'pool';
        const go = document.createElement('button'); go.type = 'button'; go.className = 'g-btn accent'; go.textContent = 'Göndər'; go.disabled = true;
        const draw = () => {
          slots.innerHTML = q.options.map((_, s) => {
            const idx = order[s];
            return idx === undefined ? `<div class="slot"><b>${s + 1}</b><span style="opacity:.5">…</span></div>` : `<div class="slot fill" data-s="${s}"><b>${s + 1}</b><span>${esc(q.options[idx])}</span></div>`;
          }).join('');
          pool.innerHTML = q.options.map((t, i) => `<button type="button" class="${order.includes(i) ? 'used' : ''}" data-i="${i}">${esc(t)}</button>`).join('');
          go.disabled = order.length !== q.options.length;
        };
        slots.addEventListener('click', e => { const s = e.target.closest('[data-s]'); if (s) { order.splice(+s.dataset.s, 1); draw(); } });
        pool.addEventListener('click', e => { const b = e.target.closest('[data-i]'); if (b && order.length < q.options.length) { order.push(+b.dataset.i); draw(); } });
        go.addEventListener('click', () => send({ choices: order }));
        const hint = document.createElement('div'); hint.className = 'g-sub'; hint.textContent = 'Düzgün ardıcıllıqla seçin';
        root.append(hint, slots, pool, go); draw();
        break;
      }
      case 'Slider': {
        const min = q.sliderMin, max = q.sliderMax, step = q.sliderStep || 1;
        const wrap = document.createElement('div'); wrap.className = 'slider-play'; wrap.style.margin = 'auto';
        wrap.innerHTML = `<div class="val">${fmt((min + max) / 2)}</div><input type="range" min="${min}" max="${max}" step="${step}" value="${(min + max) / 2}" aria-label="Cavab"><div class="mm"><span>${fmt(min)}</span><span>${fmt(max)}</span></div>`;
        const rng = wrap.querySelector('input'), val = wrap.querySelector('.val');
        rng.addEventListener('input', () => { val.textContent = fmt(+rng.value); });
        const go = document.createElement('button'); go.type = 'button'; go.className = 'g-btn accent'; go.style.marginTop = '14px'; go.textContent = 'Göndər';
        go.addEventListener('click', () => send({ number: +rng.value }));
        wrap.appendChild(go); root.appendChild(wrap);
        break;
      }
      default: { // TypeAnswer, WordCloud, OpenEnded
        const open = q.type === 'OpenEnded';
        const wrap = document.createElement('div'); wrap.style.cssText = 'margin:auto;width:100%;max-width:520px;display:grid;gap:12px';
        const input = document.createElement(open ? 'textarea' : 'input');
        input.className = 'g-input'; input.maxLength = open ? 300 : q.type === 'WordCloud' ? 25 : 100;
        input.placeholder = q.type === 'WordCloud' ? 'Bir söz yaz…' : open ? 'Fikrini yaz…' : 'Cavabı yaz…';
        input.setAttribute('aria-label', 'Cavab'); input.autocomplete = 'off';
        if (open) { input.rows = 4; input.style.fontSize = '1.1rem'; }
        const go = document.createElement('button'); go.type = 'button'; go.className = 'g-btn accent'; go.textContent = 'Göndər';
        const submit = () => { const v = input.value.trim(); if (v) send({ text: v }); else input.focus(); };
        go.addEventListener('click', submit);
        if (!open) input.addEventListener('keydown', e => { if (e.key === 'Enter') submit(); });
        wrap.append(input, go); root.appendChild(wrap);
        setTimeout(() => input.focus(), 50);
      }
    }
    return root;
  }

  /** Oyunçu ekranı: sualın şəkli və əlavə şəkil ALT-ALTA (əvvəl sualın şəkli), əlavə şəklin müəllifi altında. */
  function playerImages(q) {
    const imgs = [q.imageUrl, q.relatedImageUrl].filter(Boolean);
    if (!imgs.length) return '';
    const h = imgs.length === 2 ? 15 : (q.text ? 22 : 32);
    const mk = u => `<img src="${esc(u)}" alt="" style="max-height:${h}vh;max-width:100%;object-fit:contain;border-radius:14px;background:#fff">`;
    return `<div style="display:flex;flex-direction:column;align-items:center;gap:8px">${imgs.map(mk).join('')}` +
      (q.relatedImageUrl && q.relatedImageCredit ? `<div class="g-credit" style="margin:0;text-align:center">Şəkil: ${esc(q.relatedImageCredit)}</div>` : '') + `</div>`;
  }

  function mkErr(msg) { const d = document.createElement('div'); d.className = 'g-error'; d.textContent = msg; return d; }

  window.KG = { esc, shape, fmt, sfx, addMuteButton, countdown, confetti, wordCloud, resultView, animateBars, questionInput, mkErr, playerImages, avatar, shrinkAvatar };
})();
