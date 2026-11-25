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
  // tema sazonal (Natal, Páscoa, Ano Novo) escolhido pelo administrador; "enfeites" é a preferência de cada pessoa (animações ligadas ou pausadas)
  function festa(nome, enfeites) {
    const h = document.documentElement;
    if (nome) h.dataset.festa = nome; else delete h.dataset.festa;
    if (enfeites === false) h.dataset.enfeites = 'off'; else delete h.dataset.enfeites;
    try { localStorage.setItem(NS + ':festa', JSON.stringify(nome || '')); localStorage.setItem(NS + ':enfeites', JSON.stringify(enfeites === false ? 'off' : 'on')); } catch { }
  }
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
  const ESTILOS = ["color","background-color","text-align","font-weight","font-style","text-decoration","text-decoration-line","width","margin-left","font-size","font-family","line-height"];
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
          else if (nm === "class") ok = /^(sn-check|feito|sw-quebra)( feito)?$/.test(v);
          else if (nm === "data-feito" || nm === "colspan" || nm === "rowspan" || nm === "color" || nm === "face" || nm === "size") ok = true;
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
    descartar(el) { const st = notas.get(el); if (st) { st.ult = el.innerHTML; nota.desmontar(el); } },
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

  // ---------- Smart Write: editor de documentos (estilo Word) ----------
  const DOC_RASCUNHO = "qsmart_sw_rascunho";
  const docs = new Map();
  const doc = {
    montar(el, barra, arq, ref, html, nome) {
      if (!el) return;
      doc.desmontar(el);
      el.innerHTML = limpar(html) || "<p><br></p>";
      el.contentEditable = "true";
      try { document.execCommand("styleWithCSS", false, true); } catch { }
      const st = { ref, nome: nome || "Documento", t: null, tc: null, barra, arq };
      const contar = () => {
        clearTimeout(st.tc);
        st.tc = setTimeout(() => {
          const txt = el.innerText || "";
          const palavras = (txt.match(/\S+/g) || []).length;
          const paginas = Math.max(1, Math.ceil((el.scrollHeight - 40) / 1043) + el.querySelectorAll("hr.sw-quebra").length);
          ref.invokeMethodAsync("Contagem", palavras, txt.replace(/\s/g, "").length, paginas);
        }, 300);
      };
      const rascunho = () => {
        clearTimeout(st.t);
        st.t = setTimeout(() => {
          try { localStorage.setItem(DOC_RASCUNHO, JSON.stringify({ nome: st.nome, html: el.innerHTML, em: new Date().toISOString() })); ref.invokeMethodAsync("Rascunho", true); }
          catch { ref.invokeMethodAsync("Rascunho", false); }
        }, 800);
      };
      const mudou = () => { contar(); rascunho(); if (!st.sujo) { st.sujo = true; ref.invokeMethodAsync("Sujo"); } };
      const imgs = async files => {
        for (const f of files) {
          if (!f.type.startsWith("image/")) continue;
          const url = await imagemDe(f, 1400, 0.85);
          el.focus(); doc._restaurar(el);
          document.execCommand("insertHTML", false, `<img src="${url}">`);
        }
        mudou();
      };
      st.input = mudou;
      st.paste = e => {
        const cd = e.clipboardData; if (!cd) return;
        const fs = [...cd.files].filter(f => f.type.startsWith("image/"));
        if (fs.length) { e.preventDefault(); imgs(fs); return; }
        const h = cd.getData("text/html");
        if (h) { e.preventDefault(); document.execCommand("insertHTML", false, limpar(h)); mudou(); }
      };
      st.drop = e => { const fs = [...(e.dataTransfer?.files || [])].filter(f => f.type.startsWith("image/")); if (fs.length) { e.preventDefault(); imgs(fs); } };
      st.tecla = e => {
        if (e.key === "Tab") { e.preventDefault(); const li = window.getSelection().anchorNode; const emLista = (li && (li.nodeType === 1 ? li : li.parentElement)?.closest("li"));
          if (emLista) document.execCommand(e.shiftKey ? "outdent" : "indent"); else document.execCommand("insertText", false, "\t"); mudou(); }
        if ((e.ctrlKey || e.metaKey) && e.key === "Enter") { e.preventDefault(); doc.cmd(el, "quebra"); }
        if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "s") { e.preventDefault(); ref.invokeMethodAsync("AtalhoSalvar"); }
        if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "p") { e.preventDefault(); doc.imprimir(); }
      };
      st.sel = () => { const s = window.getSelection(); if (s.rangeCount && el.contains(s.anchorNode)) { st.faixa = s.getRangeAt(0).cloneRange(); doc._estado(el); } };
      st.mdown = e => { if (e.target.closest("select, input")) return; e.preventDefault(); };
      st.bclick = e => {
        const b = e.target.closest("[data-cmd]"); if (!b || !barra.contains(b) || b.tagName === "SELECT") return;
        e.preventDefault();
        if (b.dataset.cmd === "imagem") { arq && arq.click(); return; }
        doc.cmd(el, b.dataset.cmd, b.dataset.val);
      };
      st.bchange = e => {
        const sel = e.target.closest("select[data-cmd]"); if (!sel) return;
        doc.cmd(el, sel.dataset.cmd, sel.value);
        if (sel.dataset.volta !== undefined) sel.value = sel.dataset.volta;
      };
      st.arqmuda = () => { if (arq.files?.length) { const f = [...arq.files]; arq.value = ""; imgs(f); } };
      st.mudou = mudou; st.contar = contar;
      el.addEventListener("input", st.input); el.addEventListener("paste", st.paste); el.addEventListener("drop", st.drop); el.addEventListener("keydown", st.tecla);
      document.addEventListener("selectionchange", st.sel);
      if (barra) { barra.addEventListener("mousedown", st.mdown); barra.addEventListener("click", st.bclick); barra.addEventListener("change", st.bchange); }
      if (arq) arq.addEventListener("change", st.arqmuda);
      docs.set(el, st);
      contar();
      el.focus();
    },
    desmontar(el) {
      const st = docs.get(el); if (!st) return;
      clearTimeout(st.t); clearTimeout(st.tc);
      el.removeEventListener("input", st.input); el.removeEventListener("paste", st.paste); el.removeEventListener("drop", st.drop); el.removeEventListener("keydown", st.tecla);
      document.removeEventListener("selectionchange", st.sel);
      if (st.barra) { st.barra.removeEventListener("mousedown", st.mdown); st.barra.removeEventListener("click", st.bclick); st.barra.removeEventListener("change", st.bchange); }
      if (st.arq) st.arq.removeEventListener("change", st.arqmuda);
      docs.delete(el);
    },
    nome(el, nome) { const st = docs.get(el); if (st) { st.nome = nome; st.mudou(); } },
    limpo(el) { const st = docs.get(el); if (st) st.sujo = false; },
    _restaurar(el) { const st = docs.get(el); const s = window.getSelection(); if (st?.faixa && (!s.rangeCount || !el.contains(s.anchorNode))) { s.removeAllRanges(); s.addRange(st.faixa); } },
    _estado(el) {
      const st = docs.get(el); if (!st || !st.barra) return;
      for (const c of ["bold", "italic", "underline", "strikeThrough", "insertUnorderedList", "insertOrderedList", "justifyLeft", "justifyCenter", "justifyRight", "justifyFull"]) {
        const b = st.barra.querySelector(`[data-cmd="${c}"]`); if (!b) continue;
        let on = false; try { on = document.queryCommandState(c); } catch { }
        b.classList.toggle("on", on);
      }
      const n = window.getSelection().anchorNode; const e = n && (n.nodeType === 1 ? n : n.parentElement);
      if (e && el.contains(e)) {
        const cs = getComputedStyle(e);
        const fs = st.barra.querySelector('select[data-cmd="tamanho"]'); if (fs && document.activeElement !== fs) { const pt = Math.round(parseFloat(cs.fontSize) * 0.75); fs.value = [...fs.options].some(o => o.value == pt) ? pt : ""; }
        const ff = st.barra.querySelector('select[data-cmd="fonte"]'); if (ff && document.activeElement !== ff) { const f = cs.fontFamily.split(",")[0].replace(/["']/g, "").trim(); ff.value = [...ff.options].some(o => o.value === f) ? f : ""; }
        const bl = st.barra.querySelector('select[data-cmd="bloco"]'); if (bl && document.activeElement !== bl) { const b = e.closest("h1,h2,h3,blockquote,pre,p,li,div"); const t = b ? b.tagName.toLowerCase() : "p"; bl.value = ["h1","h2","h3","blockquote","pre"].includes(t) ? t : "p"; }
      }
    },
    cmd(el, nome, valor) {
      if (!el) return;
      el.focus(); doc._restaurar(el);
      const envolver = (estilo) => {
        // aplica um estilo (font-size/font-family) na seleção usando o truque do fontSize 7
        document.execCommand("styleWithCSS", false, false);
        document.execCommand("fontSize", false, "7");
        const novos = [];
        for (const f of el.querySelectorAll('font[size="7"]')) { const sp = document.createElement("span"); sp.setAttribute("style", estilo); while (f.firstChild) sp.appendChild(f.firstChild); f.replaceWith(sp); novos.push(sp); }
        if (novos.length) {   // mantém a seleção nos trechos alterados (para aplicar outro comando em seguida)
          const r = document.createRange(); r.setStartBefore(novos[0]); r.setEndAfter(novos[novos.length - 1]);
          const s = window.getSelection(); s.removeAllRanges(); s.addRange(r);
          const st = docs.get(el); if (st) st.faixa = r.cloneRange();
        }
        for (const sp of el.querySelectorAll('span[style*="font-size: xxx-large"], span[style*="font-size:xxx-large"]')) sp.style.fontSize = "";
        document.execCommand("styleWithCSS", false, true);
      };
      switch (nome) {
        case "bloco": document.execCommand("formatBlock", false, "<" + (valor || "p") + ">"); break;
        case "fonte": if (valor) { document.execCommand("styleWithCSS", false, true); document.execCommand("fontName", false, valor); } break;
        case "tamanho": if (valor) envolver(`font-size:${valor}pt`); break;
        case "quebra": document.execCommand("insertHTML", false, '<hr class="sw-quebra"><p><br></p>'); break;
        case "link": { const u = prompt("Endereço do link (https://…)", "https://"); if (u && /^(https?:\/\/|mailto:)/i.test(u)) document.execCommand("createLink", false, u); break; }
        case "tabela": { const n = Math.max(1, Math.min(10, parseInt(valor || "3"))); const lin = "<tr>" + "<td><br></td>".repeat(n) + "</tr>"; document.execCommand("insertHTML", false, "<table><tbody>" + lin.repeat(3) + "</tbody></table><p><br></p>"); break; }
        case "data": document.execCommand("insertText", false, new Date().toLocaleDateString("pt-BR")); break;
        case "foreColor": case "hiliteColor": document.execCommand(nome, false, valor); break;
        default: document.execCommand(nome, false, valor ?? null);
      }
      const st = docs.get(el); if (st) { st.mudou(); doc._estado(el); }
    },
    carregar(el, html, nome) { const st = docs.get(el); el.innerHTML = limpar(html) || "<p><br></p>"; if (st) { st.nome = nome; st.sujo = false; st.contar(); } el.closest(".sw-mesa")?.scrollTo(0, 0); },
    html(el) { return el ? el.innerHTML : ""; },
    rascunho() { try { return localStorage.getItem(DOC_RASCUNHO) || ""; } catch { return ""; } },
    apagarRascunho() { try { localStorage.removeItem(DOC_RASCUNHO); } catch { } },
    imprimir() { document.body.classList.add("sw-imprimindo"); setTimeout(() => { window.print(); setTimeout(() => document.body.classList.remove("sw-imprimindo"), 500); }, 50); },
    // estrutura do documento para gravar o .docx
    estrutura(el) {
      if (!el) return "[]";
      let listaId = 0;
      const corDe = v => (!v || v === "transparent" || /rgba\(0, 0, 0, 0\)/.test(v)) ? "" : v;
      const blocos = [];
      const runsDe = (no, fmt, out) => {
        for (const n of no.childNodes) {
          if (n.nodeType === 3) { const x = n.nodeValue.replace(/ /g, " ").replace(/ /g, "\t"); if (x) out.push({ x, ...fmt }); continue; }
          if (n.nodeType !== 1) continue;
          const t = n.tagName;
          if (t === "BR") { out.push({ br: true }); continue; }
          if (t === "IMG") { out.push({ img: { d: n.src, w: n.width || n.naturalWidth, h: n.height || n.naturalHeight } }); continue; }
          if (["UL", "OL", "TABLE", "P", "DIV", "H1", "H2", "H3", "BLOCKQUOTE", "PRE", "HR"].includes(t)) continue;
          const f = { ...fmt }, cs = n.style;
          if (t === "B" || t === "STRONG" || cs.fontWeight === "bold" || parseInt(cs.fontWeight) >= 600) f.b = true;
          if (t === "I" || t === "EM" || cs.fontStyle === "italic") f.i = true;
          if (t === "U" || /underline/.test(cs.textDecoration || cs.textDecorationLine)) f.u = true;
          if (t === "S" || t === "STRIKE" || /line-through/.test(cs.textDecoration || cs.textDecorationLine)) f.s = true;
          if (t === "SUP") f.v = "sup"; if (t === "SUB") f.v = "sub";
          if (t === "A") f.a = n.getAttribute("href") || "";
          if (t === "FONT") { if (n.color) f.c = n.color; if (n.face) f.fo = n.face; }
          if (cs.color) f.c = cs.color;
          if (corDe(cs.backgroundColor)) f.f = cs.backgroundColor;
          if (cs.fontSize && /pt$/.test(cs.fontSize)) f.z = parseFloat(cs.fontSize); else if (cs.fontSize && /px$/.test(cs.fontSize)) f.z = Math.round(parseFloat(cs.fontSize) * 0.75);
          if (cs.fontFamily) f.fo = cs.fontFamily;
          runsDe(n, f, out);
        }
        return out;
      };
      const alin = n => { const a = (n.style && n.style.textAlign) || n.getAttribute?.("align") || ""; return ["center", "right", "justify"].includes(a) ? a : ""; };
      const bloco = (n, destino, li) => {
        if (n.nodeType === 3) { if (n.nodeValue.trim()) destino.push({ t: "p", r: [{ x: n.nodeValue }] }); return; }
        if (n.nodeType !== 1) return;
        const t = n.tagName;
        if (t === "HR") { destino.push({ t: n.classList.contains("sw-quebra") ? "quebra" : "linha" }); return; }
        if (t === "UL" || t === "OL") {
          const id = li ? li.id : "l" + (++listaId); const nivel = li ? li.n + 1 : 0;
          const check = n.classList.contains("sn-check");
          for (const item of n.children) {
            if (item.tagName !== "LI") { bloco(item, destino, li); continue; }
            const r = [];
            if (check) r.push({ x: item.classList.contains("feito") ? "☑ " : "☐ " });
            runsDe(item, {}, r);
            destino.push({ t: "p", li: { id: li ? id : id, tipo: t === "OL" ? "ol" : "ul", n: nivel }, a: alin(item), r });
            for (const sub of item.children) if (sub.tagName === "UL" || sub.tagName === "OL") bloco(sub, destino, { id: id + "_" + nivel + "_" + (++listaId), n: nivel });
          }
          return;
        }
        if (t === "TABLE") {
          const l = [];
          for (const tr of n.querySelectorAll(":scope > tbody > tr, :scope > thead > tr, :scope > tr")) {
            const cels = [];
            for (const td of tr.children) { const b = []; let temBloco = false;
              for (const c of td.childNodes) if (c.nodeType === 1 && ["P", "DIV", "H1", "H2", "H3", "UL", "OL", "BLOCKQUOTE"].includes(c.tagName)) { temBloco = true; bloco(c, b); }
              if (!temBloco) b.push({ t: "p", r: runsDe(td, {}, []) });
              cels.push({ b, span: td.colSpan || 1 }); }
            l.push(cels);
          }
          destino.push({ t: "tabela", l }); return;
        }
        if (t === "DIV" && [...n.children].some(c => ["P", "DIV", "H1", "H2", "H3", "UL", "OL", "TABLE", "BLOCKQUOTE", "HR"].includes(c.tagName))) { for (const c of n.childNodes) bloco(c, destino, li); return; }
        const s = { H1: "h1", H2: "h2", H3: "h3", BLOCKQUOTE: "citacao", PRE: "codigo" }[t] || "p";
        destino.push({ t: "p", s, a: alin(n), r: runsDe(n, {}, []) });
        for (const c of n.children) if (["UL", "OL", "TABLE"].includes(c.tagName)) bloco(c, destino, li);
      };
      // texto solto no topo vira parágrafo
      let solto = null;
      for (const n of el.childNodes) {
        const inline = n.nodeType === 3 || (n.nodeType === 1 && !["P", "DIV", "H1", "H2", "H3", "UL", "OL", "TABLE", "BLOCKQUOTE", "PRE", "HR"].includes(n.tagName));
        if (inline) { if (!solto) { solto = document.createElement("p"); } solto.appendChild(n.cloneNode(true)); continue; }
        if (solto) { bloco(solto, blocos); solto = null; }
        bloco(n, blocos);
      }
      if (solto) bloco(solto, blocos);
      return JSON.stringify(blocos);
    },
  };

  // ---------- Smart Math: teclado, área de transferência e largura das colunas ----------
  const grades = new Map();
  const grade = {
    montar(el, ref) {
      if (!el) return;
      grade.desmontar(el);
      const st = { ref };
      st.tecla = e => {
        const t = e.target;
        const ctrl = e.ctrlKey || e.metaKey;
        if (t && t.classList && t.classList.contains("sm-ed")) {
          // editando dentro da célula
          if (e.key === "Enter" || e.key === "Tab" || e.key === "Escape") { e.preventDefault(); ref.invokeMethodAsync("TeclaEdicao", e.key, e.shiftKey, t.value); }
          return;
        }
        if (t && (t.tagName === "INPUT" || t.tagName === "SELECT" || t.tagName === "TEXTAREA")) return;
        const k = e.key;
        // a caixa de edição ainda está sendo criada: guarda o que for digitado para não perder letras
        if (st.buf != null) {
          if (k.length === 1 && !ctrl && !e.altKey) { e.preventDefault(); st.buf += k; return; }
          if (k === "Backspace") { e.preventDefault(); st.buf = st.buf.slice(0, -1); return; }
          if (k === "Enter" || k === "Tab" || k === "Escape") { e.preventDefault(); st.fim = { key: k, shift: e.shiftKey }; return; }
        }
        const mov = { ArrowUp: [-1, 0], ArrowDown: [1, 0], ArrowLeft: [0, -1], ArrowRight: [0, 1] }[k];
        if (mov) { e.preventDefault(); ref.invokeMethodAsync("Mover", mov[0], mov[1], e.shiftKey, ctrl); return; }
        if (k === "Enter") { e.preventDefault(); ref.invokeMethodAsync("Mover", e.shiftKey ? -1 : 1, 0, false, false); return; }
        if (k === "Tab") { e.preventDefault(); ref.invokeMethodAsync("Mover", 0, e.shiftKey ? -1 : 1, false, false); return; }
        if (k === "PageDown" || k === "PageUp") { e.preventDefault(); ref.invokeMethodAsync("Mover", k === "PageDown" ? 20 : -20, 0, e.shiftKey, false); return; }
        if (k === "Home") { e.preventDefault(); ref.invokeMethodAsync("Inicio", ctrl); return; }
        if (k === "F2") { e.preventDefault(); ref.invokeMethodAsync("Editar", null); return; }
        if (k === "Delete" || k === "Backspace") { e.preventDefault(); ref.invokeMethodAsync("Apagar"); return; }
        if (ctrl) {
          const c = k.toLowerCase();
          if (c === "z") { e.preventDefault(); ref.invokeMethodAsync("Desfazer"); return; }
          if (c === "y") { e.preventDefault(); ref.invokeMethodAsync("Refazer"); return; }
          if (c === "b" || c === "n") { e.preventDefault(); ref.invokeMethodAsync("Atalho", "n"); return; }
          if (c === "i") { e.preventDefault(); ref.invokeMethodAsync("Atalho", "i"); return; }
          if (c === "u" || c === "s" && e.shiftKey) { e.preventDefault(); ref.invokeMethodAsync("Atalho", "s"); return; }
          if (c === "s") { e.preventDefault(); ref.invokeMethodAsync("Atalho", "salvar"); return; }
          if (c === "d") { e.preventDefault(); ref.invokeMethodAsync("Atalho", "baixo"); return; }
          if (c === "a") { e.preventDefault(); ref.invokeMethodAsync("Atalho", "tudo"); return; }
          if (c === "c" || c === "x") { e.preventDefault(); ref.invokeMethodAsync("Copiar", c === "x").then(t => { try { navigator.clipboard.writeText(t); } catch { } }); return; }
          return;   // Ctrl+V: deixa o evento "paste" acontecer
        }
        if (k.length === 1 && !e.altKey) {
          e.preventDefault(); st.buf = k; st.fim = null;
          clearTimeout(st.bufT); st.bufT = setTimeout(() => { st.buf = null; st.fim = null; }, 2000);
          ref.invokeMethodAsync("Editar", k);
        }
      };
      st.colar = e => {
        const t = e.target; if (t && t.classList && (t.classList.contains("sm-ed") || t.tagName === "INPUT")) return;
        const txt = e.clipboardData?.getData("text/plain"); if (txt == null) return;
        e.preventDefault(); ref.invokeMethodAsync("Colar", txt);
      };
      // arrastar a borda do cabeçalho para mudar a largura da coluna
      st.rz = e => {
        const h = e.target.closest && e.target.closest(".sm-rz"); if (!h) return;
        e.preventDefault(); e.stopPropagation();
        const col = parseInt(h.dataset.col), cel = el.querySelector(`col[data-col="${col}"]`);
        const x0 = e.clientX, w0 = cel ? parseFloat(cel.style.width) : 96;
        const mv = ev => { const w = Math.max(28, Math.min(800, w0 + ev.clientX - x0)); if (cel) cel.style.width = w + "px"; };
        const up = ev => { document.removeEventListener("mousemove", mv); document.removeEventListener("mouseup", up); const w = Math.max(28, Math.min(800, w0 + ev.clientX - x0)); ref.invokeMethodAsync("Largura", col, w); };
        document.addEventListener("mousemove", mv); document.addEventListener("mouseup", up);
      };
      st.solta = () => ref.invokeMethodAsync("Soltou");
      st.espelho = e => { const t = e.target; if (t && t.classList && t.classList.contains("sm-ed") && t.closest("td")) { const bar = document.querySelector(".sm-fx-campo > input"); if (bar) bar.value = t.value; } };
      document.addEventListener("input", st.espelho, true);
      st.barra = e => { const t = e.target;
        if (t && t.classList && t.classList.contains("sm-nome") && e.key === "Enter") { setTimeout(() => el.focus({ preventScroll: true }), 30); return; } if (t && t.classList && t.classList.contains("sm-ed") && t.closest(".sm-fx") && (e.key === "Enter" || e.key === "Tab" || e.key === "Escape")) { e.preventDefault(); ref.invokeMethodAsync("TeclaEdicao", e.key, e.shiftKey, t.value); } };
      document.addEventListener("keydown", st.barra, true);
      // botões da faixa e das abas não tiram o foco da grade (assim Ctrl+Z e as setas continuam funcionando)
      st.manter = e => { if (e.target.closest && e.target.closest(".sm-faixa button, .sm-faixa .sw-cor, .sm-abas button")) e.preventDefault(); };
      document.addEventListener("mousedown", st.manter, true);
      el.addEventListener("keydown", st.tecla); el.addEventListener("paste", st.colar); el.addEventListener("mousedown", st.rz, true);
      document.addEventListener("mouseup", st.solta);
      grades.set(el, st);
    },
    desmontar(el) {
      const st = grades.get(el); if (!st) return;
      el.removeEventListener("keydown", st.tecla); el.removeEventListener("paste", st.colar); el.removeEventListener("mousedown", st.rz, true);
      document.removeEventListener("mouseup", st.solta); document.removeEventListener("input", st.espelho, true); document.removeEventListener("keydown", st.barra, true); document.removeEventListener("mousedown", st.manter, true);
      grades.delete(el);
    },
    focar(el) {
      const a = document.activeElement;
      // não rouba o foco de outros campos (título do gráfico, nome do arquivo); a barra de fórmulas e a caixa da célula podem
      if (el && !(a && /^(INPUT|SELECT|TEXTAREA)$/.test(a.tagName) && !a.closest(".sm-fx") && !a.classList.contains("sm-ed"))) el.focus({ preventScroll: true });
    },
    focarEdicao(cursorFim) {
      const i = document.querySelector(".sm-ed"); if (!i) return;
      i.focus({ preventScroll: true });
      const st = [...grades.values()].find(x => x.buf != null);
      if (st) {
        const buf = st.buf, fim = st.fim; st.buf = null; st.fim = null; clearTimeout(st.bufT);
        if (i.value !== buf) { i.value = buf; i.dispatchEvent(new Event("input", { bubbles: true })); }
        if (fim) { st.ref.invokeMethodAsync("TeclaEdicao", fim.key, fim.shift, i.value); return; }
      }
      if (cursorFim) { const n = i.value.length; i.setSelectionRange(n, n); }
    },
    barra(txt) { const bar = document.querySelector(".sm-fx-campo > input"); if (bar && document.activeElement !== bar) bar.value = txt || ""; },
    valor(txt) {
      const a = document.activeElement;
      const i = a && a.classList && a.classList.contains("sm-ed") ? a : document.querySelector(".sm-ed"); if (!i) return;
      i.value = txt; i.focus({ preventScroll: true }); i.setSelectionRange(txt.length, txt.length);
      const bar = document.querySelector(".sm-fx-campo > input"); if (bar && bar !== i) bar.value = txt;
    },
    rolarPara(el, r, c) {
      const td = el && el.querySelector(`td[data-k="${r}_${c}"]`);
      if (td) { td.scrollIntoView({ block: "nearest", inline: "nearest" }); return; }
      // linha ainda não desenhada (virtualização): rola pela altura
      if (el) { const alvo = r * 25; if (alvo < el.scrollTop || alvo > el.scrollTop + el.clientHeight - 60) el.scrollTop = Math.max(0, alvo - el.clientHeight / 2); }
    },
  };
  const lsGet = k => { try { return localStorage.getItem(k) || ""; } catch { return ""; } };
  const lsSet = (k, v) => { try { localStorage.setItem(k, v); return true; } catch { return false; } };
  const lsDel = k => { try { localStorage.removeItem(k); } catch { } };

  // ---------- Smart Screen: palco dos slides (arrastar, redimensionar, editar texto, apresentar) ----------
  const SS_RASCUNHO = "qsmart_ss_rascunho";
  const limparSlide = html => {
    const d = document.createElement("div"); d.innerHTML = limpar(html);
    d.querySelectorAll("img,table,hr").forEach(n => n.remove());
    d.querySelectorAll("h1,h2,h3,blockquote,pre").forEach(n => { const p = document.createElement("p"); p.innerHTML = n.innerHTML; n.replaceWith(p); });
    return d.innerHTML;
  };
  const telas = new Map();
  const tela = {
    montar(palco, ref) {
      if (!palco) return;
      tela.desmontar(palco);
      const st = { ref, palco, k: 1, edit: null, sel: [], acao: null, t: null };
      const slideEl = () => palco.querySelector(".ss-slide");
      const ajustar = () => {
        const r = palco.getBoundingClientRect();
        const k = Math.max(0.1, Math.min((r.width - 48) / 960, (r.height - 48) / 540));
        st.k = k; palco.style.setProperty("--k", k);
      };
      st.ro = new ResizeObserver(ajustar); st.ro.observe(palco); ajustar();
      const caixa = el => ({ x: parseFloat(el.style.left) || 0, y: parseFloat(el.style.top) || 0, w: parseFloat(el.style.width) || 0, h: parseFloat(el.style.height) || 0 });
      const ponto = e => { const r = slideEl().getBoundingClientRect(); return { x: (e.clientX - r.left) / st.k, y: (e.clientY - r.top) / st.k }; };
      const selecionados = () => [...palco.querySelectorAll(".ss-slide > .ss-el.sel[data-id]")];
      const guias = (linhas) => {
        const sl = slideEl(); if (!sl) return;
        sl.querySelectorAll(".ss-guia").forEach(g => g.remove());
        for (const [tipo, v] of linhas) {
          const g = document.createElement("div"); g.className = "ss-guia " + tipo;
          if (tipo === "v") g.style.left = v + "px"; else g.style.top = v + "px";
          sl.appendChild(g);
        }
      };
      // encaixe em bordas/centros do slide e dos outros elementos
      const encaixar = (b, ignorar) => {
        const tol = 6 / Math.max(st.k, .3);
        const xs = [0, 480, 960], ys = [0, 270, 540];
        for (const el of palco.querySelectorAll(".ss-slide > .ss-el[data-id]")) {
          if (ignorar.includes(el)) continue;
          const c = caixa(el); xs.push(c.x, c.x + c.w / 2, c.x + c.w); ys.push(c.y, c.y + c.h / 2, c.y + c.h);
        }
        let dx = 0, dy = 0, mx = tol + 1, my = tol + 1, lx = null, ly = null;
        for (const [px, off] of [[b.x, 0], [b.x + b.w / 2, b.w / 2], [b.x + b.w, b.w]])
          for (const x of xs) { const d = Math.abs(px - x); if (d < mx) { mx = d; dx = x - px; lx = x; } }
        for (const [py] of [[b.y], [b.y + b.h / 2], [b.y + b.h]])
          for (const y of ys) { const d = Math.abs(py - y); if (d < my) { my = d; dy = y - py; ly = y; } }
        const linhas = [];
        if (mx <= tol) linhas.push(["v", lx]); else dx = 0;
        if (my <= tol) linhas.push(["h", ly]); else dy = 0;
        return { dx, dy, linhas };
      };
      const fimEdicao = () => {
        if (!st.edit) return;
        const el = palco.querySelector(`.ss-el[data-id="${st.edit}"]`);
        const id = st.edit; st.edit = null;
        if (el) {
          if (el.classList.contains("ss-tabela")) { ref.invokeMethodAsync("TabelaTexto", id, JSON.stringify(celulas(el))); ref.invokeMethodAsync("FimTexto", id, null); }
          else { const inn = el.querySelector(".ss-in"); if (inn) { inn.contentEditable = "false"; ref.invokeMethodAsync("FimTexto", id, limparSlide(inn.innerHTML)); } else ref.invokeMethodAsync("FimTexto", id, null); }
        } else ref.invokeMethodAsync("FimTexto", id, null);
      };
      st.fimEdicao = fimEdicao;
      const celulas = el => [...el.querySelectorAll("tr")].map(tr => [...tr.querySelectorAll("td")].map(td => td.innerText.replace(/\n$/, "")));
      st.down = e => {
        if (e.button !== 0) return;
        const alvo = e.target;
        if (st.edit && alvo.closest(`.ss-el[data-id="${st.edit}"]`) && !alvo.closest(".ss-h")) return;   // clicando dentro do texto em edição
        if (st.edit) fimEdicao();
        const h = alvo.closest(".ss-h");
        const el = alvo.closest(".ss-slide > .ss-el[data-id]");
        if (!el) { if (alvo.closest(".ss-slide") || alvo === palco) { st.sel = []; ref.invokeMethodAsync("Selecionar", null, false); } palco.focus({ preventScroll: true }); return; }
        e.preventDefault();
        palco.focus({ preventScroll: true });
        const id = el.dataset.id;
        if (e.shiftKey || e.ctrlKey) { ref.invokeMethodAsync("Selecionar", id, true); return; }
        let lista = selecionados();
        if (!el.classList.contains("sel")) { lista = [el]; el.classList.add("sel"); palco.querySelectorAll(".ss-slide > .ss-el.sel").forEach(x => { if (x !== el) x.classList.remove("sel"); }); ref.invokeMethodAsync("Selecionar", id, false); }
        const p0 = ponto(e);
        st.acao = { tipo: h ? "redim" : "mover", alca: h?.dataset.h, el, lista, inicio: lista.map(x => caixa(x)), p0, moveu: false, prop: e.shiftKey || el.classList.contains("ss-imagem") };
      };
      st.move = e => {
        const a = st.acao; if (!a) return;
        if (e.buttons === 0) { st.up(e); return; }
        const p = ponto(e); let dx = p.x - a.p0.x, dy = p.y - a.p0.y;
        if (!a.moveu && Math.abs(dx) < 2 && Math.abs(dy) < 2) return;
        a.moveu = true;
        if (a.tipo === "mover") {
          const i0 = a.inicio[a.lista.indexOf(a.el)];
          const enc = e.altKey ? { dx: 0, dy: 0, linhas: [] } : encaixar({ x: i0.x + dx, y: i0.y + dy, w: i0.w, h: i0.h }, a.lista);
          dx += enc.dx; dy += enc.dy; guias(enc.linhas);
          a.lista.forEach((x, i) => { x.style.left = (a.inicio[i].x + dx) + "px"; x.style.top = (a.inicio[i].y + dy) + "px"; });
        } else {
          const b = { ...a.inicio[0] }, al = a.alca;
          let x1 = b.x, y1 = b.y, x2 = b.x + b.w, y2 = b.y + b.h;
          if (al.includes("w")) x1 += dx; if (al.includes("e")) x2 += dx; if (al.includes("n")) y1 += dy; if (al.includes("s")) y2 += dy;
          const linha = a.el.classList.contains("ss-forma") && a.el.querySelector("line");
          if (!linha) {
            if (x2 - x1 < 8) { if (al.includes("w")) x1 = x2 - 8; else x2 = x1 + 8; }
            if (y2 - y1 < 8) { if (al.includes("n")) y1 = y2 - 8; else y2 = y1 + 8; }
          }
          const canto = al.length === 2;
          if ((a.prop || e.shiftKey) && canto && b.w > 0 && b.h > 0) {
            const r = b.w / b.h; const w = x2 - x1, hh = y2 - y1;
            if (w / hh > r) { const nw = hh * r; if (al.includes("w")) x1 = x2 - nw; else x2 = x1 + nw; }
            else { const nh = w / r; if (al.includes("n")) y1 = y2 - nh; else y2 = y1 + nh; }
          }
          Object.assign(a.el.style, { left: x1 + "px", top: y1 + "px", width: (x2 - x1) + "px", height: (y2 - y1) + "px" });
          const svg = a.el.querySelector(":scope > svg.ss-geo"); if (svg) svg.style.visibility = "hidden";
        }
      };
      st.up = () => {
        const a = st.acao; st.acao = null; guias([]);
        if (!a || !a.moveu) return;
        if (a.tipo === "mover") ref.invokeMethodAsync("Mover", JSON.stringify(a.lista.map(x => ({ id: x.dataset.id, ...caixa(x) }))));
        else { const c = caixa(a.el); ref.invokeMethodAsync("Geometria", a.el.dataset.id, c.x, c.y, c.w, c.h); }
      };
      st.dbl = e => {
        const el = e.target.closest(".ss-slide > .ss-el[data-id]"); if (!el) return;
        if (el.classList.contains("ss-imagem")) { ref.invokeMethodAsync("TrocarImagem", el.dataset.id); return; }
        if (el.classList.contains("ss-texto") || el.classList.contains("ss-forma") || el.classList.contains("ss-tabela")) {
          if (el.querySelector("line")) return;
          st.clique = { x: e.clientX, y: e.clientY };
          ref.invokeMethodAsync("EditarTexto", el.dataset.id);
        }
      };
      st.input = e => {
        if (!st.edit) return;
        const el = palco.querySelector(`.ss-el[data-id="${st.edit}"]`); if (!el) return;
        clearTimeout(st.t);
        const id = st.edit;
        st.t = setTimeout(() => {
          if (el.classList.contains("ss-tabela")) ref.invokeMethodAsync("TabelaTexto", id, JSON.stringify(celulas(el)));
          else { const inn = el.querySelector(".ss-in"); if (inn) ref.invokeMethodAsync("TextoMudou", id, limparSlide(inn.innerHTML)); }
        }, 500);
      };
      st.paste = e => {
        const cd = e.clipboardData; if (!cd) return;
        const fs = [...cd.files].filter(f => f.type.startsWith("image/"));
        const emTexto = st.edit && e.target.closest && e.target.closest(".ss-in, td");
        if (fs.length && !emTexto && !/^(INPUT|TEXTAREA|SELECT)$/.test(document.activeElement?.tagName)) { e.preventDefault(); tela._imagens(fs, ref); return; }
        if (emTexto) {
          e.preventDefault();
          const h = cd.getData("text/html");
          if (h && e.target.closest(".ss-in")) document.execCommand("insertHTML", false, limparSlide(h));
          else document.execCommand("insertText", false, cd.getData("text/plain"));
        }
      };
      st.tecla = e => {
        const ae = document.activeElement;
        const emCampo = ae && (/^(INPUT|TEXTAREA|SELECT)$/.test(ae.tagName) || ae.isContentEditable);
        const ctrl = e.ctrlKey || e.metaKey, k = e.key;
        if (document.querySelector(".ss-show")) return;   // o modo apresentação cuida das teclas
        if (st.edit) {
          if (k === "Escape") { e.preventDefault(); const id = st.edit; fimEdicao(); ref.invokeMethodAsync("Selecionar", id, false); palco.focus({ preventScroll: true }); }
          else if (k === "Tab" && ae && ae.closest(".ss-in")) { e.preventDefault(); const n = window.getSelection().anchorNode; const li = n && (n.nodeType === 1 ? n : n.parentElement)?.closest("li"); if (li) document.execCommand(e.shiftKey ? "outdent" : "indent"); }
          else if (ctrl && k.toLowerCase() === "s") { e.preventDefault(); fimEdicao(); ref.invokeMethodAsync("Atalho", "salvar"); }
          return;
        }
        if (k === "F5") { e.preventDefault(); ref.invokeMethodAsync("Atalho", e.shiftKey ? "apresentar-atual" : "apresentar"); return; }
        if (ctrl && k.toLowerCase() === "s") { e.preventDefault(); ref.invokeMethodAsync("Atalho", "salvar"); return; }
        if (emCampo) return;
        const algum = palco.querySelector(".ss-slide > .ss-el.sel[data-id]");
        if (ctrl && ["z", "y", "d", "c", "x", "v", "a"].includes(k.toLowerCase())) {
          if (k.toLowerCase() === "v") return;   // o evento "paste" decide (imagem ou elementos copiados)
          e.preventDefault(); ref.invokeMethodAsync("Atalho", "ctrl-" + k.toLowerCase()); return;
        }
        if ((k === "Delete" || k === "Backspace") && algum) { e.preventDefault(); ref.invokeMethodAsync("Atalho", "excluir"); return; }
        if (k.startsWith("Arrow") && algum) {
          e.preventDefault();
          const p = e.shiftKey ? 10 : 1;
          const dx = k === "ArrowLeft" ? -p : k === "ArrowRight" ? p : 0, dy = k === "ArrowUp" ? -p : k === "ArrowDown" ? p : 0;
          const lista = selecionados(); lista.forEach(x => { const c = caixa(x); x.style.left = (c.x + dx) + "px"; x.style.top = (c.y + dy) + "px"; });
          clearTimeout(st.tn); st.tn = setTimeout(() => ref.invokeMethodAsync("Mover", JSON.stringify(lista.map(x => ({ id: x.dataset.id, ...caixa(x) })))), 250);
          return;
        }
        if (k === "Escape") { ref.invokeMethodAsync("Selecionar", null, false); return; }
        if (k === "PageDown" || (k === "ArrowDown" && !algum)) { e.preventDefault(); ref.invokeMethodAsync("Atalho", "proximo"); return; }
        if (k === "PageUp" || (k === "ArrowUp" && !algum)) { e.preventDefault(); ref.invokeMethodAsync("Atalho", "anterior"); return; }
        if (k === "Enter" && algum && algum.matches(".ss-texto, .ss-forma")) { e.preventDefault(); ref.invokeMethodAsync("EditarTexto", algum.dataset.id); return; }
      };
      st.copiar = e => {
        if (st.edit || /^(INPUT|TEXTAREA)$/.test(document.activeElement?.tagName) || document.activeElement?.isContentEditable) return;
        if (!palco.querySelector(".ss-slide > .ss-el.sel[data-id]")) return;
        e.preventDefault(); e.clipboardData.setData("text/plain", "qsmart-slides:" + Date.now());
        ref.invokeMethodAsync("Atalho", e.type === "cut" ? "recortar" : "copiar");
      };
      st.colarDoc = e => {
        if (st.edit || /^(INPUT|TEXTAREA|SELECT)$/.test(document.activeElement?.tagName) || document.activeElement?.isContentEditable) return;
        const cd = e.clipboardData; if (!cd) return;
        const fs = [...cd.files].filter(f => f.type.startsWith("image/"));
        if (fs.length) { e.preventDefault(); tela._imagens(fs, ref); return; }
        const txt = cd.getData("text/plain") || "";
        if (txt.startsWith("qsmart-slides:")) { e.preventDefault(); ref.invokeMethodAsync("Atalho", "colar"); return; }
        if (txt.trim()) { e.preventDefault(); ref.invokeMethodAsync("ColarTexto", txt); }
      };
      st.fora = e => {
        if (!st.edit) return;
        if (palco.contains(e.target) || e.target.closest(".ss-faixa, .ss-painel")) return;
        fimEdicao();
      };
      palco.addEventListener("mousedown", st.down);
      document.addEventListener("mousemove", st.move);
      document.addEventListener("mouseup", st.up);
      palco.addEventListener("dblclick", st.dbl);
      palco.addEventListener("input", st.input);
      palco.addEventListener("paste", st.paste);
      document.addEventListener("keydown", st.tecla);
      document.addEventListener("copy", st.copiar); document.addEventListener("cut", st.copiar); document.addEventListener("paste", st.colarDoc);
      document.addEventListener("mousedown", st.fora, true);
      // a faixa de botões não tira o foco nem a seleção do texto
      st.faixa = e => { if (e.target.tagName !== "SELECT" && e.target.tagName !== "INPUT" && e.target.closest(".ss-faixa [data-cmd], .ss-faixa .ss-cor, .ss-faixa button")) e.preventDefault(); };
      document.addEventListener("mousedown", st.faixa);
      st.faixaClique = e => { const b = e.target.closest(".ss-faixa [data-cmd]"); if (!b || b.tagName === "SELECT") return; e.preventDefault(); tela.cmd(b.dataset.cmd, b.dataset.val); };
      st.faixaMuda = e => { const s = e.target.closest(".ss-faixa select[data-cmd]"); if (!s) return; tela.cmd(s.dataset.cmd, s.value); };
      document.addEventListener("click", st.faixaClique); document.addEventListener("change", st.faixaMuda);
      st.selmuda = () => tela._estado();
      document.addEventListener("selectionchange", st.selmuda);
      telas.set(palco, st);
      palco.focus({ preventScroll: true });
    },
    desmontar(palco) {
      const st = telas.get(palco); if (!st) return;
      st.ro.disconnect();
      palco.removeEventListener("mousedown", st.down); palco.removeEventListener("dblclick", st.dbl); palco.removeEventListener("input", st.input); palco.removeEventListener("paste", st.paste);
      document.removeEventListener("mousemove", st.move); document.removeEventListener("mouseup", st.up); document.removeEventListener("keydown", st.tecla);
      document.removeEventListener("copy", st.copiar); document.removeEventListener("cut", st.copiar); document.removeEventListener("paste", st.colarDoc);
      document.removeEventListener("mousedown", st.fora, true); document.removeEventListener("mousedown", st.faixa);
      document.removeEventListener("click", st.faixaClique); document.removeEventListener("change", st.faixaMuda);
      document.removeEventListener("selectionchange", st.selmuda);
      telas.delete(palco);
    },
    _st() { return [...telas.values()][0]; },
    // começa a editar o texto (chamado depois que o Blazor marcou o elemento como "edit")
    editar(id, tudo) {
      const st = tela._st(); if (!st) return;
      st.edit = id;
      const el = st.palco.querySelector(`.ss-el[data-id="${id}"]`); if (!el) return;
      if (el.classList.contains("ss-tabela")) { const td = el.querySelector("td"); td && td.focus(); return; }
      const inn = el.querySelector(".ss-in"); if (!inn) return;
      inn.contentEditable = "true";
      try { document.execCommand("styleWithCSS", false, true); } catch { }
      if (!inn.innerHTML.trim()) inn.innerHTML = el.querySelector(".ss-p-corpo") ? "<ul><li><br></li></ul>" : "<p><br></p>";
      // parágrafos vazios precisam de um <br> para o cursor entrar neles (e manter o alinhamento do modelo)
      inn.querySelectorAll("p, li, div").forEach(b => { if (!b.textContent && !b.querySelector("br") && !b.querySelector("p, li, div, ul, ol")) b.appendChild(document.createElement("br")); });
      inn.focus({ preventScroll: true });
      const s = window.getSelection(); s.removeAllRanges();
      let r = null;
      if (!tudo && st.clique && document.caretRangeFromPoint) { r = document.caretRangeFromPoint(st.clique.x, st.clique.y); if (r && !inn.contains(r.startContainer)) r = null; }
      st.clique = null;
      if (!r) {
        r = document.createRange();
        const blocos = inn.querySelectorAll("p, li, div");
        const ult = blocos.length ? blocos[blocos.length - 1] : null;
        if (tudo) r.selectNodeContents(inn);
        else if (ult) { r.selectNodeContents(ult); if (ult.lastChild && ult.lastChild.nodeName === "BR") r.setEndBefore(ult.lastChild); r.collapse(false); }
        else { r.selectNodeContents(inn); r.collapse(false); }
      }
      s.addRange(r);
    },
    // comando da faixa (negrito, cor, tamanho…): no texto em edição ou em todos os textos selecionados
    cmd(nome, valor) {
      const st = tela._st(); if (!st) return;
      const aplicar = (raiz) => {
        const envolver = estilo => {
          document.execCommand("styleWithCSS", false, false);
          document.execCommand("fontSize", false, "7");
          const novos = [];
          for (const f of raiz.querySelectorAll('font[size="7"]')) { const sp = document.createElement("span"); sp.setAttribute("style", estilo); while (f.firstChild) sp.appendChild(f.firstChild); f.replaceWith(sp); novos.push(sp); }
          // o tamanho novo vale também para os trechos de dentro
          for (const sp of novos) sp.querySelectorAll("span").forEach(x => { if (estilo.startsWith("font-size")) x.style.fontSize = ""; });
          if (novos.length) { const r = document.createRange(); r.setStartBefore(novos[0]); r.setEndAfter(novos[novos.length - 1]); const s = window.getSelection(); s.removeAllRanges(); s.addRange(r); }
          document.execCommand("styleWithCSS", false, true);
        };
        const tamAtual = () => { const n = window.getSelection().anchorNode; const e = n && (n.nodeType === 1 ? n : n.parentElement); return e ? parseFloat(getComputedStyle(e).fontSize) : 24; };
        switch (nome) {
          case "tamanho": if (valor) envolver(`font-size:${valor}px`); break;
          case "maior": case "menor": { const t = tamAtual(); const passos = [8,10,12,14,16,18,20,24,28,32,36,40,44,48,54,60,66,72,80,88,96]; let n = nome === "maior" ? passos.find(p => p > t + .5) : [...passos].reverse().find(p => p < t - .5); if (n) envolver(`font-size:${n}px`); break; }
          case "fonte": if (valor) { document.execCommand("styleWithCSS", false, true); document.execCommand("fontName", false, valor); } break;
          case "foreColor": document.execCommand("styleWithCSS", false, true); document.execCommand("foreColor", false, valor || "inherit"); break;
          default: document.execCommand(nome, false, valor ?? null);
        }
      };
      if (st.edit) {
        const el = st.palco.querySelector(`.ss-el[data-id="${st.edit}"] .ss-in`); if (!el) return;
        el.focus({ preventScroll: true });
        aplicar(el);
        st.input();
        tela._estado();
        return;
      }
      const alvos = [...st.palco.querySelectorAll(".ss-slide > .ss-el.sel[data-id] .ss-in")];
      if (!alvos.length) return;
      const res = [];
      for (const inn of alvos) {
        const vazio = !inn.textContent.trim();
        if (vazio) inn.innerHTML = inn.closest(".ss-el").querySelector(".ss-p-corpo") ? "<ul><li>​</li></ul>" : "<p>​</p>";
        inn.contentEditable = "true"; inn.focus({ preventScroll: true });
        const r = document.createRange(); r.selectNodeContents(inn); const s = window.getSelection(); s.removeAllRanges(); s.addRange(r);
        aplicar(inn);
        inn.contentEditable = "false";
        res.push({ id: inn.closest(".ss-el").dataset.id, html: limparSlide(inn.innerHTML).replace(/​/g, "") });
      }
      window.getSelection().removeAllRanges();
      st.palco.focus({ preventScroll: true });
      st.ref.invokeMethodAsync("TextosMudaram", JSON.stringify(res));
    },
    _estado() {
      const st = tela._st(); if (!st) return;
      const barra = document.querySelector(".ss-faixa"); if (!barra) return;
      let alvo = null;
      if (st.edit) { const n = window.getSelection().anchorNode; alvo = n && (n.nodeType === 1 ? n : n.parentElement); if (alvo && !alvo.closest(".ss-in")) alvo = null; }
      else { const inn = st.palco.querySelector(".ss-slide > .ss-el.sel[data-id] .ss-in"); alvo = inn && (inn.querySelector("li, p, span, b, i") || inn); }
      for (const c of ["bold", "italic", "underline", "strikeThrough", "insertUnorderedList", "insertOrderedList", "justifyLeft", "justifyCenter", "justifyRight"]) {
        const b = barra.querySelector(`[data-cmd="${c}"]`); if (!b) continue;
        let on = false;
        if (st.edit) { try { on = document.queryCommandState(c); } catch { } }
        else if (alvo) {
          const cs = getComputedStyle(alvo);
          on = c === "bold" ? parseInt(cs.fontWeight) >= 600 : c === "italic" ? cs.fontStyle === "italic" : c === "underline" ? cs.textDecorationLine.includes("underline")
            : c === "insertUnorderedList" ? !!alvo.closest("ul") : c === "insertOrderedList" ? !!alvo.closest("ol")
            : c === "justifyCenter" ? cs.textAlign === "center" : c === "justifyRight" ? cs.textAlign === "right" : false;
        }
        b.classList.toggle("on", on);
      }
      if (alvo) {
        const cs = getComputedStyle(alvo);
        const fs = barra.querySelector('select[data-cmd="tamanho"]'); if (fs && document.activeElement !== fs) { const px = Math.round(parseFloat(cs.fontSize)); fs.value = [...fs.options].some(o => o.value == px) ? px : ""; if (fs.value === "" && fs.options[0]) fs.options[0].text = px ? String(px) : "Tam."; }
        const ff = barra.querySelector('select[data-cmd="fonte"]'); if (ff && document.activeElement !== ff) { const f = cs.fontFamily.split(",")[0].replace(/["']/g, "").trim(); ff.value = [...ff.options].some(o => o.value === f) ? f : ""; }
      }
    },
    fimEdicao() { const st = tela._st(); if (st) st.fimEdicao(); },
    focar() { const st = tela._st(); if (st) st.palco.focus({ preventScroll: true }); },
    async _imagens(fs, ref) {
      for (const f of fs) ref.invokeMethodAsync("ImagemColada", await tela._dataUrl(f));
    },
    async _dataUrl(blob) {
      // PNG/GIF pequenos ficam como estão (mantém transparência); o resto vira JPEG de até 1600 px
      if (/png|gif|webp/.test(blob.type) && blob.size < 1500000) return await new Promise(ok => { const r = new FileReader(); r.onload = () => ok(r.result); r.readAsDataURL(blob); });
      const bmp = await createImageBitmap(blob);
      const k = Math.min(1, 1600 / Math.max(bmp.width, bmp.height));
      const c = document.createElement("canvas"); c.width = Math.round(bmp.width * k); c.height = Math.round(bmp.height * k);
      const g = c.getContext("2d");
      if (/png|gif|webp/.test(blob.type)) { c.getContext("2d").drawImage(bmp, 0, 0, c.width, c.height); return c.toDataURL("image/png"); }
      g.fillStyle = "#fff"; g.fillRect(0, 0, c.width, c.height); g.drawImage(bmp, 0, 0, c.width, c.height);
      return c.toDataURL("image/jpeg", 0.86);
    },
    imagemDeBytes(bytes, tipo) { return tela._dataUrl(new Blob([bytes], { type: tipo || "image/png" })); },
    tamanhoImagem(src) { return new Promise(ok => { const i = new Image(); i.onload = () => ok([i.naturalWidth, i.naturalHeight]); i.onerror = () => ok([0, 0]); i.src = src; }); },
    // escala de um contêiner (--k) para o slide de 960 × 540 caber
    escalar(el, margem) {
      if (!el) return;
      const f = () => { const r = el.getBoundingClientRect(); el.style.setProperty("--k", Math.max(0.05, Math.min((r.width - (margem || 0)) / 960, (r.height - (margem || 0)) / 540))); };
      if (el._ro) el._ro.disconnect();
      el._ro = new ResizeObserver(f); el._ro.observe(el); f();
    },
    // ---- modo apresentação
    show(el, ref) {
      if (!el) return;
      tela.escalar(el.querySelector(".ss-show-tela") || el, 0);
      el.querySelectorAll("[data-escala]").forEach(x => tela.escalar(x, 0));
      const tecla = e => {
        const k = e.key;
        if (["ArrowRight", "ArrowDown", "PageDown", " ", "Enter", "n", "N"].includes(k)) { e.preventDefault(); ref.invokeMethodAsync("ShowTecla", "avancar"); }
        else if (["ArrowLeft", "ArrowUp", "PageUp", "Backspace", "p", "P"].includes(k)) { e.preventDefault(); ref.invokeMethodAsync("ShowTecla", "voltar"); }
        else if (k === "Escape") { e.preventDefault(); ref.invokeMethodAsync("ShowTecla", "sair"); }
        else if (k === "Home") { e.preventDefault(); ref.invokeMethodAsync("ShowTecla", "inicio"); }
        else if (k === "End") { e.preventDefault(); ref.invokeMethodAsync("ShowTecla", "fim"); }
        else if (k === "b" || k === "B" || k === ".") { e.preventDefault(); ref.invokeMethodAsync("ShowTecla", "preto"); }
        else if (k === "w" || k === "W" || k === ",") { e.preventDefault(); ref.invokeMethodAsync("ShowTecla", "branco"); }
        else if (k === "a" || k === "A") { e.preventDefault(); ref.invokeMethodAsync("ShowTecla", "apresentador"); }
      };
      const clique = e => { if (e.target.closest("button, a, textarea, input, .ss-show-barra, .ss-ap-lado")) return; if (e.button === 0) ref.invokeMethodAsync("ShowTecla", "avancar"); };
      const ctx = e => { e.preventDefault(); ref.invokeMethodAsync("ShowTecla", "voltar"); };
      let tx = null;
      const ts = e => { tx = e.touches[0].clientX; };
      const te = e => { if (tx == null) return; const d = e.changedTouches[0].clientX - tx; tx = null; if (Math.abs(d) > 50) ref.invokeMethodAsync("ShowTecla", d < 0 ? "avancar" : "voltar"); };
      const fsMuda = () => { if (!document.fullscreenElement && el._cheia) { el._cheia = false; ref.invokeMethodAsync("ShowTecla", "saiu-tela-cheia"); } };
      let tm = null; const mexeu = () => { el.classList.add("mouse"); clearTimeout(tm); tm = setTimeout(() => el.classList.remove("mouse"), 2200); };
      document.addEventListener("keydown", tecla); el.addEventListener("mousedown", clique); el.addEventListener("contextmenu", ctx);
      el.addEventListener("touchstart", ts, { passive: true }); el.addEventListener("touchend", te);
      document.addEventListener("fullscreenchange", fsMuda); el.addEventListener("mousemove", mexeu);
      el._fim = () => { document.removeEventListener("keydown", tecla); el.removeEventListener("mousedown", clique); el.removeEventListener("contextmenu", ctx); el.removeEventListener("touchstart", ts); el.removeEventListener("touchend", te); document.removeEventListener("fullscreenchange", fsMuda); el.removeEventListener("mousemove", mexeu); };
      el.focus({ preventScroll: true });
    },
    async telaCheia(el, ligar) {
      try {
        if (ligar && el && !document.fullscreenElement) { await el.requestFullscreen({ navigationUI: "hide" }); el._cheia = true; }
        else if (!ligar && document.fullscreenElement) { if (el) el._cheia = false; await document.exitFullscreen(); }
      } catch { }
    },
    fimShow(el) { if (el && el._fim) { el._fim(); el._fim = null; } if (document.fullscreenElement) document.exitFullscreen().catch(() => { }); },
    // ---- janela da plateia (segunda tela): o apresentador manda o estado por BroadcastChannel
    _canal: null,
    canal() { if (!tela._canal && "BroadcastChannel" in window) tela._canal = new BroadcastChannel("qsmart-smart-screen"); return tela._canal; },
    plateiaAbrir(json) {
      try { localStorage.setItem("qsmart_ss_plateia", json); } catch { return false; }
      const u = new URL(document.baseURI); u.searchParams.set("plateia", "1");
      const w = window.open(u.href, "qsmart-plateia", "popup,width=1280,height=760");
      return !!w;
    },
    plateiaEnviar(estado) { try { localStorage.setItem("qsmart_ss_plateia_estado", JSON.stringify(estado)); } catch { } const c = tela.canal(); if (c) c.postMessage(estado); },
    plateiaEstado() { try { return localStorage.getItem("qsmart_ss_plateia_estado") || ""; } catch { return ""; } },
    escalarTodos(el) { if (el) el.querySelectorAll("[data-escala]").forEach(x => tela.escalar(x, 0)); },
    plateiaOuvir(ref) {
      const c = tela.canal(); if (!c) return;
      c.onmessage = ev => ref.invokeMethodAsync("Estado", JSON.stringify(ev.data));
      c.postMessage({ tipo: "oi" });
    },
    plateiaDados() { try { return localStorage.getItem("qsmart_ss_plateia") || ""; } catch { return ""; } },
    imprimir() {
      document.body.classList.add("ss-imprimindo");
      const fim = () => { document.body.classList.remove("ss-imprimindo"); window.removeEventListener("afterprint", fim); };
      window.addEventListener("afterprint", fim);
      setTimeout(() => window.print(), 300);
    },
  };
  const clicar = el => { if (el) el.click(); };
  nota.ver = (el, html) => { if (el) { el.contentEditable = "false"; el.innerHTML = limpar(html) || ""; } };
  const trocarUrl = (u, titulo) => { try { history.replaceState(history.state, "", u); } catch { } if (titulo) document.title = titulo; };
  // ---------- Downloads: arquivos grandes vão e vêm direto pelo navegador, com progresso ----------
  // capa: imagem 16:9 reduzida (até 960 px de largura)
  const capa = async (input) => {
    const f = input && input.files && input.files[0]; if (!f) return "";
    const bmp = await createImageBitmap(f);
    const w = Math.min(960, bmp.width), h = Math.round(w * bmp.height / bmp.width);
    const c = document.createElement("canvas"); c.width = w; c.height = h;
    c.getContext("2d").drawImage(bmp, 0, 0, w, h);
    input.value = "";
    return c.toDataURL("image/jpeg", 0.82);
  };
  const arqInfo = input => { const f = input && input.files && input.files[0]; return f ? { nome: f.name, tamanho: f.size } : null; };
  const enviarArq = (input, url, token, ref) => new Promise((ok, falha) => {
    const f = input && input.files && input.files[0]; if (!f) { falha(new Error("Escolha o arquivo.")); return; }
    const x = new XMLHttpRequest();
    x.open("PUT", url + (url.includes("?") ? "&" : "?") + "nome=" + encodeURIComponent(f.name));
    x.setRequestHeader("Authorization", "Bearer " + token);
    x.setRequestHeader("Content-Type", f.type || "application/octet-stream");
    x.upload.onprogress = e => { if (e.lengthComputable && ref) ref.invokeMethodAsync("Progresso", Math.round(e.loaded * 100 / e.total)); };
    x.onload = () => { input.value = ""; ok(x.status + "|" + x.responseText); };
    x.onerror = () => falha(new Error("Falha de conexão ao enviar o arquivo."));
    x.send(f);
  });
  const baixarArq = async (url, token, nome, ref) => {
    const r = await fetch(url, { headers: { Authorization: "Bearer " + token } });
    if (!r.ok) { let m = "Erro " + r.status; try { m = (await r.json()).erro || m; } catch { } throw new Error(m); }
    const total = Number(r.headers.get("Content-Length") || 0);
    const leitor = r.body.getReader(); const partes = []; let lido = 0, ultimo = -1;
    for (;;) {
      const { done, value } = await leitor.read(); if (done) break;
      partes.push(value); lido += value.length;
      const p = total ? Math.round(lido * 100 / total) : -1;
      if (ref && p !== ultimo && p % 2 === 0) { ultimo = p; ref.invokeMethodAsync("Progresso", p); }
    }
    const a = document.createElement("a"); a.href = URL.createObjectURL(new Blob(partes, { type: r.headers.get("Content-Type") || "application/octet-stream" }));
    a.download = nome; document.body.appendChild(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(a.href), 30000);
  };
  const downloads = { capa, arqInfo, enviarArq, baixarArq };
  // chat: rola até a última mensagem só se a pessoa já estava perto do fim (ou se pedir)
  const chatFim = (el, forcar) => { if (!el) return; if (forcar || el.scrollHeight - el.scrollTop - el.clientHeight < 140) el.scrollTop = el.scrollHeight; };
  // painel de uso: quanto este navegador guarda do QSmart (armazenamento local + cofre/IndexedDB)
  const armazenamento = async () => {
    let local = 0;
    try { for (let i = 0; i < localStorage.length; i++) { const k = localStorage.key(i); local += (k.length + (localStorage.getItem(k) || "").length) * 2; } } catch { }
    let usado = 0, cota = 0;
    try { if (navigator.storage && navigator.storage.estimate) { const e = await navigator.storage.estimate(); usado = e.usage || 0; cota = e.quota || 0; } } catch { }
    return { local, usado, cota };
  };

  // Ecossistema QSmart (tela de entrada): plataforma no centro e os ambientes em órbita.
  function ecossistema() {
    if (document.querySelector(".eco")) return;
    const NS = "http://www.w3.org/2000/svg";
    const el = (t, a, p) => { const e = document.createElementNS(NS, t); for (const k in a) e.setAttribute(k, a[k]); if (p) p.appendChild(e); return e; };
    const W = 1000, H = 640, CX = 500, CY = 330;
    const raiz = document.createElement("div");
    raiz.className = "eco"; raiz.setAttribute("role", "dialog"); raiz.setAttribute("aria-label", "Ecossistema QSmart");
    raiz.innerHTML = `
      <div class="eco-topo"><span class="eco-rot">ECOSSISTEMA QSMART</span>
        <span class="eco-leg"><i class="on"></i>Em operação<i></i>Compatível</span>
        <button class="eco-x" aria-label="Fechar">✕</button></div>
      <svg class="eco-svg" viewBox="0 0 ${W} ${H}" preserveAspectRatio="xMidYMid meet">
        <defs>
          <linearGradient id="eco-lg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#14B8A6"/><stop offset=".55" stop-color="#3B82F6"/><stop offset="1" stop-color="#7C3AED"/></linearGradient>
          <radialGradient id="eco-halo"><stop offset="0" stop-color="#3B82F6" stop-opacity=".32"/><stop offset="1" stop-color="#3B82F6" stop-opacity="0"/></radialGradient>
          <radialGradient id="eco-pS" cx=".32" cy=".28" r=".75"><stop offset="0" stop-color="#C9F2E2"/><stop offset=".35" stop-color="#2BB58A"/><stop offset=".8" stop-color="#0F6E56"/><stop offset="1" stop-color="#04342C"/></radialGradient>
          <radialGradient id="eco-pG" cx=".32" cy=".28" r=".75"><stop offset="0" stop-color="#E7E4FB"/><stop offset=".35" stop-color="#8F87E6"/><stop offset=".8" stop-color="#4A41A8"/><stop offset="1" stop-color="#1E1A4D"/></radialGradient>
          <radialGradient id="eco-pR" cx=".32" cy=".28" r=".75"><stop offset="0" stop-color="#E3F0FC"/><stop offset=".35" stop-color="#5A9FE6"/><stop offset=".8" stop-color="#185FA5"/><stop offset="1" stop-color="#062447"/></radialGradient>
          <radialGradient id="eco-aS"><stop offset=".7" stop-color="#5DCAA5" stop-opacity="0"/><stop offset=".85" stop-color="#5DCAA5" stop-opacity=".35"/><stop offset="1" stop-color="#5DCAA5" stop-opacity="0"/></radialGradient>
          <radialGradient id="eco-aG"><stop offset=".7" stop-color="#AFA9EC" stop-opacity="0"/><stop offset=".85" stop-color="#AFA9EC" stop-opacity=".25"/><stop offset="1" stop-color="#AFA9EC" stop-opacity="0"/></radialGradient>
          <radialGradient id="eco-aR"><stop offset=".7" stop-color="#85B7EB" stop-opacity="0"/><stop offset=".85" stop-color="#85B7EB" stop-opacity=".25"/><stop offset="1" stop-color="#85B7EB" stop-opacity="0"/></radialGradient>
        </defs>
        <ellipse cx="${CX}" cy="${CY}" rx="220" ry="130" fill="none" stroke="#3B82F6" stroke-opacity=".28"/>
        <ellipse cx="${CX}" cy="${CY}" rx="400" ry="232" fill="none" stroke="#8fa3c4" stroke-opacity=".18" stroke-dasharray="2 6"/>
        <g class="eco-linhas"></g><g class="eco-tras"></g>
        <circle cx="${CX}" cy="${CY}" r="120" fill="url(#eco-halo)"/>
        <g transform="translate(${CX - 48} ${CY - 48})">
          <rect width="96" height="96" rx="23" fill="url(#eco-lg)"/>
          <circle cx="44" cy="45" r="23" fill="none" stroke="#fff" stroke-width="7"/>
          <path d="M33 45l9 9 16.5-18.5" fill="none" stroke="#fff" stroke-width="7" stroke-linecap="round" stroke-linejoin="round"/>
          <path d="M62 63l12 12" stroke="#fff" stroke-width="8" stroke-linecap="round"/>
          <path d="M77 12l2.2 5.5 5.5 2.2-5.5 2.2-2.2 5.5-2.2-5.5-5.5-2.2 5.5-2.2z" fill="#fff"/>
        </g>
        <text x="${CX}" y="${CY + 76}" text-anchor="middle" fill="#fff" font-size="17" font-weight="700">Plataforma QSmart</text>
        <text x="${CX}" y="${CY + 96}" text-anchor="middle" fill="#8fa3c4" font-size="13">núcleo comum</text>
        <g class="eco-frente"></g>
      </svg>
      <div class="eco-info">Selecione um ambiente. O Sicoob leva você de volta à plataforma.</div>`;
    document.body.appendChild(raiz);
    const svg = raiz.querySelector("svg"), L = svg.querySelector(".eco-linhas"), TR = svg.querySelector(".eco-tras"), FR = svg.querySelector(".eco-frente"), info = raiz.querySelector(".eco-info");
    const A = [
      { k: "sicoob", n: "Sicoob", st: "Em operação · entrar", rx: 220, ry: 130, r: 40, a: -0.4, v: .00007, g: "pS", at: "aS", ativo: true,
        mods: ["One a One", "Férias", "Day off", "Radar Smart360", "Mr. Smart"] },
      { k: "gedau", n: "Gedau", st: "Compatível", rx: 400, ry: 232, r: 31, a: 2.2, v: .00004, g: "pG", at: "aG",
        txt: "<b>Gedau · ambiente compatível.</b> Mesmo núcleo de segurança, regras e relatórios; implantação sob demanda." },
      { k: "sicredi", n: "Sicredi", st: "Compatível", rx: 400, ry: 232, r: 34, a: 4.4, v: .00004, g: "pR", at: "aR", anel: true,
        txt: "<b>Sicredi · ambiente compatível.</b> A arquitetura permite novos ambientes sem refazer a plataforma." }];
    let fechado = false;
    const fechar = () => {
      if (fechado) return; fechado = true;
      raiz.classList.add("sai"); document.removeEventListener("keydown", tecla);
      setTimeout(() => raiz.remove(), 450);
    };
    const tecla = e => { if (e.key === "Escape") fechar(); };
    document.addEventListener("keydown", tecla);
    raiz.querySelector(".eco-x").onclick = fechar;
    A.forEach(p => {
      p.l = el("line", { stroke: p.ativo ? "#1D9E75" : "#8fa3c4", "stroke-opacity": p.ativo ? .7 : .35, "stroke-width": p.ativo ? 2 : 1.2, "stroke-dasharray": p.ativo ? "" : "5 5" }, L);
      if (p.ativo) p.pulso = el("circle", { r: 4, fill: "#5DCAA5" }, L);
      p.G = el("g", { class: "eco-mundo", tabindex: "0", role: "button", "aria-label": p.n + ", " + p.st });
      const ir = () => { if (p.ativo) fechar(); else info.innerHTML = p.txt; };
      p.G.addEventListener("click", ir);
      p.G.addEventListener("keydown", e => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); ir(); } });
      if (p.ativo) {
        p.G.addEventListener("mouseenter", () => info.innerHTML = "<b>Sicoob · ambiente em operação.</b> Módulos ativos: " + p.mods.join(", ") + " e outros. Clique para voltar à plataforma.");
      }
      el("circle", { r: p.r * 1.45, fill: `url(#eco-${p.at})` }, p.G);
      if (p.anel) el("ellipse", { rx: p.r * 1.8, ry: p.r * .42, fill: "none", stroke: "#85B7EB", "stroke-opacity": .55, "stroke-width": 3, transform: "rotate(-14)" }, p.G);
      el("circle", { class: "eco-esfera", r: p.r, fill: `url(#eco-${p.g})` }, p.G);
      el("path", { d: `M${-p.r * .85} ${-p.r * .15} Q0 ${p.r * .2} ${p.r * .85} ${-p.r * .25}`, fill: "none", stroke: "#fff", "stroke-opacity": .12, "stroke-width": 4 }, p.G);
      el("path", { d: `M${-p.r * .75} ${p.r * .35} Q0 ${p.r * .65} ${p.r * .7} ${p.r * .3}`, fill: "none", stroke: "#fff", "stroke-opacity": .08, "stroke-width": 3 }, p.G);
      if (p.anel) el("path", { d: `M${-p.r * 1.75} ${p.r * .45} A${p.r * 1.8} ${p.r * .42} -14 0 0 ${p.r * 1.75} ${-p.r * .45}`, fill: "none", stroke: "#85B7EB", "stroke-opacity": .75, "stroke-width": 3 }, p.G);
      if (p.mods) p.luas = p.mods.map(m => { const c = el("circle", { r: 5, fill: "#9FE1CB" }, p.G); el("title", {}, c).textContent = m; return c; });
      const w = p.ativo ? 176 : 140;
      el("rect", { x: -w / 2, y: p.r + 20, width: w, height: 52, rx: 10, fill: "#111c31", stroke: p.ativo ? "#1D9E75" : "#2a3a58" }, p.G);
      el("text", { x: 0, y: p.r + 42, "text-anchor": "middle", fill: "#fff", "font-size": 16, "font-weight": 700 }, p.G).textContent = p.n;
      el("text", { x: 0, y: p.r + 61, "text-anchor": "middle", fill: p.ativo ? "#5DCAA5" : "#8fa3c4", "font-size": 13 }, p.G).textContent = p.st;
    });
    const parado = matchMedia("(prefers-reduced-motion: reduce)").matches;
    const t0 = performance.now();
    const quadro = agora => {
      if (fechado && !raiz.isConnected) return;
      const dt = parado ? 0 : agora - t0;
      A.forEach(p => {
        const a = p.a + dt * p.v, x = CX + p.rx * Math.cos(a), y = CY + p.ry * Math.sin(a);
        const atras = Math.sin(a) < -0.35; (atras ? TR : FR).appendChild(p.G);
        p.G.setAttribute("transform", `translate(${x.toFixed(1)} ${y.toFixed(1)}) scale(${atras ? .85 : 1})`);
        p.l.setAttribute("x1", CX); p.l.setAttribute("y1", CY); p.l.setAttribute("x2", x.toFixed(1)); p.l.setAttribute("y2", y.toFixed(1));
        if (p.pulso) { const f = (dt / 1800) % 1; p.pulso.setAttribute("cx", (CX + (x - CX) * f).toFixed(1)); p.pulso.setAttribute("cy", (CY + (y - CY) * f).toFixed(1)); }
        if (p.luas) p.luas.forEach((m, i) => { const b = dt * .0012 + i * 2 * Math.PI / p.luas.length; m.setAttribute("cx", (Math.cos(b) * (p.r + 17)).toFixed(1)); m.setAttribute("cy", (Math.sin(b) * (p.r * .45 + 8)).toFixed(1)); });
      });
      if (!parado) requestAnimationFrame(quadro);
    };
    requestAnimationFrame(quadro);
    raiz.querySelector(".eco-x").focus();
  }

  window.qsmart = { festa, ecossistema, armazenamento, downloads, chatFim, kv, cofre, rolar, ia, get, getH, req, reduzirImagem, imagem, nota, doc, grade, tela, lsGet, lsSet, lsDel, colarImagens, clicar, trocarUrl, pasta, baixar, abrir, copiar, abrirUrl, pdfPaginas, pdfJuntar, pdfLinhas, tema, aoOcultar, focar, rolarLinha, pronto };
})();
