// Intermediário do QSmart para a API do Portal da Transparência (Cloudflare Worker, plano gratuito).
// O Portal não aceita consultas feitas direto do navegador (CORS); este worker repassa a consulta
// e devolve a resposta com a permissão para o site do QSmart.
// Opcional: guarde a chave como segredo do worker (nome CHAVE_PORTAL) para não deixá-la nos navegadores.

const ORIGENS = ["https://biel1412.github.io", "http://127.0.0.1:5199", "http://localhost:5199"];
const PORTAL = "https://api.portaldatransparencia.gov.br";

export default {
  async fetch(req, env) {
    const origem = req.headers.get("Origin") || "";
    const permitida = ORIGENS.includes(origem);
    const cors = {
      "Access-Control-Allow-Origin": permitida ? origem : ORIGENS[0],
      "Access-Control-Allow-Methods": "GET, OPTIONS",
      "Access-Control-Allow-Headers": "chave-api-dados, Accept",
      "Access-Control-Max-Age": "86400",
      "Vary": "Origin",
    };
    if (req.method === "OPTIONS") return new Response(null, { status: 204, headers: cors });
    if (req.method !== "GET" || !permitida) return new Response("Não permitido", { status: 403, headers: cors });

    const url = new URL(req.url);
    if (!url.pathname.startsWith("/api-de-dados/")) return new Response("Caminho inválido", { status: 404, headers: cors });

    const chave = req.headers.get("chave-api-dados") || env.CHAVE_PORTAL || "";
    const r = await fetch(PORTAL + url.pathname + url.search, { headers: { "chave-api-dados": chave, "Accept": "application/json" } });
    const h = new Headers(cors);
    h.set("Content-Type", r.headers.get("Content-Type") || "application/json");
    return new Response(r.body, { status: r.status, headers: h });
  },
};
