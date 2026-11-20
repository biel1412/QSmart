// Intermediário do QSmart (Cloudflare Worker, plano gratuito).
// 1) Portal da Transparência: o Portal não aceita consultas direto do navegador (CORS).
// 2) IA do Mr. Smart (Groq): a chave fica guardada aqui, sem configurar cada máquina.
// 3) Contas, One a One e Férias: banco D1 (ligação "DB"); senhas com PBKDF2 e dados do 1:1 cifrados (AES-GCM).
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

const b64 = buf => { let s = ""; const b = new Uint8Array(buf); for (let i = 0; i < b.length; i++) s += String.fromCharCode(b[i]); return btoa(s); };
const deB64 = s => Uint8Array.from(atob(s), c => c.charCodeAt(0));
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

  return erro("Caminho inválido", 404);
}
