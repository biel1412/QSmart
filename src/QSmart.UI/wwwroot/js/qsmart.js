// QSmart · Insi — ponte com o navegador (armazenamento, arquivos, PDF, área de transferência)
(function () {
  const BASE = document.currentScript ? new URL('.', document.currentScript.src).href : './_content/QSmart.UI/js/';
  const LIB = new URL('../lib/', BASE).href;

  // ---------- armazenamento (IndexedDB, com localStorage de reserva) ----------
  let dbp = null;
  function db() {
    if (dbp) return dbp;
    dbp = new Promise((ok, erro) => {
      try {
        const req = indexedDB.open('qsmart', 1);
        req.onupgradeneeded = () => req.result.createObjectStore('kv');
        req.onsuccess = () => ok(req.result);
        req.onerror = () => erro(req.error);
      } catch (e) { erro(e); }
    });
    return dbp;
  }
  async function tx(modo, fn) {
    const d = await db();
    return new Promise((ok, erro) => {
      const t = d.transaction('kv', modo);
      const st = t.objectStore('kv');
      const r = fn(st);
      t.oncomplete = () => ok(r && 'result' in r ? r.result : undefined);
      t.onerror = () => erro(t.error);
      t.onabort = () => erro(t.error);
    });
  }
  const kv = {
    async get(k) {
      try { const v = await tx('readonly', s => s.get(k)); return v === undefined ? null : v; }
      catch { return localStorage.getItem('qsmart:' + k); }
    },
    async set(k, v) {
      try { await tx('readwrite', s => s.put(v, k)); return true; }
      catch { try { localStorage.setItem('qsmart:' + k, v); return true; } catch { return false; } }
    },
    async del(k) {
      try { await tx('readwrite', s => s.delete(k)); } catch { localStorage.removeItem('qsmart:' + k); }
    },
    async keys() {
      try { return (await tx('readonly', s => s.getAllKeys())).map(String); }
      catch { return Object.keys(localStorage).filter(k => k.startsWith('qsmart:')).map(k => k.slice(7)); }
    },
  };

  // ---------- pasta de dados e pasta de backup (Chrome/Edge: File System Access API) ----------
  // papel: 'dados' | 'backup'. O identificador da pasta fica guardado no IndexedDB.
  const pasta = {
    suportado() { return typeof window.showDirectoryPicker === 'function'; },
    async _h(papel) {
      try { const v = await tx('readonly', s => s.get('__pasta_' + papel)); return v || null; } catch { return null; }
    },
    async nome(papel) { const h = await pasta._h(papel); return h ? h.name : null; },
    async estado(papel) {
      const h = await pasta._h(papel);
      if (!h) return 'nenhuma';
      try { return await h.queryPermission({ mode: 'readwrite' }); } catch { return 'denied'; }
    },
    async permitir(papel) {
      const h = await pasta._h(papel);
      if (!h) return false;
      try { return (await h.requestPermission({ mode: 'readwrite' })) === 'granted'; } catch { return false; }
    },
    // abre o seletor; devolve o nome da pasta escolhida (null = cancelado). Não grava a escolha ainda.
    async escolher(papel) {
      try {
        const h = await window.showDirectoryPicker({ id: 'qsmart-' + papel, mode: 'readwrite' });
        pasta._escolhida = h;
        // falha aqui se a pasta não aceitar gravação
        const f = await h.getFileHandle('.teste_escrita', { create: true });
        const w = await f.createWritable(); await w.write('ok'); await w.close();
        await h.removeEntry('.teste_escrita');
        return h.name;
      } catch (e) {
        if (e && e.name === 'AbortError') return null;
        throw e;
      }
    },
    async confirmar(papel) { await tx('readwrite', s => s.put(pasta._escolhida, '__pasta_' + papel)); },
    async esquecer(papel) { try { await tx('readwrite', s => s.delete('__pasta_' + papel)); } catch { } },
    async _dir(papel, partes, criar, escolhida) {
      let d = escolhida ? pasta._escolhida : await pasta._h(papel);
      if (!d) throw new Error('Pasta não definida.');
      for (const p of partes) d = await d.getDirectoryHandle(p, { create: !!criar });
      return d;
    },
    _partes(caminho) { const p = caminho.split('/').filter(x => x); return { dirs: p.slice(0, -1), nome: p[p.length - 1] }; },
    async ler(papel, caminho, escolhida) {
      const { dirs, nome } = pasta._partes(caminho);
      try {
        const d = await pasta._dir(papel, dirs, false, escolhida);
        const f = await (await d.getFileHandle(nome)).getFile();
        return await f.text();
      } catch (e) { if (e && (e.name === 'NotFoundError' || e.name === 'TypeMismatchError')) return null; throw e; }
    },
    async existe(papel, caminho, escolhida) { return (await pasta.ler(papel, caminho, escolhida)) !== null; },
    async gravar(papel, caminho, texto, escolhida) {
      const { dirs, nome } = pasta._partes(caminho);
      const d = await pasta._dir(papel, dirs, true, escolhida);
      const w = await (await d.getFileHandle(nome, { create: true })).createWritable();   // grava num temporário e troca ao fechar
      await w.write(texto); await w.close();
      return true;
    },
    async listar(papel, sub) {
      try {
        const d = await pasta._dir(papel, sub ? sub.split('/').filter(x => x) : [], false);
        const out = [];
        for await (const [n, h] of d.entries()) if (h.kind === 'directory') out.push(n);
        return out;
      } catch (e) { if (e && e.name === 'NotFoundError') return []; throw e; }
    },
    async apagar(papel, caminho) {
      const { dirs, nome } = pasta._partes(caminho);
      try { const d = await pasta._dir(papel, dirs, false); await d.removeEntry(nome, { recursive: true }); }
      catch (e) { if (!(e && e.name === 'NotFoundError')) throw e; }
    },
  };

  // ---------- arquivos ----------
  function baixar(nome, bytes, mime) {
    const blob = new Blob([bytes], { type: mime || 'application/octet-stream' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url; a.download = nome; a.style.display = 'none';
    document.body.appendChild(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60000);
  }
  function abrir(bytes, mime) {
    const blob = new Blob([bytes], { type: mime || 'application/pdf' });
    const url = URL.createObjectURL(blob);
    window.open(url, '_blank', 'noopener');
    setTimeout(() => URL.revokeObjectURL(url), 300000);
  }
  async function copiar(texto) {
    try { await navigator.clipboard.writeText(texto); return true; }
    catch {
      const t = document.createElement('textarea');
      t.value = texto; document.body.appendChild(t); t.select();
      let ok = false; try { ok = document.execCommand('copy'); } catch { }
      t.remove(); return ok;
    }
  }
  function abrirUrl(url) { window.open(url, '_blank', 'noopener'); }

  // ---------- PDF ----------
  let pdfLib = null, pdfJs = null;
  function carregarScript(src) {
    return new Promise((ok, erro) => {
      const s = document.createElement('script'); s.src = src; s.onload = ok; s.onerror = () => erro(new Error('Falha ao carregar ' + src));
      document.head.appendChild(s);
    });
  }
  async function lib() {
    if (!pdfLib) { await carregarScript(LIB + 'pdf-lib.min.js'); pdfLib = window.PDFLib; }
    return pdfLib;
  }
  async function js() {
    if (!pdfJs) {
      pdfJs = await import(LIB + 'pdf.min.mjs');
      pdfJs.GlobalWorkerOptions.workerSrc = LIB + 'pdf.worker.min.mjs';
    }
    return pdfJs;
  }
  async function pdfPaginas(bytes) {
    const L = await lib();
    const d = await L.PDFDocument.load(bytes, { ignoreEncryption: true });
    return d.getPageCount();
  }
  async function pdfJuntar(lista) {
    const L = await lib();
    const out = await L.PDFDocument.create();
    for (const b of lista) {
      const d = await L.PDFDocument.load(b, { ignoreEncryption: true });
      const pgs = await out.copyPages(d, d.getPageIndices());
      pgs.forEach(p => out.addPage(p));
    }
    return await out.save();
  }
  // linhas de texto do PDF (equivalente ao extract_text do pypdf)
  async function pdfLinhas(bytes) {
    const P = await js();
    const doc = await P.getDocument({ data: bytes, isEvalSupported: false }).promise;
    const linhas = [];
    for (let i = 1; i <= doc.numPages; i++) {
      const pg = await doc.getPage(i);
      const tc = await pg.getTextContent();
      let atual = '', y = null;
      for (const it of tc.items) {
        if (!('str' in it)) continue;
        const yy = it.transform ? Math.round(it.transform[5]) : y;
        if (y !== null && yy !== null && Math.abs(yy - y) > 2 && atual) { linhas.push(atual); atual = ''; }
        atual += it.str;
        y = yy;
        if (it.hasEOL) { linhas.push(atual); atual = ''; y = null; }
      }
      if (atual) linhas.push(atual);
    }
    return linhas;
  }

  // ---------- tela ----------
  function tema(nome) {
    document.documentElement.dataset.tema = nome;
    try { localStorage.setItem('qsmart:tema', JSON.stringify(nome)); } catch { }
    const meta = document.querySelector('meta[name=theme-color]');
    if (meta) meta.content = nome === 'escuro' ? '#0B0E1A' : '#F6F7FB';
  }
  function aoOcultar(ref) {
    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState === 'hidden') ref.invokeMethodAsync('AoOcultar').catch(() => { });
    });
  }
  function focar(id) { const e = document.getElementById(id); if (e) { e.focus(); if (e.select) e.select(); } }
  function rolarLinha(id, linha) {
    const c = document.getElementById(id);
    if (!c) return;
    const alvo = Math.max(0, linha * 28 - c.clientHeight / 2);
    c.scrollTop = alvo;
  }
  function pronto() {
    const l = document.getElementById('qs-carregando');
    if (l) { l.classList.add('sumir'); setTimeout(() => l.remove(), 500); }
  }

  window.qsmart = { kv, pasta, baixar, abrir, copiar, abrirUrl, pdfPaginas, pdfJuntar, pdfLinhas, tema, aoOcultar, focar, rolarLinha, pronto };
})();
