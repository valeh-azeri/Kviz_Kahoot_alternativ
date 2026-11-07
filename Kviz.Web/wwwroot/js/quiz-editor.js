/* Kvizo — kviz redaktoru */
(function () {
  'use strict';

  const TYPES = {
    MultipleChoice: { name: 'Test', emoji: '✅', desc: '2–6 variant, bir və ya bir neçə düzgün cavab', scored: true },
    TrueFalse: { name: 'Doğru / Yanlış', emoji: '⚖️', desc: 'İki variantlı sürətli sual', scored: true },
    TypeAnswer: { name: 'Cavabı yaz', emoji: '⌨️', desc: 'Oyunçu cavabı özü yazır', scored: true },
    Slider: { name: 'Sürüşdürücü', emoji: '🎚️', desc: 'Rəqəm aralığında düzgün dəyəri tap', scored: true },
    Puzzle: { name: 'Puzzle', emoji: '🧩', desc: 'Elementləri düzgün ardıcıllıqla düz', scored: true },
    Poll: { name: 'Sorğu', emoji: '📊', desc: 'Düzgün cavabı olmayan rəy sorğusu', scored: false },
    WordCloud: { name: 'Söz buludu', emoji: '☁️', desc: 'Hamı bir söz yazır, bulud yaranır', scored: false },
    OpenEnded: { name: 'Açıq sual', emoji: '💬', desc: 'Sərbəst mətn cavabları', scored: false }
  };
  const TIMES = [5, 10, 20, 30, 45, 60, 90, 120, 240];
  const MAX_OPTS = 6;
  const TF_TEXT = ['Doğru', 'Yanlış'];

  const raw = JSON.parse(document.getElementById('quiz-data').textContent);
  const cats = JSON.parse(document.getElementById('cat-data').textContent);

  let uid = 0;
  const state = {
    id: raw.id || null, title: raw.title || '', description: raw.description || '', coverImageUrl: raw.coverImageUrl || '',
    categoryId: raw.categoryId || '', language: raw.language || 'az', isPublic: !!raw.isPublic,
    questions: (raw.questions || []).map(withKey)
  };
  let sel = 0, dirty = false, errors = {}, errorList = [];
  if (!state.questions.length) state.questions.push(withKey(newQuestion('MultipleChoice')));

  function withKey(q) { q._k = ++uid; q.options = q.options || []; return q; }
  function newQuestion(type, base) {
    const q = Object.assign({ type, text: '', imageUrl: '', relatedImageUrl: '', relatedImageCredit: '', timeLimitSeconds: 20, pointsMode: 'Standard', multiSelect: false,
      sliderMin: null, sliderMax: null, sliderStep: null, sliderCorrect: null, sliderTolerancePercent: 0, options: [] }, base || {});
    q.type = type;
    q.multiSelect = false;
    const blank = n => Array.from({ length: n }, () => ({ text: '', isCorrect: false }));
    switch (type) {
      case 'MultipleChoice': q.options = blank(4); break;
      case 'TrueFalse': q.options = [{ text: TF_TEXT[0], isCorrect: true }, { text: TF_TEXT[1], isCorrect: false }]; q.timeLimitSeconds = Math.min(q.timeLimitSeconds, 20); break;
      case 'TypeAnswer': q.options = [{ text: '', isCorrect: true }]; break;
      case 'Slider': Object.assign(q, { sliderMin: 0, sliderMax: 100, sliderStep: 1, sliderCorrect: 50, sliderTolerancePercent: 5, options: [] }); break;
      case 'Puzzle': q.options = blank(4).map(o => ({ text: '', isCorrect: true })); break;
      case 'Poll': q.options = blank(4); q.pointsMode = 'NoPoints'; break;
      default: q.options = []; q.pointsMode = 'NoPoints';
    }
    if (TYPES[type].scored && q.pointsMode === 'NoPoints') q.pointsMode = 'Standard';
    return q;
  }

  // ---------- DOM köməkçisi ----------
  function el(tag, attrs, ...kids) {
    const n = document.createElement(tag);
    for (const [k, v] of Object.entries(attrs || {})) {
      if (v === false || v == null) continue;
      if (k === 'class') n.className = v;
      else if (k === 'text') n.textContent = v;
      else if (k.startsWith('on')) n.addEventListener(k.slice(2), v);
      else if (k === 'style') n.setAttribute('style', v);
      else n.setAttribute(k, v === true ? '' : v);
    }
    for (const kid of kids.flat()) if (kid != null && kid !== false) n.append(kid.nodeType ? kid : document.createTextNode(kid));
    return n;
  }
  const svg = (id, cls) => { const s = document.createElementNS('http://www.w3.org/2000/svg', 'svg'); s.setAttribute('class', cls || 'icon'); const u = document.createElementNS('http://www.w3.org/2000/svg', 'use'); u.setAttribute('href', '#' + id); s.appendChild(u); return s; };
  const $ = id => document.getElementById(id);
  const cur = () => state.questions[sel];
  const qLabel = q => q.text || (q.imageUrl || q.relatedImageUrl ? '🖼 Şəkilli sual' : '(sual mətni yoxdur)');
  const touch = () => { dirty = true; };

  // ---------- Sol siyahı ----------
  function renderList() {
    const box = $('edItems');
    const scroll = box.scrollTop;
    box.replaceChildren();
    state.questions.forEach((q, i) => {
      const item = el('button', { type: 'button', class: 'qi' + (i === sel ? ' active' : '') + (errors[i] ? ' has-error' : ''), draggable: 'true', 'data-i': i,
        onclick: () => { sel = i; renderAll(); } },
        el('div', { class: 'n', text: i + 1 }),
        el('div', {}, el('div', { class: 't', text: qLabel(q) }), el('div', { class: 'ty', text: TYPES[q.type].emoji + ' ' + TYPES[q.type].name })),
        errors[i] ? el('span', { class: 'err-dot', title: 'Bu sualda səhv var' }) : null);
      item.addEventListener('dragstart', e => { e.dataTransfer.setData('text/plain', String(i)); item.classList.add('dragging'); });
      item.addEventListener('dragend', () => item.classList.remove('dragging'));
      item.addEventListener('dragover', e => { e.preventDefault(); item.classList.add('drag-over'); });
      item.addEventListener('dragleave', () => item.classList.remove('drag-over'));
      item.addEventListener('drop', e => {
        e.preventDefault(); item.classList.remove('drag-over');
        const from = Number(e.dataTransfer.getData('text/plain'));
        if (!Number.isNaN(from) && from !== i) moveQuestion(from, i);
      });
      box.appendChild(item);
    });
    box.scrollTop = scroll;
  }
  function moveQuestion(from, to) {
    const [q] = state.questions.splice(from, 1);
    state.questions.splice(to, 0, q);
    if (sel === from) sel = to; else if (from < sel && to >= sel) sel--; else if (from > sel && to <= sel) sel++;
    errors = {}; touch(); renderAll();
  }

  // ---------- Mərkəz ----------
  function renderStage() {
    const q = cur(); const stage = $('edStage');
    stage.replaceChildren();
    if (errorList.length) {
      stage.appendChild(el('div', { class: 'err-list', role: 'alert' }, el('b', { text: 'Yadda saxlamaq üçün bunları düzəldin:' }), el('ul', {}, errorList.slice(0, 8).map(e => el('li', { text: e })))));
    }
    const ta = el('textarea', { class: 'q-text' + (hasErr(sel, 'text') ? ' is-invalid' : ''), maxlength: 500, placeholder: 'Sualı yazın və ya şəkli bura Ctrl+V ilə yapışdırın…', rows: 2, 'aria-label': 'Sualın mətni' });
    ta.value = q.text;
    ta.addEventListener('input', () => { q.text = ta.value; touch(); updateListText(); });
    stage.appendChild(ta);

    // 1) SUALIN ŞƏKLİ: sizin öz şəkliniz (Ctrl+V ilə yapışdırılmış, yüklənmiş, ekrandan kəsilmiş). Sualın özü ola bilər.
    stage.appendChild(el('div', { class: 'img-slot' },
      el('div', { class: 'slot-title', text: '🖼 Sualın şəkli' }),
      q.imageUrl
        ? el('div', { class: 'media-box' },
            el('img', { src: q.imageUrl, alt: '' }),
            el('div', { class: 'row', style: 'justify-content:center;margin-top:10px' },
              el('button', { type: 'button', class: 'btn secondary sm', onclick: () => $('qImageFile').click() }, svg('i-upload'), 'Dəyiş'),
              el('button', { type: 'button', class: 'btn secondary sm', onclick: pasteFromClipboard }, svg('i-copy'), 'Yapışdır'),
              el('button', { type: 'button', class: 'btn danger sm', onclick: () => { q.imageUrl = ''; touch(); renderStage(); renderList(); } }, svg('i-trash'), 'Sil')))
        : el('div', { class: 'center' },
            el('div', { class: 'row', style: 'justify-content:center' },
              el('button', { type: 'button', class: 'btn secondary sm', onclick: () => $('qImageFile').click() }, svg('i-image'), 'Şəkil yüklə'),
              el('button', { type: 'button', class: 'btn secondary sm', onclick: pasteFromClipboard }, svg('i-copy'), 'Yapışdır')),
            el('div', { class: 'muted', style: 'font-size:.82rem;margin-top:6px', text: 'Sualı şəkil kimi qurmaq üçün: Word, PDF və ya ekranın istədiyiniz hissəsini kəsin (Win+Shift+S) və yuxarıdakı yazı sahəsinə Ctrl+V basın. Faylı bura sürükləmək də olar.' }))));

    // 2) ƏLAVƏ ŞƏKİL: sualla bağlı illüstrasiya, adətən internetdən. Sualın şəklini əvəz etmir, onun ALTINDA göstərilir.
    stage.appendChild(el('div', { class: 'img-slot' },
      el('div', { class: 'slot-title', text: '🌐 Əlavə şəkil (sualla bağlı, istəyə bağlı)' }),
      q.relatedImageUrl
        ? el('div', { class: 'media-box' },
            el('img', { src: q.relatedImageUrl, alt: '' }),
            q.relatedImageCredit ? el('div', { class: 'credit', text: 'Şəkil: ' + q.relatedImageCredit }) : null,
            el('div', { class: 'row', style: 'justify-content:center;margin-top:10px' },
              el('button', { type: 'button', class: 'btn secondary sm', onclick: () => openImageSearch() }, svg('i-globe'), 'İnternetdən dəyiş'),
              el('button', { type: 'button', class: 'btn secondary sm', onclick: () => $('rImageFile').click() }, svg('i-upload'), 'Kompüterdən'),
              el('button', { type: 'button', class: 'btn danger sm', onclick: () => { q.relatedImageUrl = ''; q.relatedImageCredit = ''; touch(); renderStage(); } }, svg('i-trash'), 'Sil')))
        : el('div', { class: 'center' },
            el('div', { class: 'row', style: 'justify-content:center' },
              el('button', { type: 'button', class: 'btn secondary sm', onclick: () => openImageSearch() }, svg('i-globe'), 'İnternetdən tap'),
              el('button', { type: 'button', class: 'btn secondary sm', onclick: () => $('rImageFile').click() }, svg('i-upload'), 'Kompüterdən')),
            el('div', { class: 'muted', style: 'font-size:.82rem;margin-top:6px', text: 'Suala aid illüstrasiya. Sualın şəklinin altında göstərilir. Cavabınıza və ya sualınıza görə internetdən özü tapa bilər.' }))));

    stage.appendChild(renderBody(q));
  }
  const hasErr = (i, part) => (errors[i] || []).some(p => p.includes(part));

  function renderBody(q) {
    switch (q.type) {
      case 'MultipleChoice': case 'Poll': return renderOptions(q);
      case 'TrueFalse': return renderTF(q);
      case 'TypeAnswer': return renderTypeAnswer(q);
      case 'Slider': return renderSlider(q);
      case 'Puzzle': return renderPuzzle(q);
      case 'WordCloud': return el('div', { class: 'hint-box' }, '☁️ Oyunçular telefonlarında qısa söz və ya ifadə yazır. Ən çox yazılanlar bulud şəklində böyük görünür. Variant tələb olunmur.');
      default: return el('div', { class: 'hint-box' }, '💬 Oyunçular sərbəst mətn cavabı yazır, host bütün cavabları ekranda görür. Variant tələb olunmur.');
    }
  }

  function renderOptions(q) {
    const poll = q.type === 'Poll';
    const grid = el('div', { class: 'opt-grid' });
    q.options.forEach((o, i) => {
      const input = el('input', { class: 'o-text', type: 'text', maxlength: 200, placeholder: 'Variant ' + (i + 1) + (!poll && i >= 2 ? ' (istəyə bağlı)' : ''), 'aria-label': 'Variant ' + (i + 1) });
      input.value = o.text;
      input.addEventListener('input', () => { o.text = input.value; touch(); });
      const row = el('div', { class: 'opt' + (hasErr(sel, 'options') && !o.text.trim() && i < 2 ? ' is-invalid' : ''), style: '--oc: var(--c' + i + ')' },
        svg('shape-' + i, 'shape'), input,
        poll ? el('span') : el('button', { type: 'button', class: 'ok-toggle' + (o.isCorrect ? ' on' : ''), title: 'Düzgün cavab', 'aria-pressed': o.isCorrect ? 'true' : 'false',
          onclick: () => {
            if (q.multiSelect) o.isCorrect = !o.isCorrect;
            else q.options.forEach((x, j) => x.isCorrect = j === i);
            touch(); renderStage();
          } }, svg('i-check')),
        q.options.length > 2 ? el('button', { type: 'button', class: 'rm-opt', title: 'Variantı sil', onclick: () => { q.options.splice(i, 1); touch(); renderStage(); } }, svg('i-x')) : null);
      grid.appendChild(row);
    });
    const wrap = el('div', {}, grid);
    if (q.options.length < MAX_OPTS) {
      wrap.appendChild(el('div', { class: 'center', style: 'margin-top:14px' },
        el('button', { type: 'button', class: 'btn secondary sm', onclick: () => { q.options.push({ text: '', isCorrect: false }); touch(); renderStage(); } }, svg('i-plus'), 'Variant əlavə et')));
    }
    if (!poll) wrap.appendChild(el('div', { class: 'muted center', style: 'margin-top:10px;font-size:.88rem' }, q.multiSelect ? 'Bir neçə düzgün cavab seçə bilərsiniz (hamısı seçilməlidir).' : 'Düzgün cavabı dairəyə basaraq seçin.'));
    return wrap;
  }

  function renderTF(q) {
    const grid = el('div', { class: 'opt-grid' });
    q.options.forEach((o, i) => {
      grid.appendChild(el('div', { class: 'opt tf static', style: '--oc: var(--c' + (i === 0 ? 1 : 0) + ')' },
        svg('shape-' + (i === 0 ? 1 : 0), 'shape'), el('div', { class: 'tf-label', text: TF_TEXT[i] }),
        el('button', { type: 'button', class: 'ok-toggle' + (o.isCorrect ? ' on' : ''), title: 'Düzgün cavab', 'aria-pressed': o.isCorrect ? 'true' : 'false',
          onclick: () => { q.options.forEach((x, j) => x.isCorrect = j === i); touch(); renderStage(); } }, svg('i-check'))));
    });
    return el('div', {}, grid, el('div', { class: 'muted center', style: 'margin-top:10px;font-size:.88rem' }, 'Hansı cavabın düzgün olduğunu dairəyə basaraq seçin.'));
  }

  function renderTypeAnswer(q) {
    const list = el('div', { class: 'puzzle-list' });
    q.options.forEach((o, i) => {
      const input = el('input', { type: 'text', maxlength: 200, placeholder: i === 0 ? 'Düzgün cavab' : 'Alternativ düzgün cavab', 'aria-label': 'Qəbul olunan cavab ' + (i + 1) });
      input.value = o.text;
      input.addEventListener('input', () => { o.text = input.value; touch(); });
      list.appendChild(el('div', { class: 'puzzle-item' + (hasErr(sel, 'options') && !o.text.trim() ? ' is-invalid' : '') },
        el('span', { class: 'pn', style: 'background:var(--c3)' }, svg('i-check')), input,
        q.options.length > 1 ? el('button', { type: 'button', class: 'btn ghost icon-only sm', title: 'Sil', onclick: () => { q.options.splice(i, 1); touch(); renderStage(); } }, svg('i-x')) : el('span')));
    });
    const wrap = el('div', {}, list);
    if (q.options.length < 4) wrap.appendChild(el('div', { class: 'center', style: 'margin-top:14px' },
      el('button', { type: 'button', class: 'btn secondary sm', onclick: () => { q.options.push({ text: '', isCorrect: true }); touch(); renderStage(); } }, svg('i-plus'), 'Alternativ cavab əlavə et')));
    wrap.appendChild(el('div', { class: 'hint-box' }, 'Böyük-kiçik hərf və artıq boşluqlar nəzərə alınmır. I, İ və ı hərfləri eyni sayılır. Eyni cavabın fərqli yazılışlarını alternativ kimi əlavə edin.'));
    return wrap;
  }

  function renderPuzzle(q) {
    const list = el('div', { class: 'puzzle-list' });
    q.options.forEach((o, i) => {
      const input = el('input', { type: 'text', maxlength: 200, placeholder: i + 1 + '-ci element', 'aria-label': i + 1 + '-ci element' });
      input.value = o.text;
      input.addEventListener('input', () => { o.text = input.value; touch(); });
      list.appendChild(el('div', { class: 'puzzle-item' + (hasErr(sel, 'options') && !o.text.trim() ? ' is-invalid' : '') },
        el('span', { class: 'pn', text: i + 1 }), input,
        el('div', { class: 'row', style: 'gap:2px;flex-wrap:nowrap' },
          el('button', { type: 'button', class: 'btn ghost icon-only sm', title: 'Yuxarı', disabled: i === 0, onclick: () => { [q.options[i - 1], q.options[i]] = [q.options[i], q.options[i - 1]]; touch(); renderStage(); } }, svg('i-up')),
          el('button', { type: 'button', class: 'btn ghost icon-only sm', title: 'Aşağı', disabled: i === q.options.length - 1, onclick: () => { [q.options[i + 1], q.options[i]] = [q.options[i], q.options[i + 1]]; touch(); renderStage(); } }, svg('i-down')),
          q.options.length > 2 ? el('button', { type: 'button', class: 'btn ghost icon-only sm', title: 'Sil', onclick: () => { q.options.splice(i, 1); touch(); renderStage(); } }, svg('i-x')) : null)));
    });
    const wrap = el('div', {}, list);
    if (q.options.length < MAX_OPTS) wrap.appendChild(el('div', { class: 'center', style: 'margin-top:14px' },
      el('button', { type: 'button', class: 'btn secondary sm', onclick: () => { q.options.push({ text: '', isCorrect: true }); touch(); renderStage(); } }, svg('i-plus'), 'Element əlavə et')));
    wrap.appendChild(el('div', { class: 'hint-box' }, 'Elementləri DÜZGÜN ardıcıllıqla yazın. Oyunda onlar qarışdırılıb göstəriləcək.'));
    return wrap;
  }

  function renderSlider(q) {
    const num = (key, label, step) => {
      const inp = el('input', { type: 'number', class: 'input', step: step || 'any', 'aria-label': label });
      inp.value = q[key] ?? '';
      inp.addEventListener('input', () => { q[key] = inp.value === '' ? null : Number(inp.value); touch(); preview(); });
      return el('div', { class: 'field', style: 'margin:0' }, el('label', { text: label }), inp);
    };
    const tol = el('select', { class: 'input', 'aria-label': 'Dəqiqlik' }, [[0, 'Dəqiq'], [5, '±5%'], [10, '±10%'], [20, '±20%'], [30, '±30%']].map(([v, t]) => el('option', { value: v, text: t })));
    tol.value = String(q.sliderTolerancePercent);
    tol.addEventListener('change', () => { q.sliderTolerancePercent = Number(tol.value); touch(); });
    const prev = el('div', { class: 'center', style: 'margin-top:18px' });
    function preview() {
      prev.replaceChildren();
      if (q.sliderMin == null || q.sliderMax == null || q.sliderMin >= q.sliderMax) return;
      const r = el('input', { type: 'range', min: q.sliderMin, max: q.sliderMax, step: q.sliderStep || 1, style: 'width:100%;max-width:520px;accent-color:var(--brand)' });
      r.value = q.sliderCorrect ?? q.sliderMin;
      prev.append(el('div', { class: 'muted', text: 'Düzgün dəyər: ' + (q.sliderCorrect ?? '—') }), r,
        el('div', { class: 'row between', style: 'max-width:520px;margin:0 auto;font-weight:800' }, el('span', { text: q.sliderMin }), el('span', { text: q.sliderMax })));
    }
    preview();
    return el('div', {}, el('div', { class: 'slider-cfg' }, num('sliderMin', 'Minimum'), num('sliderMax', 'Maksimum'), num('sliderStep', 'Addım'), num('sliderCorrect', 'Düzgün dəyər'),
      el('div', { class: 'field', style: 'margin:0' }, el('label', { text: 'Dəqiqlik həddi' }), tol)), prev,
      hasErr(sel, 'slider') ? el('div', { class: 'field-error center', style: 'margin-top:10px', text: 'Sürüşdürücü məlumatlarını yoxlayın.' }) : null);
  }

  // ---------- Sağ panel ----------
  function renderSide() {
    const q = cur(); const side = $('edSide'); side.replaceChildren();
    const panel = el('div', { class: 'side-panel' });
    panel.append(el('h4', { text: 'Sualın növü' }),
      el('button', { type: 'button', class: 'type-btn', style: 'width:100%;margin-bottom:16px', onclick: () => openTypePicker(true) },
        el('span', { class: 'e', text: TYPES[q.type].emoji }), el('span', {}, el('b', { text: TYPES[q.type].name }), el('small', { text: 'Dəyişmək üçün basın' }))));

    panel.append(el('h4', { text: 'Vaxt limiti' }));
    const tg = el('div', { class: 'seg', style: 'grid-auto-flow:row;grid-template-columns:repeat(3,1fr);margin-bottom:16px' });
    TIMES.forEach(t => tg.appendChild(el('button', { type: 'button', class: q.timeLimitSeconds === t ? 'on' : '', text: (t < 60 || t === 90) ? t + ' san' : (t / 60) + ' dəq', onclick: () => { q.timeLimitSeconds = t; touch(); renderSide(); } })));
    panel.append(tg);

    if (TYPES[q.type].scored) {
      panel.append(el('h4', { text: 'Xal' }));
      const pg = el('div', { class: 'seg', style: 'margin-bottom:16px' });
      [['Standard', 'Standart'], ['Double', '2× xal'], ['NoPoints', 'Xalsız']].forEach(([v, t]) =>
        pg.appendChild(el('button', { type: 'button', class: q.pointsMode === v ? 'on' : '', text: t, onclick: () => { q.pointsMode = v; touch(); renderSide(); } })));
      panel.append(pg);
    }
    if (q.type === 'MultipleChoice') {
      panel.append(el('label', { class: 'check', style: 'margin-bottom:16px' },
        el('input', { type: 'checkbox', ...(q.multiSelect ? { checked: true } : {}), onchange: e => {
          q.multiSelect = e.target.checked;
          if (!q.multiSelect) { let seen = false; q.options.forEach(o => { if (o.isCorrect && !seen) seen = true; else o.isCorrect = false; }); }
          touch(); renderStage();
        } }), el('span', {}, 'Bir neçə düzgün cavab', el('small', { text: 'Oyunçu hamısını seçməlidir' }))));
    }

    panel.append(el('div', { class: 'row', style: 'gap:8px' },
      el('button', { type: 'button', class: 'btn secondary sm', onclick: duplicateQuestion }, svg('i-copy'), 'Kopyala'),
      el('button', { type: 'button', class: 'btn secondary sm', disabled: sel === 0, title: 'Yuxarı', onclick: () => moveQuestion(sel, sel - 1) }, svg('i-up')),
      el('button', { type: 'button', class: 'btn secondary sm', disabled: sel === state.questions.length - 1, title: 'Aşağı', onclick: () => moveQuestion(sel, sel + 1) }, svg('i-down')),
      el('button', { type: 'button', class: 'btn danger sm', onclick: deleteQuestion }, svg('i-trash'), 'Sil')));
    side.append(panel);
  }

  function updateListText() {
    const item = $('edItems').children[sel];
    if (item) item.querySelector('.t').textContent = qLabel(cur());
  }

  // ---------- Əməliyyatlar ----------
  function duplicateQuestion() {
    const copy = JSON.parse(JSON.stringify(cur())); delete copy._k; withKey(copy);
    state.questions.splice(sel + 1, 0, copy); sel++; errors = {}; touch(); renderAll();
  }
  async function deleteQuestion() {
    if (state.questions.length === 1) { Kviz.toast('Kvizdə ən azı bir sual qalmalıdır.', 'error'); return; }
    if (!(await Kviz.confirm('Sual silinsin?', 'Bu sual kvizdən çıxarılacaq.', 'Sil'))) return;
    state.questions.splice(sel, 1); sel = Math.min(sel, state.questions.length - 1); errors = {}; touch(); renderAll();
  }
  function openTypePicker(changing) {
    const grid = $('typeGrid'); grid.replaceChildren();
    Object.entries(TYPES).forEach(([key, t]) => grid.appendChild(el('button', { type: 'button', class: 'type-btn', onclick: () => {
      $('dlgType').close();
      if (changing) {
        const q = cur();
        if (q.type === key) return;
        const fresh = newQuestion(key, { text: q.text, imageUrl: q.imageUrl, relatedImageUrl: q.relatedImageUrl, relatedImageCredit: q.relatedImageCredit, timeLimitSeconds: q.timeLimitSeconds });
        fresh._k = q._k; state.questions[sel] = fresh;
      } else {
        state.questions.push(withKey(newQuestion(key))); sel = state.questions.length - 1;
      }
      errors = {}; touch(); renderAll();
      if (!changing) document.querySelector('.q-text')?.focus();
    } }, el('span', { class: 'e', text: t.emoji }), el('span', {}, el('b', { text: t.name }), el('small', { text: t.desc })))));
    $('dlgType').showModal();
  }

  // ---------- Şəkil yükləmə ----------
  async function uploadImage(file) {
    if (!file) return null;
    const fd = new FormData(); fd.append('file', file);
    const res = await fetch('/Quiz/UploadImage', { method: 'POST', headers: { 'RequestVerificationToken': Kviz.csrf() }, body: fd, credentials: 'same-origin' });
    let data = null; try { data = await res.json(); } catch { /* */ }
    if (!res.ok || !data?.ok) { Kviz.toast(data?.error || 'Şəkil yüklənmədi.', 'error'); return null; }
    return data.url;
  }
  $('qImageFile').addEventListener('change', async e => {
    const url = await uploadImage(e.target.files[0]); e.target.value = '';
    if (url) { setImage(cur(), url); }
  });
  // Əlavə şəkli kompüterdən yükləmək (internetdən gələn kimi sualın şəklinin altında göstərilir)
  $('rImageFile').addEventListener('change', async e => {
    const file = e.target.files[0]; e.target.value = '';
    if (!file) return;
    const url = await uploadImage(await shrinkImage(file));
    if (url) setRelated(cur(), url, '');
  });

  // ---------- Şəkil: təyin et, yapışdır, sürüklə ----------
  function setImage(q, url) { q.imageUrl = url; touch(); renderStage(); renderList(); }
  function setRelated(q, url, credit) { q.relatedImageUrl = url; q.relatedImageCredit = credit || ''; touch(); renderStage(); renderList(); }

  /** Böyük ekran görüntülərini kiçildir: tez yüklənsin və 5 MB limitinə düşməsin. Animasiyalı GIF toxunulmaz qalır. */
  async function shrinkImage(file) {
    if (file.type === 'image/gif') return file;
    try {
      const bmp = await createImageBitmap(file);
      const scale = Math.min(1, 1600 / Math.max(bmp.width, bmp.height));
      if (scale === 1 && file.size < 1.5 * 1024 * 1024) { bmp.close?.(); return file; }
      const w = Math.round(bmp.width * scale), h = Math.round(bmp.height * scale);
      const cv = document.createElement('canvas'); cv.width = w; cv.height = h;
      const ctx = cv.getContext('2d'); ctx.fillStyle = '#fff'; ctx.fillRect(0, 0, w, h); ctx.drawImage(bmp, 0, 0, w, h); bmp.close?.();
      let blob = await new Promise(r => cv.toBlob(r, 'image/png'));
      if (blob && blob.size > 1.5 * 1024 * 1024) blob = await new Promise(r => cv.toBlob(r, 'image/jpeg', 0.88));
      return blob ? new File([blob], blob.type === 'image/jpeg' ? 'sual.jpg' : 'sual.png', { type: blob.type }) : file;
    } catch { return file; }
  }

  async function handleImageFile(file) {
    if (!file || !file.type.startsWith('image/')) { Kviz.toast('Yalnız şəkil faylı qəbul olunur.', 'error'); return; }
    Kviz.toast('Şəkil yüklənir…');
    const url = await uploadImage(await shrinkImage(file));
    if (url) { setImage(cur(), url); Kviz.toast('Şəkil sualın şəkli kimi əlavə edildi ✓', 'success'); }
  }

  function imageFromClipboard(cd) {
    if (!cd) return null;
    const files = [...(cd.files || [])].filter(f => f.type.startsWith('image/'));
    if (files.length) return files[0];
    for (const it of cd.items || []) {
      if (it.kind === 'file' && it.type.startsWith('image/')) { const f = it.getAsFile(); if (f) return f; }
    }
    return null;
  }

  // Ekrandan kəsilmiş şəkil və ya kopyalanmış şəkil Ctrl+V ilə yapışdırılır: sual yazdığınız mətn sahəsində də,
  // səhifənin istənilən yerində də işləyir. Şəkil internetdəki heç bir şəklə bağlı deyil, sizin öz şəklinizdir.
  document.addEventListener('paste', e => {
    const file = imageFromClipboard(e.clipboardData);
    if (!file) return;
    // Mübadilə buferində mətn də varsa (məsələn Excel-dən kopyalanıb), mətn adi qaydada yapışsın
    if ((e.clipboardData.getData('text/plain') || '').trim().length > 0) return;
    e.preventDefault();
    handleImageFile(file);
  });

  // "Yapışdır" düyməsi (HTTPS və ya localhost-da). Digər hallarda Ctrl+V istifadə olunur.
  async function pasteFromClipboard() {
    if (!navigator.clipboard || !navigator.clipboard.read) { Kviz.toast('Bu ünvanda düymə işləmir. Klaviaturada Ctrl+V basın.', 'error'); return; }
    try {
      for (const item of await navigator.clipboard.read()) {
        const type = item.types.find(t => t.startsWith('image/'));
        if (type) { await handleImageFile(new File([await item.getType(type)], 'yapisdirildi.png', { type })); return; }
      }
      Kviz.toast('Mübadilə buferində şəkil yoxdur. Əvvəl ekranı kəsin (Win+Shift+S).', 'error');
    } catch { Kviz.toast('Yapışdırmağa icazə verilmədi. Klaviaturada Ctrl+V basın.', 'error'); }
  }
  const stageEl = $('edStage');
  ['dragenter', 'dragover'].forEach(ev => stageEl.addEventListener(ev, e => {
    if ([...(e.dataTransfer?.types || [])].includes('Files')) { e.preventDefault(); stageEl.classList.add('stage-drop'); }
  }));
  ['dragleave', 'drop'].forEach(ev => stageEl.addEventListener(ev, () => stageEl.classList.remove('stage-drop')));
  stageEl.addEventListener('drop', e => { const f = e.dataTransfer?.files?.[0]; if (f) { e.preventDefault(); handleImageFile(f); } });

  // ---------- Şəkil: internetdən tap ----------
  let imgFor = null;
  function correctAnswers(q) {
    return ['MultipleChoice', 'TypeAnswer'].includes(q.type) ? q.options.filter(o => o.isCorrect && o.text.trim()).map(o => o.text.trim()) : [];
  }
  function searchImages(q, manual) {
    return Kviz.post('/Quiz/ImageSearch', { query: manual || null, text: q.text, answers: correctAnswers(q) });
  }
  function openImageSearch() {
    imgFor = cur();
    $('imgQuery').value = '';
    $('imgGrid').replaceChildren(); $('imgStatus').textContent = ''; $('imgMore').hidden = true;
    $('dlgImg').showModal();
    runImageSearch(1); // boş sorğu: sualdan avtomatik təkliflər
  }

  let imgPage = 1, imgQueryUsed = '';
  /** Boş axtarış qutusu: sualın cavabı və açar sözlərindən təkliflər. Söz yazılıbsa: YALNIZ həmin söz (Openverse + Wikimedia), səhifələmə ilə. */
  async function runImageSearch(page) {
    const status = $('imgStatus'), grid = $('imgGrid'), more = $('imgMore');
    const manual = $('imgQuery').value.trim();
    const append = page > 1;
    if (!append) { grid.replaceChildren(); imgQueryUsed = manual; }
    more.hidden = true;
    status.textContent = 'Axtarılır…';
    const body = { query: manual || null, text: imgFor.text, answers: correctAnswers(imgFor), page: page || 1 };
    const r = await Kviz.post('/Quiz/ImageSearch', body);
    if (!r.ok || !r.data) { status.textContent = r.status === 429 ? 'Çox sayda sorğu, bir az gözləyin.' : 'Axtarış alınmadı.'; return; }
    if (!r.data.ok) { status.textContent = r.data.error; return; }
    const list = r.data.results;
    imgPage = page || 1;
    if (!list.length && !append) { status.textContent = manual ? '“' + manual + '” üçün heç nə tapılmadı. Başqa söz yazın (məsələn, ingiliscə).' : 'Heç nə tapılmadı. Yuxarıdakı qutuya axtarış sözü yazın.'; return; }
    const total = grid.children.length + list.length;
    status.textContent = total + ' şəkil' + (manual ? ' (“' + manual + '”)' : ' (sualdan təkliflər)') + '. Uyğun olana basın.' + (r.data.notice ? ' ' + r.data.notice : '');
    list.forEach(x => {
      const card = el('button', { type: 'button', class: 'img-card', title: x.credit },
        el('img', { src: x.thumbUrl, alt: '', loading: 'lazy' }),
        x.source ? el('span', { class: 'src ' + x.source.toLowerCase(), text: x.source }) : null,
        el('small', { text: x.credit }));
      card.addEventListener('click', async () => {
        card.classList.add('busy');
        const imp = await Kviz.post('/Quiz/ImageImport', { url: x.thumbUrl });
        card.classList.remove('busy');
        if (imp.data && imp.data.ok) { setRelated(imgFor, imp.data.url, x.credit); $('dlgImg').close(); Kviz.toast('Əlavə şəkil əlavə edildi ✓', 'success'); }
        else Kviz.toast(imp.data?.error || 'Şəkil yüklənmədi.', 'error');
      });
      grid.appendChild(card);
    });
    // "Daha çox göstər" yalnız əl ilə yazılmış söz üçün (Google kimi səhifələmə)
    more.hidden = !(manual && r.data.hasMore);
  }
  $('imgForm').addEventListener('submit', e => { e.preventDefault(); runImageSearch(1); });
  $('imgMore').addEventListener('click', () => runImageSearch(imgPage + 1));

  // Şəkilsiz bütün suallara ən uyğun şəkli avtomatik gətir
  $('btnAutoImages').addEventListener('click', async () => {
    const targets = state.questions.filter(q => !q.relatedImageUrl && (q.text.trim() || correctAnswers(q).length));
    if (!targets.length) { Kviz.toast('Əlavə şəkil tapılacaq sual yoxdur (sualda mətn və ya cavab olmalıdır, əlavə şəkli olanlara toxunulmur).', 'error'); return; }
    const ok = await Kviz.confirm('Şəkillər avtomatik əlavə edilsin?',
      targets.length + ' sual üçün internetdən ən uyğun şəkil gətirilib ƏLAVƏ şəkil kimi qoyulacaq. Sualların öz şəkilləri dəyişmir. Sonra hər birini yoxlayıb dəyişə və ya silə bilərsiniz.', 'Davam et', false);
    if (!ok) return;
    const btn = $('btnAutoImages'), original = btn.innerHTML;
    btn.disabled = true;
    let added = 0, offline = false;
    for (let i = 0; i < targets.length; i++) {
      btn.textContent = 'Axtarılır ' + (i + 1) + '/' + targets.length + '…';
      const r = await searchImages(targets[i], '');
      if (r.data && (r.data.offline || r.data.busy)) { offline = r.data.error; break; }
      const first = r.data && r.data.ok ? r.data.results[0] : null;
      if (!first) continue;
      const imp = await Kviz.post('/Quiz/ImageImport', { url: first.thumbUrl });
      if (imp.data && imp.data.ok) { targets[i].relatedImageUrl = imp.data.url; targets[i].relatedImageCredit = first.credit; added++; touch(); }
    }
    btn.disabled = false; btn.innerHTML = original;
    renderAll();
    if (offline) Kviz.toast((added ? added + ' şəkil əlavə edildi, sonra dayandı. ' : '') + offline, 'error');
    else Kviz.toast(added + ' sual üçün şəkil əlavə edildi' + (added < targets.length ? ', ' + (targets.length - added) + ' sual üçün uyğun şəkil tapılmadı.' : '.'), added ? 'success' : 'error');
  });

  // ---------- Tənzimləmələr dialoqu ----------
  const catSel = $('sCat');
  catSel.append(el('option', { value: '', text: 'Seçilməyib' }), ...cats.map(c => el('option', { value: c.id, text: c.icon + ' ' + c.name })));
  function syncSettings() {
    $('sDesc').value = state.description; catSel.value = state.categoryId || ''; $('sLang').value = state.language; $('sPublic').checked = state.isPublic;
    $('sCoverPrev').style.display = state.coverImageUrl ? 'block' : 'none'; if (state.coverImageUrl) $('sCoverPrev').src = state.coverImageUrl;
    $('sCoverRm').style.display = state.coverImageUrl ? 'inline-flex' : 'none';
  }
  $('sDesc').addEventListener('input', e => { state.description = e.target.value; touch(); });
  catSel.addEventListener('change', e => { state.categoryId = e.target.value ? Number(e.target.value) : ''; touch(); });
  $('sLang').addEventListener('change', e => { state.language = e.target.value; touch(); });
  $('sPublic').addEventListener('change', e => { state.isPublic = e.target.checked; touch(); });
  if (raw.adminLocked) {
    // Administrator kvizi hamıya açıqlıqdan çıxarıb
    state.isPublic = false;
    $('sPublic').disabled = true;
    const note = document.createElement('div');
    note.className = 'alert error'; note.style.marginTop = '12px';
    note.textContent = 'Bu kviz administrator tərəfindən hamıya açıqlıqdan çıxarılıb və yenidən açıla bilməz.';
    $('sPublic').closest('label').after(note);
  }
  $('sCoverFile').addEventListener('change', async e => { const url = await uploadImage(e.target.files[0]); e.target.value = ''; if (url) { state.coverImageUrl = url; touch(); syncSettings(); } });
  $('sCoverRm').addEventListener('click', () => { state.coverImageUrl = ''; touch(); syncSettings(); });
  $('btnSettings').addEventListener('click', () => { syncSettings(); $('dlgSettings').showModal(); });
  document.querySelectorAll('dialog [data-close]').forEach(b => b.addEventListener('click', () => b.closest('dialog').close()));
  $('btnAdd').addEventListener('click', () => openTypePicker(false));

  $('edTitle').value = state.title;
  $('edTitle').addEventListener('input', e => { state.title = e.target.value; e.target.classList.remove('is-invalid'); touch(); });

  // ---------- Yadda saxla ----------
  function payload() {
    return {
      id: state.id, title: state.title, description: state.description || null, coverImageUrl: state.coverImageUrl || null,
      categoryId: state.categoryId === '' ? null : state.categoryId, language: state.language, isPublic: state.isPublic,
      questions: state.questions.map(q => ({
        type: q.type, text: q.text, imageUrl: q.imageUrl || null, relatedImageUrl: q.relatedImageUrl || null, relatedImageCredit: q.relatedImageCredit || null, timeLimitSeconds: q.timeLimitSeconds, pointsMode: q.pointsMode, multiSelect: q.multiSelect,
        sliderMin: q.sliderMin, sliderMax: q.sliderMax, sliderStep: q.sliderStep, sliderCorrect: q.sliderCorrect, sliderTolerancePercent: q.sliderTolerancePercent,
        options: q.options.map(o => ({ text: o.text, isCorrect: !!o.isCorrect }))
      }))
    };
  }
  let saving = false;
  async function save() {
    if (saving) return; saving = true;
    const btn = $('btnSave'); btn.disabled = true;
    errors = {}; errorList = [];
    try {
      const r = await Kviz.post('/Quiz/Save', payload());
      if (r.status === 401) { Kviz.toast('Sessiya bitib. Yenidən daxil olun.', 'error'); return; }
      if (!r.ok) { Kviz.toast('Server xətası. Bir az sonra yenidən yoxlayın.', 'error'); return; }
      if (r.data.ok) {
        dirty = false;
        if (!state.id) {
          state.id = r.data.id;
          history.replaceState(null, '', r.data.editUrl);
          $('btnExit').href = r.data.detailsUrl;
          $('btnDelete').hidden = false;
        }
        Kviz.toast('Yadda saxlanıldı ✓', 'success');
      } else {
        let first = -1;
        for (const e of r.data.errors) {
          errorList.push(e.message);
          const m = /^questions\[(\d+)\]/.exec(e.path);
          if (m) { const i = Number(m[1]); (errors[i] = errors[i] || []).push(e.path); if (first < 0 || i < first) first = i; }
          else if (e.path === 'title') $('edTitle').classList.add('is-invalid');
        }
        if (first >= 0) sel = first;
        Kviz.toast('Bəzi sahələri düzəltmək lazımdır.', 'error');
      }
    } finally { saving = false; btn.disabled = false; renderAll(); }
  }
  $('btnSave').addEventListener('click', save);

  // Kvizi sil (yalnız saxlanılmış kviz üçün)
  $('btnDelete').addEventListener('click', async () => {
    if (!state.id) return;
    const ok = await Kviz.confirm('Kviz silinsin?', 'Kviz və bütün sualları həmişəlik silinəcək. Keçmiş oyun hesabatları qalacaq.', 'Sil');
    if (!ok) return;
    dirty = false;
    const form = document.createElement('form');
    form.method = 'post'; form.action = '/Quiz/Delete/' + state.id;
    const t = document.createElement('input'); t.type = 'hidden'; t.name = '__RequestVerificationToken'; t.value = Kviz.csrf();
    form.appendChild(t); document.body.appendChild(form); form.submit();
  });
  document.addEventListener('keydown', e => { if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') { e.preventDefault(); save(); } });
  window.addEventListener('beforeunload', e => { if (dirty) { e.preventDefault(); e.returnValue = ''; } });

  function renderAll() { renderList(); renderStage(); renderSide(); }
  renderAll();
})();
