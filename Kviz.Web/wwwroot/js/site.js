/* Kvizo — ümumi skript: menyu, bildirişlər, təsdiq pəncərələri, fetch köməkçisi */
(function () {
  'use strict';

  const csrf = () => document.querySelector('meta[name="csrf"]')?.content || '';

  window.Kviz = {
    csrf,
    /** JSON POST (antiforgery başlığı ilə) */
    async post(url, body) {
      const res = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrf(), 'X-Requested-With': 'XMLHttpRequest' },
        body: body === undefined ? undefined : JSON.stringify(body),
        credentials: 'same-origin'
      });
      let data = null;
      try { data = await res.json(); } catch { /* boş cavab */ }
      return { ok: res.ok, status: res.status, data };
    },
    toast(message, kind) {
      const box = document.getElementById('toasts');
      if (!box) return;
      const el = document.createElement('div');
      el.className = 'toast ' + (kind || '');
      el.textContent = message;
      box.appendChild(el);
      setTimeout(() => { el.style.opacity = '0'; el.style.transition = 'opacity .3s'; setTimeout(() => el.remove(), 300); }, 3800);
    },
    /** Gözəl təsdiq pəncərəsi; Promise<boolean> qaytarır */
    confirm(title, text, okLabel, danger) {
      return new Promise(resolve => {
        const dlg = document.createElement('dialog');
        dlg.innerHTML = '<div class="dlg-body"><h3></h3><p class="muted" style="margin:0"></p></div>' +
          '<div class="dlg-actions"><button type="button" class="btn secondary" data-r="0">Ləğv et</button>' +
          '<button type="button" class="btn ' + (danger === false ? '' : 'danger') + '" data-r="1"></button></div>';
        dlg.querySelector('h3').textContent = title;
        dlg.querySelector('p').textContent = text || '';
        dlg.querySelector('[data-r="1"]').textContent = okLabel || 'Təsdiqlə';
        document.body.appendChild(dlg);
        let result = false;
        dlg.addEventListener('click', e => {
          const b = e.target.closest('[data-r]');
          if (b) { result = b.dataset.r === '1'; dlg.close(); }
        });
        dlg.addEventListener('close', () => { dlg.remove(); resolve(result); });
        dlg.showModal();
      });
    },
    copy(text) {
      if (navigator.clipboard && window.isSecureContext) return navigator.clipboard.writeText(text);
      // HTTP (lokal şəbəkə) üçün ehtiyat üsul
      const ta = document.createElement('textarea');
      ta.value = text; ta.style.position = 'fixed'; ta.style.opacity = '0';
      document.body.appendChild(ta); ta.select();
      try { document.execCommand('copy'); } catch { /* ignore */ }
      ta.remove();
      return Promise.resolve();
    }
  };

  // Mobil menyu
  const header = document.getElementById('siteHeader');
  const toggle = document.getElementById('navToggle');
  toggle?.addEventListener('click', () => {
    const open = header.classList.toggle('open');
    toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
  });

  // data-confirm="Başlıq|Mətn|Düymə" olan forma/düymələr üçün təsdiq
  document.addEventListener('submit', async e => {
    const form = e.target;
    const spec = (e.submitter && e.submitter.dataset.confirm) || form.dataset.confirm;
    if (!spec || form.dataset.confirmed) return;
    e.preventDefault();
    const [title, text, ok] = spec.split('|');
    if (await Kviz.confirm(title, text, ok || 'Bəli')) {
      form.dataset.confirmed = '1';
      form.requestSubmit(e.submitter || undefined);
    }
  });

  // PIN sahələri yalnız rəqəm qəbul etsin
  document.addEventListener('input', e => {
    if (e.target.matches('[data-pin]')) e.target.value = e.target.value.replace(/\D+/g, '').slice(0, 8);
  });
  document.querySelectorAll('.pin-mini input').forEach(i => i.setAttribute('data-pin', ''));

  // Sevimlilər
  document.addEventListener('click', async e => {
    const btn = e.target.closest('[data-fav]');
    if (btn) {
      e.preventDefault();
      const r = await Kviz.post('/Quiz/Favorite/' + btn.dataset.fav);
      if (r.status === 401) { location.href = '/Account/Login?returnUrl=' + encodeURIComponent(location.pathname); return; }
      if (r.ok) btn.classList.toggle('on', !!r.data.isFavorite);
      return;
    }
    const cp = e.target.closest('[data-copy]');
    if (cp) {
      await Kviz.copy(cp.dataset.copy);
      Kviz.toast('Kopyalandı', 'success');
    }
  });

  // Flash mesajlar bir müddət sonra solsun
  document.querySelectorAll('main .alert').forEach(a => setTimeout(() => { a.style.transition = 'opacity .5s'; a.style.opacity = '0'; setTimeout(() => a.remove(), 500); }, 6000));
})();
