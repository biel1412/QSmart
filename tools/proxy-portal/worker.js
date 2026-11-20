// Intermediário do QSmart (Cloudflare Worker, plano gratuito).
// 1) Portal da Transparência: o Portal não aceita consultas direto do navegador (CORS).
// 2) IA do Mr. Smart (Groq): a chave fica guardada aqui, sem precisar configurar cada máquina.
// Segredos do worker (Settings > Variables and Secrets): CHAVE_PORTAL e CHAVE_GROQ.

const ORIGENS = ["https://biel1412.github.io", "http://127.0.0.1:5199", "http://localhost:5199"];
const PORTAL = "https://api.portaldatransparencia.gov.br";
const GROQ = "https://api.groq.com";

export default {
  async fetch(req, env) {
    const origem = req.headers.get("Origin") || "";
    const permitida = ORIGENS.includes(origem);
    const cors = {
      "Access-Control-Allow-Origin": permitida ? origem : ORIGENS[0],
      "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
      "Access-Control-Allow-Headers": "chave-api-dados, Accept, Content-Type, Authorization",
      "Access-Control-Max-Age": "86400",
      "Vary": "Origin",
    };
    if (req.method === "OPTIONS") return new Response(null, { status: 204, headers: cors });
    if (!permitida) return new Response("Não permitido", { status: 403, headers: cors });

    const url = new URL(req.url);
    let r;
    if (url.pathname.startsWith("/api-de-dados/") && req.method === "GET") {
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
      return new Response("Caminho inválido", { status: 404, headers: cors });
    }
    const h = new Headers(cors);
    h.set("Content-Type", r.headers.get("Content-Type") || "application/json");
    return new Response(r.body, { status: r.status, headers: h });
  },
};
