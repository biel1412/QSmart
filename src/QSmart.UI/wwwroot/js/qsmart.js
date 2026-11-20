// QSmart · Insi — ponte com o navegador (armazenamento, arquivos, PDF, área de transferência)
(function () {
  const BASE = document.currentScript ? new URL('.', document.currentScript.src).href : './_content/QSmart.UI/js/';
  const LIB = new URL('../lib/', BASE).href;

  // versão de teste (…/teste/) usa um armazenamento separado: não toca nos dados da versão oficial
  const NS = /\/teste\/?$/.test(new URL(document.baseURI).pathname) ? 'qsmart-teste' : 'qsmart';

  // ---------- armazenamento (IndexedDB, com localStorage de reserva) ----------
  let dbp = null;
  function db() {
    if (dbp) return dbp;
    dbp = new Promise((ok, erro) => {
      try {
        const req = indexedDB.open(NS, 1);
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
      catch { return localStorage.getItem(NS + ':' + k); }
    },
    async set(k, v) {
      try { await tx('readwrite', s => s.put(v, k)); return true; }
      catch { try { localStorage.setItem(NS + ':' + k, v); return true; } catch { return false; } }
    },
    async del(k) {
      try { await tx('readwrite', s => s.delete(k)); } catch { localStorage.removeItem(NS + ':' + k); }
    },
    async keys() {
      try { return (await tx('readonly', s => s.getAllKeys())).map(String); }
      catch { return Object.keys(localStorage).filter(k => k.startsWith(NS + ':')).map(k => k.slice(NS.length + 1)); }
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
    try { localStorage.setItem(NS + ':tema', JSON.stringify(nome)); } catch { }
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

  // Cofre: dados pessoais guardados só neste navegador, criptografados com AES-GCM.
  // A chave é gerada aqui, não pode ser exportada e fica no IndexedDB; nada vai para servidor.
  const cofre = {
    async chave() {
      let k = null;
      try { k = await tx('readonly', s => s.get('\u0001cofre-chave')); } catch { }
      if (k) return k;
      k = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']);
      await tx('readwrite', s => s.put(k, '\u0001cofre-chave'));
      return k;
    },
    async set(nome, bytes) {
      try {
        const k = await cofre.chave();
        const iv = crypto.getRandomValues(new Uint8Array(12));
        const c = await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, k, bytes);
        await tx('readwrite', s => s.put({ iv, c: new Uint8Array(c) }, 'cofre:' + nome));
        return true;
      } catch { return false; }
    },
    async get(nome) {
      try {
        const v = await tx('readonly', s => s.get('cofre:' + nome));
        if (!v) return null;
        const k = await cofre.chave();
        return new Uint8Array(await crypto.subtle.decrypt({ name: 'AES-GCM', iv: v.iv }, k, v.c));
      } catch { return null; }
    },
    async del(nome) { try { await tx('readwrite', s => s.delete('cofre:' + nome)); } catch { } },
    // Arquivo para a pasta de dados / backups: AES-GCM com chave derivada da senha (PBKDF2, 210 mil voltas).
    async derivar(senha, sal) {
      const base = await crypto.subtle.importKey('raw', new TextEncoder().encode(senha), 'PBKDF2', false, ['deriveKey']);
      return crypto.subtle.deriveKey({ name: 'PBKDF2', salt: sal, iterations: 210000, hash: 'SHA-256' }, base, { name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']);
    },
    async cifrarSenha(bytes, senha) {
      const sal = crypto.getRandomValues(new Uint8Array(16)), iv = crypto.getRandomValues(new Uint8Array(12));
      const k = await cofre.derivar(senha, sal);
      const c = new Uint8Array(await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, k, bytes));
      const r = new Uint8Array(28 + c.length); r.set(sal, 0); r.set(iv, 16); r.set(c, 28);
      return r;
    },
    async decifrarSenha(bytes, senha) {
      try {
        const k = await cofre.derivar(senha, bytes.slice(0, 16));
        return new Uint8Array(await crypto.subtle.decrypt({ name: 'AES-GCM', iv: bytes.slice(16, 28) }, k, bytes.slice(28)));
      } catch { return null; }
    },
  };
  // Análise com IA: chamada direta do navegador para o Groq (a chave fica só neste navegador).
  const ia = async (url, chave, corpo) => {
    try {
      const r = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json', 'Authorization': 'Bearer ' + chave }, body: corpo });
      return { status: r.status, texto: await r.text() };
    } catch (e) { return { status: 0, texto: String(e) }; }
  };
  const get = async url => {
    try { const r = await fetch(url); return { status: r.status, texto: await r.text() }; }
    catch (e) { return { status: 0, texto: String(e) }; }
  };
  // requisição genérica (contas e banco do QSmart)
  const req = async (url, metodo, cab, corpo, ms) => {
    const ctl = new AbortController(); const t = setTimeout(() => ctl.abort(), ms || 30000);
    try { const r = await fetch(url, { method: metodo, headers: cab || {}, body: corpo == null ? undefined : corpo, signal: ctl.signal }); return { status: r.status, texto: await r.text() }; }
    catch (e) { return { status: 0, texto: String(e) }; }
    finally { clearTimeout(t); }
  };
  // reduz uma imagem (foto de perfil) para um quadrado de "max" px em JPEG
  const reduzirImagem = async (bytes, max) => {
    const bmp = await createImageBitmap(new Blob([bytes]));
    const lado = Math.min(bmp.width, bmp.height);
    const c = document.createElement("canvas"); c.width = c.height = max;
    c.getContext("2d").drawImage(bmp, (bmp.width - lado) / 2, (bmp.height - lado) / 2, lado, lado, 0, 0, max, max);
    return c.toDataURL("image/jpeg", 0.85);
  };
  // GET com cabeçalhos (ex.: chave do Portal da Transparência) e tempo-limite
  const getH = async (url, cab, ms) => {
    const ctl = new AbortController(); const t = setTimeout(() => ctl.abort(), ms || 25000);
    try { const r = await fetch(url, { headers: cab || {}, signal: ctl.signal }); return { status: r.status, texto: await r.text() }; }
    catch (e) { return { status: 0, texto: String(e) }; }
    finally { clearTimeout(t); }
  };
  const rolar = id => { const el = document.getElementById(id); if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' }); };

  // imagem proporcional (lado maior = max), em JPEG com fundo branco
  const imagemDe = async (blob, max, q) => {
    const bmp = await createImageBitmap(blob);
    const k = Math.min(1, max / Math.max(bmp.width, bmp.height));
    const c = document.createElement("canvas"); c.width = Math.round(bmp.width * k); c.height = Math.round(bmp.height * k);
    const g = c.getContext("2d"); g.fillStyle = "#fff"; g.fillRect(0, 0, c.width, c.height); g.drawImage(bmp, 0, 0, c.width, c.height);
    return c.toDataURL("image/jpeg", q || 0.82);
  };
  const imagem = (bytes, max) => imagemDe(new Blob([bytes]), max);

  // ---------- Smart Note: editor de texto rico ----------
  const PERMITIDAS = new Set(["P","DIV","BR","SPAN","B","STRONG","I","EM","U","S","STRIKE","H1","H2","H3","UL","OL","LI","A","IMG","TABLE","THEAD","TBODY","TR","TD","TH","BLOCKQUOTE","PRE","CODE","HR","FONT","MARK","SUB","SUP"]);
  const ESTILOS = ["color","background-color","text-align","font-weight","font-style","text-decoration","text-decoration-line","width","margin-left"];
  const limpar = html => {
    const doc = new DOMParser().parseFromString("<div>" + (html || "") + "</div>", "text/html");
    const anda = el => {
      for (const n of [...el.childNodes]) {
        if (n.nodeType === 8) { n.remove(); continue; }
        if (n.nodeType !== 1) continue;
        if (!PERMITIDAS.has(n.tagName)) {
          if (["SCRIPT","STYLE","IFRAME","OBJECT","EMBED","LINK","META","TITLE","svg","SVG"].includes(n.tagName)) { n.remove(); continue; }
          anda(n); n.replaceWith(...n.childNodes); continue;
        }
        for (const a of [...n.attributes]) {
          const nm = a.name.toLowerCase(), v = a.value;
          let ok = false;
          if (nm === "href") ok = /^(https?:|mailto:)/i.test(v.trim());
          else if (nm === "src") ok = /^data:image\/(png|jpe?g|gif|webp);/i.test(v) || /^https:/i.test(v);
          else if (nm === "class") ok = /^(sn-check|feito)( feito)?$/.test(v);
          else if (nm === "data-feito" || nm === "colspan" || nm === "rowspan" || nm === "color") ok = true;
          else if (nm === "style") {
            const st = n.style, keep = [];
            for (const k of ESTILOS) { const val = st.getPropertyValue(k); if (val && !/url\(|expression/i.test(val)) keep.push(k + ":" + val); }
            if (keep.length) { n.setAttribute("style", keep.join(";")); continue; }
          }
          if (!ok) n.removeAttribute(a.name);
        }
        if (n.tagName === "A") { n.setAttribute("target", "_blank"); n.setAttribute("rel", "noopener noreferrer"); }
        anda(n);
      }
    };
    const raiz = doc.body.firstChild; anda(raiz);
    return raiz.innerHTML;
  };
  const notas = new Map();
  const nota = {
    montar(el, barra, arq, ref, html, id) {
      if (!el) return;
      nota.desmontar(el);
      el.innerHTML = limpar(html) || "<p><br></p>";
      el.contentEditable = "true";
      const st = { ref, id, t: null, ult: el.innerHTML };
      const salvar = () => { clearTimeout(st.t); st.t = setTimeout(() => nota.enviar(el), 1100); if (!st.dig) { st.dig = true; ref.invokeMethodAsync("Digitando"); } };
      const inserirImgs = async files => {
        for (const f of files) {
          if (!f.type.startsWith("image/")) continue;
          const url = await imagemDe(f, 1400, 0.8);
          el.focus();
          const sel = window.getSelection();
          if (nota._faixa && (!sel.rangeCount || !el.contains(sel.anchorNode))) { sel.removeAllRanges(); sel.addRange(nota._faixa); }
          document.execCommand("insertHTML", false, `<img src="${url}"><p><br></p>`);
        }
        salvar();
      };
      st.input = () => salvar();
      st.paste = e => {
        const cd = e.clipboardData; if (!cd) return;
        const imgs = [...cd.files].filter(f => f.type.startsWith("image/"));
        if (imgs.length) { e.preventDefault(); inserirImgs(imgs); return; }
        const h = cd.getData("text/html");
        if (h) { e.preventDefault(); document.execCommand("insertHTML", false, limpar(h)); salvar(); }
      };
      st.drop = e => {
        const imgs = [...(e.dataTransfer?.files || [])].filter(f => f.type.startsWith("image/"));
        if (imgs.length) { e.preventDefault(); inserirImgs(imgs); }
      };
      st.click = e => {
        const li = e.target.closest && e.target.closest("ul.sn-check > li");
        if (li && e.offsetX < 26 && e.target === li) { li.classList.toggle("feito"); salvar(); e.preventDefault(); }
        const a = e.target.closest && e.target.closest("a[href]");
        if (a && (e.ctrlKey || e.metaKey)) window.open(a.href, "_blank", "noopener");
      };
      st.tecla = e => {
        if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "s") { e.preventDefault(); nota.enviar(el); }
        if (e.key === "Tab") { e.preventDefault(); document.execCommand(e.shiftKey ? "outdent" : "indent"); salvar(); }
      };
      st.imgs = inserirImgs;
      // barra de ferramentas: tudo no navegador (sem ida e volta), sem tirar o foco do texto
      st.barra = barra; st.arq = arq;
      st.mdown = e => { if (e.target.closest("input")) return; e.preventDefault(); };
      st.bclick = e => {
        const b = e.target.closest("[data-cmd]"); if (!b || !barra.contains(b)) return;
        e.preventDefault();
        if (b.dataset.cmd === "imagem") { nota.guardarSelecao(el); arq && arq.click(); return; }
        nota.cmd(el, b.dataset.cmd, b.dataset.val);
      };
      st.arqmuda = () => { if (arq.files?.length) { const f = [...arq.files]; arq.value = ""; inserirImgs(f); } };
      if (barra) { barra.addEventListener("mousedown", st.mdown); barra.addEventListener("click", st.bclick); }
      if (arq) arq.addEventListener("change", st.arqmuda);
      el.addEventListener("input", st.input); el.addEventListener("paste", st.paste); el.addEventListener("drop", st.drop);
      el.addEventListener("click", st.click); el.addEventListener("keydown", st.tecla);
      notas.set(el, st);
    },
    enviar(el) {
      const st = notas.get(el); if (!st) return;
      clearTimeout(st.t); st.dig = false;
      const h = el.innerHTML;
      if (h === st.ult) { st.ref.invokeMethodAsync("Salvo"); return; }
      st.ult = h; st.ref.invokeMethodAsync("Mudou", st.id, h);
    },
    desmontar(el) {
      const st = notas.get(el); if (!st) return;
      if (st.ult !== el.innerHTML) { st.ult = el.innerHTML; st.ref.invokeMethodAsync("Mudou", st.id, el.innerHTML); }
      clearTimeout(st.t);
      el.removeEventListener("input", st.input); el.removeEventListener("paste", st.paste); el.removeEventListener("drop", st.drop);
      el.removeEventListener("click", st.click); el.removeEventListener("keydown", st.tecla);
      if (st.barra) { st.barra.removeEventListener("mousedown", st.mdown); st.barra.removeEventListener("click", st.bclick); }
      if (st.arq) st.arq.removeEventListener("change", st.arqmuda);
      notas.delete(el);
    },
    cmd(el, nome, valor) {
      if (!el) return;
      el.focus();
      const sel = window.getSelection();
      if (nota._faixa && (!sel.rangeCount || !el.contains(sel.anchorNode))) { sel.removeAllRanges(); sel.addRange(nota._faixa); }
      switch (nome) {
        case "h1": case "h2": case "h3": case "p": case "blockquote": case "pre":
          document.execCommand("formatBlock", false, "<" + nome + ">"); break;
        case "check": {
          document.execCommand("insertUnorderedList");
          const n = window.getSelection().anchorNode; const ul = (n.nodeType === 1 ? n : n.parentElement)?.closest("ul");
          if (ul && el.contains(ul)) ul.classList.toggle("sn-check");
          break; }
        case "link": { const u = prompt("Endereço do link (https://…)", "https://"); if (u && /^https?:\/\//i.test(u)) document.execCommand("createLink", false, u); break; }
        case "tabela": document.execCommand("insertHTML", false, "<table><tbody>" + "<tr><td><br></td><td><br></td><td><br></td></tr>".repeat(3) + "</tbody></table><p><br></p>"); break;
        case "data": document.execCommand("insertText", false, new Date().toLocaleDateString("pt-BR") + " "); break;
        default: document.execCommand(nome, false, valor ?? null);
      }
      const st = notas.get(el); if (st) { clearTimeout(st.t); st.t = setTimeout(() => nota.enviar(el), 900); if (!st.dig) { st.dig = true; st.ref.invokeMethodAsync("Digitando"); } }
    },
    guardarSelecao(el) { const s = window.getSelection(); if (s.rangeCount && el && el.contains(s.anchorNode)) nota._faixa = s.getRangeAt(0).cloneRange(); },
    async imagens(el, input) { const st = notas.get(el); if (st && input?.files) { await st.imgs([...input.files]); input.value = ""; } },
    estado() { const r = {}; for (const c of ["bold","italic","underline","strikeThrough","insertUnorderedList","insertOrderedList"]) { try { r[c] = document.queryCommandState(c); } catch { } } return r; },
  };
  // feed: colar imagem no campo de texto
  const colarImagens = (el, ref) => {
    if (!el || el._colar) return;
    el._colar = async e => {
      const imgs = [...(e.clipboardData?.files || [])].filter(f => f.type.startsWith("image/"));
      if (!imgs.length) return;
      e.preventDefault();
      for (const f of imgs) ref.invokeMethodAsync("ImagemColada", await imagemDe(f, 1280, 0.8));
    };
    el.addEventListener("paste", el._colar);
  };
  const clicar = el => { if (el) el.click(); };
  window.qsmart = { kv, cofre, rolar, ia, get, getH, req, reduzirImagem, imagem, nota, colarImagens, clicar, pasta, baixar, abrir, copiar, abrirUrl, pdfPaginas, pdfJuntar, pdfLinhas, tema, aoOcultar, focar, rolarLinha, pronto };
})();
