// Intermediário do QSmart (Cloudflare Worker, plano gratuito).
// 1) Portal da Transparência: o Portal não aceita consultas direto do navegador (CORS).
// 2) IA do Mr. Smart (Groq): a chave fica guardada aqui, sem configurar cada máquina.
// 3) Contas, One a One, Férias, Smart Note, Feed e notificações: banco D1 (ligação "DB"); senhas com PBKDF2 e dados do 1:1 cifrados (AES-GCM).
// Segredos do worker: CHAVE_PORTAL, CHAVE_GROQ e CHAVE_DADOS.

const ORIGENS = ["https://biel1412.github.io", "http://127.0.0.1:5199", "http://localhost:5199"];
const PORTAL = "https://api.portaldatransparencia.gov.br";
const GROQ = "https://api.groq.com";
const ITERACOES = 100000;          // máximo aceito pelo PBKDF2 do Workers
const SESSAO_HORAS = 12;
const MAX_FALHAS = 5, TRAVA_MIN = 10;

export default {
  async fetch(req, env) {
    const origem = req.headers.get("Origin") || "";
    const permitida = ORIGENS.includes(origem);
    const cors = {
      "Access-Control-Allow-Origin": permitida ? origem : ORIGENS[0],
      "Access-Control-Allow-Methods": "GET, POST, PUT, DELETE, OPTIONS",
      "Access-Control-Allow-Headers": "chave-api-dados, Accept, Content-Type, Authorization",
      "Access-Control-Max-Age": "86400",
      "Vary": "Origin",
    };
    if (req.method === "OPTIONS") return new Response(null, { status: 204, headers: cors });
    if (!permitida) return new Response("Não permitido", { status: 403, headers: cors });

    const url = new URL(req.url);
    try {
      let r;
      if (url.pathname.startsWith("/api/")) {
        r = await api(req, env, url);
      } else if (url.pathname.startsWith("/api-de-dados/") && req.method === "GET") {
        const chave = req.headers.get("chave-api-dados") || env.CHAVE_PORTAL || "";
        r = await fetch(PORTAL + url.pathname + url.search, { headers: { "chave-api-dados": chave, "Accept": "application/json" } });
      } else if (url.pathname === "/groq/openai/v1/chat/completions" && req.method === "POST") {
        const recebida = (req.headers.get("Authorization") || "").replace(/^Bearer\s*/i, "").trim();
        const chave = recebida.startsWith("gsk_") ? recebida : (env.CHAVE_GROQ || "");
        r = await fetch(GROQ + "/openai/v1/chat/completions", {
          method: "POST",
          headers: { "Authorization": "Bearer " + chave, "Content-Type": "application/json" },
          body: await req.text(),
        });
      } else {
        r = json({ erro: "Caminho inválido" }, 404);
      }
      const h = new Headers(cors);
      h.set("Content-Type", r.headers.get("Content-Type") || "application/json");
      return new Response(r.body, { status: r.status, headers: h });
    } catch (e) {
      return new Response(JSON.stringify({ erro: "Falha no servidor: " + (e && e.message || e) }), { status: 500, headers: { ...cors, "Content-Type": "application/json" } });
    }
  },
};

// =============================== utilidades ===============================

const json = (obj, status = 200) => new Response(JSON.stringify(obj), { status, headers: { "Content-Type": "application/json" } });
const erro = (msg, status = 400, extra = {}) => json({ erro: msg, ...extra }, status);
const agora = () => new Date().toISOString();
const hojeBR = () => new Date(Date.now() - 3 * 3600e3).toISOString().slice(0, 10);   // dia no horário de Brasília
const enc = new TextEncoder(), dec = new TextDecoder();

const b64 = buf => { const b = new Uint8Array(buf); let s = ""; for (let i = 0; i < b.length; i += 0x8000) s += String.fromCharCode.apply(null, b.subarray(i, i + 0x8000)); return btoa(s); };
const deB64 = s => { const t = atob(s), b = new Uint8Array(t.length); for (let i = 0; i < t.length; i++) b[i] = t.charCodeAt(i); return b; };
const aleatorio = n => crypto.getRandomValues(new Uint8Array(n));

async function sha256(texto) { return b64(await crypto.subtle.digest("SHA-256", enc.encode(texto))); }

async function hashSenha(senha, sal = aleatorio(16), iter = ITERACOES) {
  const chave = await crypto.subtle.importKey("raw", enc.encode(senha), "PBKDF2", false, ["deriveBits"]);
  const bits = await crypto.subtle.deriveBits({ name: "PBKDF2", hash: "SHA-256", salt: sal, iterations: iter }, chave, 256);
  return `pbkdf2$${iter}$${b64(sal)}$${b64(bits)}`;
}

async function confere(senha, guardado) {
  const [tipo, iter, sal, h] = (guardado || "").split("$");
  if (tipo !== "pbkdf2") return false;
  const outro = await hashSenha(senha, deB64(sal), parseInt(iter, 10));
  const a = enc.encode(outro), b = enc.encode(guardado);
  if (a.length !== b.length) return false;
  let d = 0; for (let i = 0; i < a.length; i++) d |= a[i] ^ b[i];   // comparação em tempo constante
  return d === 0;
}

const ALFABETO = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
function novoCodigo() {
  const b = aleatorio(12); let s = "";
  for (let i = 0; i < 12; i++) { s += ALFABETO[b[i] % ALFABETO.length]; if (i === 3 || i === 7) s += "-"; }
  return s;
}
const normCodigo = c => (c || "").toUpperCase().replace(/[^A-Z0-9]/g, "");

let _chaveDados;
async function chaveDados(env) {
  if (!env.CHAVE_DADOS) throw new Error("CHAVE_DADOS não configurada");
  if (!_chaveDados) _chaveDados = await crypto.subtle.importKey("raw", deB64(env.CHAVE_DADOS), "AES-GCM", false, ["encrypt", "decrypt"]);
  return _chaveDados;
}
async function cifrar(env, texto) {
  if (!texto) return "";
  const iv = aleatorio(12);
  const ct = await crypto.subtle.encrypt({ name: "AES-GCM", iv }, await chaveDados(env), enc.encode(texto));
  const tudo = new Uint8Array(12 + ct.byteLength); tudo.set(iv); tudo.set(new Uint8Array(ct), 12);
  return "c1:" + b64(tudo);
}
async function decifrar(env, guardado) {
  if (!guardado || !guardado.startsWith("c1:")) return guardado || "";
  const tudo = deB64(guardado.slice(3));
  const pt = await crypto.subtle.decrypt({ name: "AES-GCM", iv: tudo.slice(0, 12) }, await chaveDados(env), tudo.slice(12));
  return dec.decode(pt);
}

async function corpo(req) { try { return await req.json(); } catch { return {}; } }

async function perfil(env, u) {
  return {
    id: u.id, usuario: u.usuario, nome: u.nome, sobrenome: u.sobrenome, papel: u.papel, lideranca: u.lideranca,
    status: u.status, foto: u.foto ? await decifrar(env, u.foto) : "", criadoEm: u.criado_em, ultimoAcesso: u.ultimo_acesso,
  };
}

async function logado(req, env) {
  const t = (req.headers.get("Authorization") || "").replace(/^Bearer\s*/i, "").trim();
  if (!t || t.startsWith("gsk_")) return null;
  const u = await env.DB.prepare(
    "SELECT u.* FROM sessoes s JOIN usuarios u ON u.id = s.usuario_id WHERE s.token = ? AND s.expira > ? AND u.status = 'ativo'"
  ).bind(await sha256(t), agora()).first();
  return u || null;
}

async function marcarDia(env, id) {
  await env.DB.prepare("INSERT OR IGNORE INTO dias (usuario_id, dia) VALUES (?, ?)").bind(id, hojeBR()).run();
}

const podeVerTudo = u => u.papel === "admin" || u.papel === "coordenador";

// =============================== rotas /api ===============================

async function api(req, env, url) {
  if (!env.DB) return erro("Banco não ligado ao intermediário (ligação DB).", 500);
  const rota = url.pathname.slice(5);   // sem "/api/"
  const m = req.method;

  // ---------- públicas ----------
  if (rota === "cadastro" && m === "POST") {
    const b = await corpo(req);
    const usuario = String(b.usuario || "").trim().toLowerCase();
    const nome = String(b.nome || "").trim(), sobrenome = String(b.sobrenome || "").trim(), senha = String(b.senha || "");
    if (!/^[a-z0-9._-]{3,30}$/.test(usuario)) return erro("Usuário: 3 a 30 caracteres, só letras sem acento, números, ponto, traço ou sublinhado.");
    if (nome.length < 2 || sobrenome.length < 2) return erro("Preencha nome e sobrenome.");
    if (senha.length < 8) return erro("A senha precisa ter pelo menos 8 caracteres.");
    if (await env.DB.prepare("SELECT 1 FROM usuarios WHERE usuario = ?").bind(usuario).first()) return erro("Esse usuário já existe. Escolha outro.", 409);
    const primeiro = !(await env.DB.prepare("SELECT 1 FROM usuarios LIMIT 1").first());
    const codigo = novoCodigo();
    await env.DB.prepare(
      "INSERT INTO usuarios (usuario, nome, sobrenome, senha, recuperacao, papel, status, criado_em) VALUES (?, ?, ?, ?, ?, ?, ?, ?)"
    ).bind(usuario, nome.slice(0, 60), sobrenome.slice(0, 80), await hashSenha(senha), await hashSenha(normCodigo(codigo)),
      primeiro ? "admin" : "usuario", primeiro ? "ativo" : "pendente", agora()).run();
    return json({ codigo, status: primeiro ? "ativo" : "pendente", admin: primeiro });
  }

  if ((rota === "entrar" || rota === "recuperar") && m === "POST") {
    const b = await corpo(req);
    const u = await env.DB.prepare("SELECT * FROM usuarios WHERE usuario = ?").bind(String(b.usuario || "").trim().toLowerCase()).first();
    const generico = rota === "entrar" ? "Usuário ou senha incorretos." : "Usuário ou código de recuperação incorretos.";
    if (!u) { await hashSenha("x"); return erro(generico, 401); }
    if (u.travado_ate && u.travado_ate > agora()) {
      const min = Math.max(1, Math.ceil((new Date(u.travado_ate).getTime() - Date.now()) / 60000));
      return erro(`Muitas tentativas erradas. Tente de novo em ${min} min.`, 429);
    }
    const ok = rota === "entrar" ? await confere(String(b.senha || ""), u.senha) : await confere(normCodigo(b.codigo), u.recuperacao);
    if (!ok) {
      const falhas = (u.falhas || 0) + 1;
      if (falhas >= MAX_FALHAS)
        await env.DB.prepare("UPDATE usuarios SET falhas = 0, travado_ate = ? WHERE id = ?").bind(new Date(Date.now() + TRAVA_MIN * 60000).toISOString(), u.id).run();
      else await env.DB.prepare("UPDATE usuarios SET falhas = ? WHERE id = ?").bind(falhas, u.id).run();
      return erro(generico, 401);
    }
    if (rota === "recuperar") {
      const nova = String(b.nova || "");
      if (nova.length < 8) return erro("A nova senha precisa ter pelo menos 8 caracteres.");
      const codigo = novoCodigo();
      await env.DB.batch([
        env.DB.prepare("UPDATE usuarios SET senha = ?, recuperacao = ?, falhas = 0, travado_ate = NULL WHERE id = ?").bind(await hashSenha(nova), await hashSenha(normCodigo(codigo)), u.id),
        env.DB.prepare("DELETE FROM sessoes WHERE usuario_id = ?").bind(u.id),
      ]);
      return json({ ok: true, codigo, status: u.status });
    }
    if (u.status === "pendente") return erro("Seu cadastro ainda está aguardando a aprovação do administrador.", 403, { motivo: "pendente" });
    if (u.status !== "ativo") return erro("Seu acesso está bloqueado. Fale com o administrador.", 403, { motivo: "bloqueado" });
    const bytes = aleatorio(32);
    const token = b64(bytes).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
    const expira = new Date(Date.now() + SESSAO_HORAS * 3600e3).toISOString();
    await env.DB.batch([
      env.DB.prepare("INSERT INTO sessoes (token, usuario_id, expira) VALUES (?, ?, ?)").bind(await sha256(token), u.id, expira),
      env.DB.prepare("UPDATE usuarios SET falhas = 0, travado_ate = NULL, ultimo_acesso = ? WHERE id = ?").bind(agora(), u.id),
      env.DB.prepare("DELETE FROM sessoes WHERE expira < ?").bind(agora()),
    ]);
    await marcarDia(env, u.id);
    return json({ token, expira, perfil: await perfil(env, u) });
  }

  // ---------- Smart Note compartilhado: só leitura, sem login, por link ----------
  if (rota === "publico/caderno" || rota === "publico/pagina") {
    if (m !== "GET") return erro("Método inválido.", 405);
    const t = (url.searchParams.get("t") || "").slice(0, 80);
    const l = t.length >= 20 ? await env.DB.prepare("SELECT caderno_id, usuario_id FROM sn_links WHERE token = ?").bind(await sha256(t)).first() : null;
    if (!l) return erro("Este link não existe mais ou foi desativado por quem compartilhou.", 404);
    const c = await env.DB.prepare("SELECT nome, cor FROM sn_cadernos WHERE id = ? AND usuario_id = ?").bind(l.caderno_id, l.usuario_id).first();
    if (!c) return erro("Este caderno não existe mais.", 404);
    if (rota === "publico/pagina") {
      const x = await env.DB.prepare("SELECT conteudo FROM sn_paginas WHERE id = ? AND caderno_id = ? AND usuario_id = ?").bind(url.searchParams.get("id") || "", l.caderno_id, l.usuario_id).first();
      if (!x) return erro("Página não encontrada.", 404);
      return json({ conteudo: await decifrar(env, x.conteudo) });
    }
    const a = await env.DB.prepare("SELECT nome, sobrenome FROM usuarios WHERE id = ?").bind(l.usuario_id).first();
    const p = await env.DB.prepare("SELECT id, pai_id, titulo, ordem, criado_em, atualizado_em FROM sn_paginas WHERE caderno_id = ? AND usuario_id = ? ORDER BY ordem, criado_em").bind(l.caderno_id, l.usuario_id).all();
    const paginas = [];
    for (const x of p.results || []) paginas.push({ id: x.id, pai: x.pai_id || "", titulo: await decifrar(env, x.titulo), ordem: x.ordem, criadoEm: x.criado_em, atualizadoEm: x.atualizado_em });
    return json({ nome: await decifrar(env, c.nome), cor: c.cor || "", autor: a ? `${a.nome} ${a.sobrenome}`.trim() : "", paginas });
  }

  // ---------- daqui para baixo, só logado ----------
  const u = await logado(req, env);
  if (!u) return erro("Sessão expirada. Entre de novo.", 401, { motivo: "sessao" });

  if (rota === "sair" && m === "POST") {
    const t = (req.headers.get("Authorization") || "").replace(/^Bearer\s*/i, "").trim();
    await env.DB.prepare("DELETE FROM sessoes WHERE token = ?").bind(await sha256(t)).run();
    return json({ ok: true });
  }

  if (rota === "eu" && m === "GET") {
    const uso = await env.DB.prepare("SELECT chave, qtd, primeiro, ultimo FROM uso WHERE usuario_id = ?").bind(u.id).all();
    const dias = await env.DB.prepare("SELECT dia FROM dias WHERE usuario_id = ? ORDER BY dia DESC LIMIT 500").bind(u.id).all();
    return json({ perfil: await perfil(env, u), uso: uso.results || [], dias: (dias.results || []).map(d => d.dia) });
  }

  if (rota === "perfil" && m === "POST") {
    const b = await corpo(req);
    if (typeof b.foto === "string") {
      if (b.foto.length > 300000) return erro("Foto grande demais.");
      if (b.foto && !b.foto.startsWith("data:image/")) return erro("Formato de foto inválido.");
      await env.DB.prepare("UPDATE usuarios SET foto = ? WHERE id = ?").bind(await cifrar(env, b.foto), u.id).run();
    }
    return json({ ok: true });
  }

  if (rota === "senha" && m === "POST") {
    const b = await corpo(req);
    if (!(await confere(String(b.atual || ""), u.senha))) return erro("A senha atual não confere.", 401);
    if (String(b.nova || "").length < 8) return erro("A nova senha precisa ter pelo menos 8 caracteres.");
    await env.DB.prepare("UPDATE usuarios SET senha = ? WHERE id = ?").bind(await hashSenha(String(b.nova)), u.id).run();
    return json({ ok: true });
  }

  if (rota === "uso" && m === "POST") {
    const b = await corpo(req);
    const chave = String(b.chave || "").slice(0, 40);
    if (!/^[a-z0-9_.-]+$/.test(chave)) return erro("Chave inválida.");
    const n = Math.max(1, Math.min(50, parseInt(b.qtd || 1, 10) || 1));
    await env.DB.batch([
      env.DB.prepare("INSERT INTO uso (usuario_id, chave, qtd, primeiro, ultimo) VALUES (?, ?, ?, ?, ?) ON CONFLICT(usuario_id, chave) DO UPDATE SET qtd = qtd + excluded.qtd, ultimo = excluded.ultimo")
        .bind(u.id, chave, n, agora(), agora()),
      env.DB.prepare("INSERT OR IGNORE INTO dias (usuario_id, dia) VALUES (?, ?)").bind(u.id, hojeBR()),
    ]);
    return json({ ok: true });
  }

  // ---------- administração ----------
  if (rota.startsWith("admin/")) {
    if (u.papel !== "admin") return erro("Só o administrador.", 403);
    if (rota === "admin/usuarios" && m === "GET") {
      const r = await env.DB.prepare("SELECT id, usuario, nome, sobrenome, papel, lideranca, status, criado_em, ultimo_acesso FROM usuarios ORDER BY status = 'pendente' DESC, nome").all();
      return json({ usuarios: r.results || [] });
    }
    if (rota === "admin/usuario" && m === "POST") {
      const b = await corpo(req);
      const alvo = await env.DB.prepare("SELECT * FROM usuarios WHERE id = ?").bind(b.id).first();
      if (!alvo) return erro("Usuário não encontrado.", 404);
      if (alvo.id === u.id && ((b.papel && b.papel !== "admin") || (b.status && b.status !== "ativo"))) return erro("Você não pode tirar o seu próprio acesso de administrador.");
      const papel = ["admin", "coordenador", "lideranca", "usuario"].includes(b.papel) ? b.papel : alvo.papel;
      const status = ["ativo", "pendente", "bloqueado"].includes(b.status) ? b.status : alvo.status;
      const lideranca = typeof b.lideranca === "string" ? b.lideranca.trim().slice(0, 80) : alvo.lideranca;
      await env.DB.prepare("UPDATE usuarios SET papel = ?, status = ?, lideranca = ? WHERE id = ?").bind(papel, status, lideranca, alvo.id).run();
      if (status !== "ativo") await env.DB.prepare("DELETE FROM sessoes WHERE usuario_id = ?").bind(alvo.id).run();
      return json({ ok: true });
    }
    if (rota === "admin/recuperacao" && m === "POST") {
      const b = await corpo(req);
      const codigo = novoCodigo();
      const r = await env.DB.prepare("UPDATE usuarios SET recuperacao = ?, falhas = 0, travado_ate = NULL WHERE id = ?").bind(await hashSenha(normCodigo(codigo)), b.id).run();
      if (!r.meta || !r.meta.changes) return erro("Usuário não encontrado.", 404);
      return json({ codigo });
    }
    if (rota === "admin/excluir" && m === "POST") {
      const b = await corpo(req);
      if (b.id === u.id) return erro("Você não pode excluir a sua própria conta.");
      await env.DB.batch([
        env.DB.prepare("DELETE FROM sessoes WHERE usuario_id = ?").bind(b.id),
        env.DB.prepare("DELETE FROM uso WHERE usuario_id = ?").bind(b.id),
        env.DB.prepare("DELETE FROM dias WHERE usuario_id = ?").bind(b.id),
        env.DB.prepare("DELETE FROM sn_paginas WHERE usuario_id = ?").bind(b.id),
        env.DB.prepare("DELETE FROM sn_cadernos WHERE usuario_id = ?").bind(b.id),
        env.DB.prepare("DELETE FROM sn_links WHERE usuario_id = ?").bind(b.id),
        env.DB.prepare("DELETE FROM notificacoes WHERE usuario_id = ? OR de_id = ?").bind(b.id, b.id),
        env.DB.prepare("DELETE FROM feed_curtidas WHERE usuario_id = ?").bind(b.id),
        env.DB.prepare("DELETE FROM feed_comentarios WHERE usuario_id = ?").bind(b.id),
        env.DB.prepare("DELETE FROM usuarios WHERE id = ?").bind(b.id),
      ]);
      return json({ ok: true });
    }
    return erro("Caminho inválido", 404);
  }

  // ---------- One a One ----------
  if (rota === "oo" || rota === "oo/lote") {
    if (!podeVerTudo(u) && !(u.papel === "lideranca" && u.lideranca)) return erro("Seu acesso não inclui o One a One. Fale com o administrador.", 403);
    const minha = l => podeVerTudo(u) || (l || "") === u.lideranca;

    if (rota === "oo" && m === "GET") {
      const r = podeVerTudo(u)
        ? await env.DB.prepare("SELECT id, lideranca, dados, atualizado_em FROM oo_pessoas").all()
        : await env.DB.prepare("SELECT id, lideranca, dados, atualizado_em FROM oo_pessoas WHERE lideranca = ?").bind(u.lideranca).all();
      const pessoas = [];
      for (const p of r.results || []) pessoas.push({ id: p.id, lideranca: p.lideranca, dados: await decifrar(env, p.dados), atualizadoEm: p.atualizado_em });
      return json({ pessoas, veTudo: podeVerTudo(u), lideranca: u.lideranca });
    }

    const salvar = async p => {
      const id = String(p.id || "").slice(0, 64), lid = String(p.lideranca || "").trim().slice(0, 80);
      if (!id || typeof p.dados !== "string") return "Registro inválido.";
      if (p.dados.length > 400000) return "Registro grande demais.";
      if (!minha(lid)) return "Você só pode salvar pessoas da sua equipe.";
      const antes = await env.DB.prepare("SELECT lideranca FROM oo_pessoas WHERE id = ?").bind(id).first();
      if (antes && !minha(antes.lideranca)) return "Essa pessoa é de outra equipe.";
      await env.DB.prepare("INSERT INTO oo_pessoas (id, lideranca, dados, atualizado_em, atualizado_por) VALUES (?, ?, ?, ?, ?) ON CONFLICT(id) DO UPDATE SET lideranca = excluded.lideranca, dados = excluded.dados, atualizado_em = excluded.atualizado_em, atualizado_por = excluded.atualizado_por")
        .bind(id, lid, await cifrar(env, p.dados), agora(), u.id).run();
      return null;
    };

    if (rota === "oo" && m === "PUT") {
      const falha = await salvar(await corpo(req));
      return falha ? erro(falha, 403) : json({ ok: true, em: agora() });
    }
    if (rota === "oo/lote" && m === "POST") {
      const b = await corpo(req);
      const lista = Array.isArray(b.pessoas) ? b.pessoas.slice(0, 2000) : [];
      let ok = 0; const falhas = [];
      for (const p of lista) { const f = await salvar(p); if (f) falhas.push(f); else ok++; }
      return json({ ok, falhas: falhas.length });
    }
    if (rota === "oo" && m === "DELETE") {
      const id = url.searchParams.get("id") || "";
      const antes = await env.DB.prepare("SELECT lideranca FROM oo_pessoas WHERE id = ?").bind(id).first();
      if (!antes) return json({ ok: true });
      if (!minha(antes.lideranca)) return erro("Essa pessoa é de outra equipe.", 403);
      await env.DB.prepare("DELETE FROM oo_pessoas WHERE id = ?").bind(id).run();
      return json({ ok: true });
    }
  }

  // ---------- Férias (mesmas pessoas do One a One; a equipe vem da pessoa no One a One) ----------
  if (rota === "fe") {
    if (!podeVerTudo(u) && !(u.papel === "lideranca" && u.lideranca)) return erro("Seu acesso não inclui as Férias. Fale com o administrador.", 403);
    const equipeDe = async id => {
      const p = await env.DB.prepare("SELECT lideranca FROM oo_pessoas WHERE id = ?").bind(id).first();
      return p ? (p.lideranca || "") : null;
    };
    const pode = l => podeVerTudo(u) || (l || "") === u.lideranca;

    if (m === "GET") {
      const r = podeVerTudo(u)
        ? await env.DB.prepare("SELECT f.id, f.dados, f.atualizado_em FROM fe_pessoas f").all()
        : await env.DB.prepare("SELECT f.id, f.dados, f.atualizado_em FROM fe_pessoas f JOIN oo_pessoas o ON o.id = f.id WHERE o.lideranca = ?").bind(u.lideranca).all();
      const pessoas = [];
      for (const p of r.results || []) pessoas.push({ id: p.id, dados: await decifrar(env, p.dados), atualizadoEm: p.atualizado_em });
      return json({ pessoas, veTudo: podeVerTudo(u), lideranca: u.lideranca });
    }
    if (m === "PUT") {
      const b = await corpo(req);
      const id = String(b.id || "").slice(0, 64);
      if (!id || typeof b.dados !== "string" || b.dados.length > 200000) return erro("Registro inválido.");
      const l = await equipeDe(id);
      if (l === null) return erro("Pessoa não encontrada no One a One.", 404);
      if (!pode(l)) return erro("Essa pessoa é de outra equipe.", 403);
      await env.DB.prepare("INSERT INTO fe_pessoas (id, dados, atualizado_em, atualizado_por) VALUES (?, ?, ?, ?) ON CONFLICT(id) DO UPDATE SET dados = excluded.dados, atualizado_em = excluded.atualizado_em, atualizado_por = excluded.atualizado_por")
        .bind(id, await cifrar(env, b.dados), agora(), u.id).run();
      return json({ ok: true, em: agora() });
    }
    if (m === "DELETE") {
      const id = url.searchParams.get("id") || "";
      const l = await equipeDe(id);
      if (l !== null && !pode(l)) return erro("Essa pessoa é de outra equipe.", 403);
      await env.DB.prepare("DELETE FROM fe_pessoas WHERE id = ?").bind(id).run();
      return json({ ok: true });
    }
  }

  // ---------- Smart Note: cadernos e páginas de cada usuário (só o dono vê) ----------
  if (rota.startsWith("sn")) {
    const MAX_PAG = 1450000;
    if (rota === "sn" && m === "GET") {
      const c = await env.DB.prepare("SELECT c.id, c.nome, c.cor, c.ordem, (SELECT l.token_cifrado FROM sn_links l WHERE l.caderno_id = c.id LIMIT 1) AS link FROM sn_cadernos c WHERE c.usuario_id = ? ORDER BY c.ordem, c.criado_em").bind(u.id).all();
      const p = await env.DB.prepare("SELECT id, caderno_id, pai_id, titulo, ordem, criado_em, atualizado_em, length(conteudo) AS tam FROM sn_paginas WHERE usuario_id = ? ORDER BY ordem, criado_em").bind(u.id).all();
      const cadernos = [], paginas = [];
      for (const x of c.results || []) cadernos.push({ id: x.id, nome: await decifrar(env, x.nome), cor: x.cor || "", ordem: x.ordem, link: x.link ? await decifrar(env, x.link) : "" });
      for (const x of p.results || []) paginas.push({ id: x.id, caderno: x.caderno_id, pai: x.pai_id || "", titulo: await decifrar(env, x.titulo), ordem: x.ordem, criadoEm: x.criado_em, atualizadoEm: x.atualizado_em, tam: x.tam || 0 });
      return json({ cadernos, paginas });
    }
    if (rota === "sn/pagina" && m === "GET") {
      const x = await env.DB.prepare("SELECT conteudo FROM sn_paginas WHERE id = ? AND usuario_id = ?").bind(url.searchParams.get("id") || "", u.id).first();
      if (!x) return erro("Página não encontrada.", 404);
      return json({ conteudo: await decifrar(env, x.conteudo) });
    }
    if (rota === "sn/caderno" && m === "PUT") {
      const b = await corpo(req);
      const id = String(b.id || "").slice(0, 40), nome = String(b.nome || "").trim().slice(0, 120);
      if (!id || !nome) return erro("Caderno inválido.");
      const dono = await env.DB.prepare("SELECT usuario_id FROM sn_cadernos WHERE id = ?").bind(id).first();
      if (dono && dono.usuario_id !== u.id) return erro("Caderno inválido.", 403);
      await env.DB.prepare("INSERT INTO sn_cadernos (id, usuario_id, nome, cor, ordem, criado_em) VALUES (?, ?, ?, ?, ?, ?) ON CONFLICT(id) DO UPDATE SET nome = excluded.nome, cor = excluded.cor, ordem = excluded.ordem")
        .bind(id, u.id, await cifrar(env, nome), String(b.cor || "").slice(0, 20), Number(b.ordem) || 0, agora()).run();
      return json({ ok: true });
    }
    if (rota === "sn/link" && m === "POST") {
      // gera um link novo (o antigo deixa de funcionar); só o hash fica no banco
      const b = await corpo(req); const cad = String(b.caderno || "");
      const c = await env.DB.prepare("SELECT id FROM sn_cadernos WHERE id = ? AND usuario_id = ?").bind(cad, u.id).first();
      if (!c) return erro("Caderno não encontrado.", 404);
      const bytes = crypto.getRandomValues(new Uint8Array(24));
      const token = b64(bytes).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
      await env.DB.batch([
        env.DB.prepare("DELETE FROM sn_links WHERE caderno_id = ? AND usuario_id = ?").bind(cad, u.id),
        env.DB.prepare("INSERT INTO sn_links (token, caderno_id, usuario_id, criado_em, token_cifrado) VALUES (?, ?, ?, ?, ?)").bind(await sha256(token), cad, u.id, agora(), await cifrar(env, token)),
      ]);
      return json({ ok: true, token });
    }
    if (rota === "sn/link" && m === "DELETE") {
      await env.DB.prepare("DELETE FROM sn_links WHERE caderno_id = ? AND usuario_id = ?").bind(url.searchParams.get("caderno") || "", u.id).run();
      return json({ ok: true });
    }
    if (rota === "sn/caderno" && m === "DELETE") {
      const id = url.searchParams.get("id") || "";
      await env.DB.batch([
        env.DB.prepare("DELETE FROM sn_paginas WHERE caderno_id = ? AND usuario_id = ?").bind(id, u.id),
        env.DB.prepare("DELETE FROM sn_links WHERE caderno_id = ? AND usuario_id = ?").bind(id, u.id),
        env.DB.prepare("DELETE FROM sn_cadernos WHERE id = ? AND usuario_id = ?").bind(id, u.id),
      ]);
      return json({ ok: true });
    }
    if (rota === "sn/pagina" && m === "PUT") {
      const b = await corpo(req);
      const id = String(b.id || "").slice(0, 40), cad = String(b.caderno || "").slice(0, 40), pai = String(b.pai || "").slice(0, 40);
      if (!id || !cad) return erro("Página inválida.");
      if (typeof b.conteudo === "string" && b.conteudo.length > MAX_PAG) return erro("A página ficou grande demais (muitas imagens). Crie uma subpágina para continuar.", 413);
      const c = await env.DB.prepare("SELECT id FROM sn_cadernos WHERE id = ? AND usuario_id = ?").bind(cad, u.id).first();
      if (!c) return erro("Caderno não encontrado.", 404);
      const ex = await env.DB.prepare("SELECT usuario_id FROM sn_paginas WHERE id = ?").bind(id).first();
      if (ex && ex.usuario_id !== u.id) return erro("Página inválida.", 403);
      const titulo = await cifrar(env, String(b.titulo || "").slice(0, 200));
      const em = agora();
      if (!ex) {
        await env.DB.prepare("INSERT INTO sn_paginas (id, usuario_id, caderno_id, pai_id, titulo, conteudo, ordem, criado_em, atualizado_em) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)")
          .bind(id, u.id, cad, pai, titulo, await cifrar(env, typeof b.conteudo === "string" ? b.conteudo : ""), Number(b.ordem) || 0, em, em).run();
      } else if (typeof b.conteudo === "string") {
        await env.DB.prepare("UPDATE sn_paginas SET caderno_id = ?, pai_id = ?, titulo = ?, conteudo = ?, ordem = ?, atualizado_em = ? WHERE id = ? AND usuario_id = ?")
          .bind(cad, pai, titulo, await cifrar(env, b.conteudo), Number(b.ordem) || 0, em, id, u.id).run();
      } else {
        await env.DB.prepare("UPDATE sn_paginas SET caderno_id = ?, pai_id = ?, titulo = ?, ordem = ?, atualizado_em = ? WHERE id = ? AND usuario_id = ?")
          .bind(cad, pai, titulo, Number(b.ordem) || 0, em, id, u.id).run();
      }
      return json({ ok: true, em });
    }
    if (rota === "sn/pagina" && m === "DELETE") {
      const id = url.searchParams.get("id") || "";
      await env.DB.prepare("DELETE FROM sn_paginas WHERE usuario_id = ?1 AND id IN (WITH RECURSIVE d(id) AS (SELECT ?2 UNION SELECT p.id FROM sn_paginas p JOIN d ON p.pai_id = d.id WHERE p.usuario_id = ?1) SELECT id FROM d)")
        .bind(u.id, id).run();
      return json({ ok: true });
    }
  }

  // ---------- Feed: todos postam, todos veem, curtem e comentam ----------
  if (rota.startsWith("feed")) {
    const autores = async ids => {
      const r = {};
      for (const id of [...new Set(ids)]) {
        const a = await env.DB.prepare("SELECT id, usuario, nome, sobrenome, papel, foto FROM usuarios WHERE id = ?").bind(id).first();
        r[id] = a ? { id: a.id, usuario: a.usuario, nome: `${a.nome} ${a.sobrenome}`.trim(), papel: a.papel, foto: a.foto ? await decifrar(env, a.foto) : "" }
                  : { id, usuario: "", nome: "Usuário removido", papel: "", foto: "" };
      }
      return r;
    };
    const podeApagar = dono => dono === u.id || u.papel === "admin";

    if (rota === "feed" && m === "GET") {
      const antes = Number(url.searchParams.get("antes")) || 1e15, so = Number(url.searchParams.get("post")) || 0;
      const r = so
        ? await env.DB.prepare("SELECT * FROM feed_posts WHERE id = ?").bind(so).all()
        : await env.DB.prepare("SELECT * FROM feed_posts WHERE id < ? ORDER BY id DESC LIMIT 10").bind(antes).all();
      const posts = [], ids = [];
      for (const p of r.results || []) {
        const cur = await env.DB.prepare("SELECT usuario_id FROM feed_curtidas WHERE post_id = ? ORDER BY criado_em").bind(p.id).all();
        const com = await env.DB.prepare("SELECT id, usuario_id, texto, criado_em FROM feed_comentarios WHERE post_id = ? ORDER BY id").bind(p.id).all();
        const comentarios = [];
        for (const c of com.results || []) { comentarios.push({ id: c.id, autor: c.usuario_id, texto: await decifrar(env, c.texto), criadoEm: c.criado_em }); ids.push(c.usuario_id); }
        const curt = (cur.results || []).map(x => x.usuario_id);
        let imagens = [];
        try { imagens = JSON.parse(await decifrar(env, p.imagens) || "[]"); } catch { }
        posts.push({ id: p.id, autor: p.usuario_id, texto: await decifrar(env, p.texto), imagens, criadoEm: p.criado_em, curtidas: curt.length, euCurti: curt.includes(u.id), quemCurtiu: curt.slice(0, 30), comentarios });
        ids.push(p.usuario_id, ...curt.slice(0, 30));
      }
      return json({ posts, autores: await autores(ids), fim: !so && (r.results || []).length < 10 });
    }
    if (rota === "feed" && m === "POST") {
      const b = await corpo(req);
      const texto = String(b.texto || "").trim().slice(0, 5000);
      const imagens = Array.isArray(b.imagens) ? b.imagens.filter(x => typeof x === "string" && x.startsWith("data:image/")).slice(0, 4) : [];
      if (!texto && imagens.length === 0) return erro("Escreva algo ou escolha uma imagem.");
      if (imagens.reduce((t, x) => t + x.length, 0) > 1400000) return erro("As imagens ficaram grandes demais. Tente com menos imagens.", 413);
      const em = agora();
      const r = await env.DB.prepare("INSERT INTO feed_posts (usuario_id, texto, imagens, criado_em) VALUES (?, ?, ?, ?)")
        .bind(u.id, await cifrar(env, texto), imagens.length ? await cifrar(env, JSON.stringify(imagens)) : "", em).run();
      const id = r.meta.last_row_id;
      await env.DB.prepare("INSERT INTO notificacoes (usuario_id, tipo, de_id, post_id, criado_em) SELECT id, 'post', ?, ?, ? FROM usuarios WHERE status = 'ativo' AND id != ?")
        .bind(u.id, id, em, u.id).run();
      return json({ ok: true, id });
    }
    if (rota === "feed" && m === "DELETE") {
      const id = Number(url.searchParams.get("id")) || 0;
      const p = await env.DB.prepare("SELECT usuario_id FROM feed_posts WHERE id = ?").bind(id).first();
      if (!p) return json({ ok: true });
      if (!podeApagar(p.usuario_id)) return erro("Só quem publicou pode apagar.", 403);
      await env.DB.batch([
        env.DB.prepare("DELETE FROM feed_curtidas WHERE post_id = ?").bind(id),
        env.DB.prepare("DELETE FROM feed_comentarios WHERE post_id = ?").bind(id),
        env.DB.prepare("DELETE FROM notificacoes WHERE post_id = ?").bind(id),
        env.DB.prepare("DELETE FROM feed_posts WHERE id = ?").bind(id),
      ]);
      return json({ ok: true });
    }
    if (rota === "feed/curtir" && m === "POST") {
      const b = await corpo(req); const id = Number(b.id) || 0;
      const p = await env.DB.prepare("SELECT usuario_id FROM feed_posts WHERE id = ?").bind(id).first();
      if (!p) return erro("Publicação não encontrada.", 404);
      const ja = await env.DB.prepare("SELECT 1 AS x FROM feed_curtidas WHERE post_id = ? AND usuario_id = ?").bind(id, u.id).first();
      if (ja) {
        await env.DB.batch([
          env.DB.prepare("DELETE FROM feed_curtidas WHERE post_id = ? AND usuario_id = ?").bind(id, u.id),
          env.DB.prepare("DELETE FROM notificacoes WHERE post_id = ? AND de_id = ? AND tipo = 'curtida'").bind(id, u.id),
        ]);
      } else {
        const em = agora();
        const q = [env.DB.prepare("INSERT OR IGNORE INTO feed_curtidas (post_id, usuario_id, criado_em) VALUES (?, ?, ?)").bind(id, u.id, em)];
        if (p.usuario_id !== u.id) q.push(env.DB.prepare("INSERT INTO notificacoes (usuario_id, tipo, de_id, post_id, criado_em) VALUES (?, 'curtida', ?, ?, ?)").bind(p.usuario_id, u.id, id, em));
        await env.DB.batch(q);
      }
      const n = await env.DB.prepare("SELECT COUNT(*) AS n FROM feed_curtidas WHERE post_id = ?").bind(id).first();
      return json({ ok: true, curtiu: !ja, curtidas: n.n });
    }
    if (rota === "feed/comentar" && m === "POST") {
      const b = await corpo(req); const id = Number(b.id) || 0;
      const texto = String(b.texto || "").trim().slice(0, 2000);
      if (!texto) return erro("Escreva o comentário.");
      const p = await env.DB.prepare("SELECT usuario_id FROM feed_posts WHERE id = ?").bind(id).first();
      if (!p) return erro("Publicação não encontrada.", 404);
      const em = agora();
      const r = await env.DB.prepare("INSERT INTO feed_comentarios (post_id, usuario_id, texto, criado_em) VALUES (?, ?, ?, ?)").bind(id, u.id, await cifrar(env, texto), em).run();
      // avisa quem publicou e quem já comentou (menos eu)
      const outros = await env.DB.prepare("SELECT DISTINCT usuario_id FROM feed_comentarios WHERE post_id = ? AND usuario_id NOT IN (?, ?)").bind(id, u.id, p.usuario_id).all();
      const q = [];
      if (p.usuario_id !== u.id) q.push(env.DB.prepare("INSERT INTO notificacoes (usuario_id, tipo, de_id, post_id, criado_em) VALUES (?, 'comentario', ?, ?, ?)").bind(p.usuario_id, u.id, id, em));
      for (const o of outros.results || []) q.push(env.DB.prepare("INSERT INTO notificacoes (usuario_id, tipo, de_id, post_id, criado_em) VALUES (?, 'resposta', ?, ?, ?)").bind(o.usuario_id, u.id, id, em));
      if (q.length) await env.DB.batch(q);
      return json({ ok: true, id: r.meta.last_row_id, criadoEm: em });
    }
    if (rota === "feed/comentario" && m === "DELETE") {
      const id = Number(url.searchParams.get("id")) || 0;
      const c = await env.DB.prepare("SELECT usuario_id FROM feed_comentarios WHERE id = ?").bind(id).first();
      if (c && !podeApagar(c.usuario_id)) return erro("Só quem comentou pode apagar.", 403);
      await env.DB.prepare("DELETE FROM feed_comentarios WHERE id = ?").bind(id).run();
      return json({ ok: true });
    }
  }

  // ---------- Notificações (sininho) ----------
  if (rota === "notif" && m === "GET") {
    await env.DB.prepare("DELETE FROM notificacoes WHERE usuario_id = ? AND criado_em < ?").bind(u.id, new Date(Date.now() - 60 * 864e5).toISOString()).run();
    const r = await env.DB.prepare(
      "SELECT n.id, n.tipo, n.post_id, n.criado_em, n.lida, n.de_id, d.nome, d.sobrenome, d.foto, p.texto AS ptexto FROM notificacoes n LEFT JOIN usuarios d ON d.id = n.de_id LEFT JOIN feed_posts p ON p.id = n.post_id WHERE n.usuario_id = ? ORDER BY n.id DESC LIMIT 30"
    ).bind(u.id).all();
    const itens = [], fotos = {};
    for (const x of r.results || []) {
      if (x.de_id && !(x.de_id in fotos)) fotos[x.de_id] = x.foto ? await decifrar(env, x.foto) : "";
      const t = x.ptexto ? (await decifrar(env, x.ptexto)).replace(/\s+/g, " ").trim() : "";
      itens.push({ id: x.id, tipo: x.tipo, postId: x.post_id, criadoEm: x.criado_em, lida: !!x.lida, de: x.de_id, nome: x.nome ? `${x.nome} ${x.sobrenome}`.trim() : "Alguém", trecho: t.length > 70 ? t.slice(0, 70) + "…" : t });
    }
    const n = await env.DB.prepare("SELECT COUNT(*) AS n FROM notificacoes WHERE usuario_id = ? AND lida = 0").bind(u.id).first();
    return json({ itens, fotos, naoLidas: n.n });
  }
  if (rota === "notif/lidas" && m === "POST") {
    await env.DB.prepare("UPDATE notificacoes SET lida = 1 WHERE usuario_id = ? AND lida = 0").bind(u.id).run();
    return json({ ok: true });
  }

  return erro("Caminho inválido", 404);
}
