using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace QSmart.Core.Relacionamento;

// ============================================================================
//  Radar Smart360: consulta pública de CNPJ e CPF.
//  Tudo aqui é cálculo e leitura de respostas; as chamadas ficam na tela.
// ============================================================================

/// <summary>CPF e CNPJ: limpar, validar, formatar e mascarar.</summary>
public static class Documento
{
    public static string Digitos(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());

    public static bool CnpjValido(string? s)
    {
        var d = Digitos(s);
        if (d.Length != 14 || d.Distinct().Count() == 1) return false;
        int Dv(int n)
        {
            int[] p = n == 12 ? new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 } : new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
            int soma = 0;
            for (int i = 0; i < n; i++) soma += (d[i] - '0') * p[i];
            var r = soma % 11;
            return r < 2 ? 0 : 11 - r;
        }
        return Dv(12) == d[12] - '0' && Dv(13) == d[13] - '0';
    }

    public static bool CpfValido(string? s)
    {
        var d = Digitos(s);
        if (d.Length != 11 || d.Distinct().Count() == 1) return false;
        int Dv(int n)
        {
            int soma = 0;
            for (int i = 0; i < n; i++) soma += (d[i] - '0') * (n + 1 - i);
            var r = soma * 10 % 11;
            return r == 10 ? 0 : r;
        }
        return Dv(9) == d[9] - '0' && Dv(10) == d[10] - '0';
    }

    public static string FmtCnpj(string? s)
    {
        var d = Digitos(s);
        return d.Length == 14 ? $"{d[..2]}.{d.Substring(2, 3)}.{d.Substring(5, 3)}/{d.Substring(8, 4)}-{d[12..]}" : s ?? "";
    }

    public static string FmtCpf(string? s)
    {
        var d = Digitos(s);
        return d.Length == 11 ? $"{d[..3]}.{d.Substring(3, 3)}.{d.Substring(6, 3)}-{d[9..]}" : s ?? "";
    }

    /// <summary>CPF mascarado para a tela: 123.***.***-09.</summary>
    public static string MascararCpf(string? s)
    {
        var d = Digitos(s);
        return d.Length == 11 ? $"{d[..3]}.***.***-{d[9..]}" : "***";
    }

    /// <summary>Formata enquanto digita (CNPJ ou CPF), sem passar de 14 dígitos.</summary>
    public static string Digitando(string? s, bool cnpj)
    {
        var d = Digitos(s);
        if (cnpj)
        {
            if (d.Length > 14) d = d[..14];
            var sb = new StringBuilder();
            for (int i = 0; i < d.Length; i++)
            {
                if (i == 2 || i == 5) sb.Append('.');
                if (i == 8) sb.Append('/');
                if (i == 12) sb.Append('-');
                sb.Append(d[i]);
            }
            return sb.ToString();
        }
        else
        {
            if (d.Length > 11) d = d[..11];
            var sb = new StringBuilder();
            for (int i = 0; i < d.Length; i++)
            {
                if (i == 3 || i == 6) sb.Append('.');
                if (i == 9) sb.Append('-');
                sb.Append(d[i]);
            }
            return sb.ToString();
        }
    }
}

// ----------------------------------------------------------------------------
//  Ficha do CNPJ
// ----------------------------------------------------------------------------

public sealed record Atividade(string Codigo, string Descricao, bool Principal)
{
    public string CodigoFmt => Cnae.Fmt(Codigo);
    public string Area => Cnae.Area(Codigo);
}

public sealed record Socio(string Nome, string Qualificacao, DateOnly? Entrada, string Documento, bool PJ, string FaixaEtaria)
{
    /// <summary>CNPJ completo quando o sócio é empresa (para abrir a ficha dela).</summary>
    public string? CnpjSocio => PJ && Relacionamento.Documento.Digitos(Documento).Length == 14 ? Relacionamento.Documento.Digitos(Documento) : null;
}

public sealed class Empresa
{
    public string Cnpj = "", RazaoSocial = "", Fantasia = "", Situacao = "", MotivoSituacao = "", SituacaoEspecial = "";
    public bool Matriz = true;
    public DateOnly? DataSituacao, Abertura, DataSituacaoEspecial;
    public string Natureza = "", CodigoNatureza = "", Porte = "";
    public decimal? Capital;
    public string Logradouro = "", Numero = "", Complemento = "", Bairro = "", Municipio = "", Uf = "", Cep = "", CodigoIbge = "";
    public string Email = "", Telefone = "";
    public bool? Simples, Mei;
    public DateOnly? SimplesDesde, SimplesAte, MeiDesde, MeiAte;
    public List<Atividade> Atividades = new();
    public List<Socio> Socios = new();
    public string Fonte = "";

    public string CnpjFmt => Documento.FmtCnpj(Cnpj);
    public bool Ativa => Texto.SemAcento(Situacao).ToUpperInvariant().Contains("ATIVA") && !Texto.SemAcento(Situacao).ToUpperInvariant().Contains("INATIV");
    public Atividade? Principal => Atividades.FirstOrDefault(a => a.Principal);
    public bool Cooperativa => CodigoNatureza.StartsWith("214") || Texto.SemAcento(Natureza).ToLowerInvariant().Contains("cooperativa");
    public bool Financeira => Atividades.Any(a => a.Codigo.StartsWith("64") || a.Codigo.StartsWith("65") || a.Codigo.StartsWith("66"));
    public bool SociedadeAnonima => CodigoNatureza.StartsWith("204") || CodigoNatureza.StartsWith("205") || Texto.SemAcento(Natureza).ToLowerInvariant().Contains("anonima");

    public string Endereco
    {
        get
        {
            var l = string.Join(", ", new[] { Logradouro, Numero }.Where(x => x != ""));
            if (Complemento != "") l += " " + Complemento;
            var partes = new[] { l, Bairro, Municipio != "" ? $"{Municipio}/{Uf}" : Uf, Cep != "" ? Radar.FmtCep(Cep) : "" }.Where(x => x.Trim() != "");
            return string.Join(" · ", partes);
        }
    }

    public string Idade(DateOnly hoje)
    {
        if (Abertura is not { } a) return "";
        int anos = hoje.Year - a.Year - (hoje < a.AddYears(hoje.Year - a.Year) ? 1 : 0);
        if (anos >= 1) return anos == 1 ? "1 ano" : $"{anos} anos";
        int meses = (hoje.Year - a.Year) * 12 + hoje.Month - a.Month - (hoje.Day < a.Day ? 1 : 0);
        return meses <= 0 ? "menos de 1 mês" : meses == 1 ? "1 mês" : $"{meses} meses";
    }
}

/// <summary>CNAE: formato e área em linguagem simples.</summary>
public static class Cnae
{
    public static string Fmt(string c)
    {
        var d = Documento.Digitos(c).PadLeft(7, '0');
        return d.Length == 7 ? $"{d[..4]}-{d[4]}/{d[5..]}" : c;
    }

    static readonly (int de, int ate, string area)[] Areas =
    {
        (1, 3, "Agro, pecuária, pesca e floresta"), (5, 9, "Mineração, petróleo e gás"), (10, 33, "Indústria"),
        (35, 35, "Energia elétrica e gás"), (36, 39, "Água, esgoto e resíduos"), (41, 43, "Construção"),
        (45, 45, "Comércio e conserto de veículos"), (46, 46, "Comércio atacadista"), (47, 47, "Comércio varejista"),
        (49, 53, "Transporte, armazenagem e correio"), (55, 56, "Hospedagem e alimentação"), (58, 63, "Informação, comunicação e tecnologia"),
        (64, 66, "Serviços financeiros, seguros e previdência"), (68, 68, "Imóveis"), (69, 75, "Serviços profissionais, científicos e técnicos"),
        (77, 82, "Serviços administrativos e de apoio"), (84, 84, "Administração pública"), (85, 85, "Educação"),
        (86, 88, "Saúde e assistência social"), (90, 93, "Arte, cultura, esporte e lazer"), (94, 96, "Outros serviços (associações, reparos, serviços pessoais)"),
        (97, 97, "Serviços domésticos"), (99, 99, "Organismos internacionais"),
    };

    public static string Area(string c)
    {
        var d = Documento.Digitos(c).PadLeft(7, '0');
        if (!int.TryParse(d[..2], out var div)) return "";
        foreach (var (de, ate, area) in Areas) if (div >= de && div <= ate) return area;
        return "";
    }

    /// <summary>"COOPERATIVAS DE CRÉDITO MÚTUO" → "Cooperativas de crédito mútuo".</summary>
    public static string Frase(string s)
    {
        s = s.Trim();
        if (s == "") return s;
        if (s.Any(char.IsLower)) return char.ToUpperInvariant(s[0]) + s[1..];
        var l = s.ToLowerInvariant();
        return char.ToUpperInvariant(l[0]) + l[1..];
    }
}

/// <summary>Leitura das respostas das fontes de CNPJ (BrasilAPI, Minha Receita e CNPJá).</summary>
public static class LeitorCnpj
{
    public static string UrlBrasilApi(string cnpj) => "https://brasilapi.com.br/api/cnpj/v1/" + Documento.Digitos(cnpj);
    public static string UrlMinhaReceita(string cnpj) => "https://minhareceita.org/" + Documento.Digitos(cnpj);
    public static string UrlCnpja(string cnpj) => "https://open.cnpja.com/office/" + Documento.Digitos(cnpj);

    internal static string S(JsonNode? n, params string[] nomes)
    {
        if (n is not JsonObject o) return "";
        foreach (var nome in nomes)
        {
            if (!o.TryGetPropertyValue(nome, out var v) || v == null) continue;
            if (v is JsonValue jv)
            {
                if (jv.TryGetValue<string>(out var s)) { if (s.Trim() != "") return s.Trim(); continue; }
                return jv.ToJsonString().Trim('"');
            }
        }
        return "";
    }

    internal static bool? B(JsonNode? n, string nome)
    {
        if (n is not JsonObject o || !o.TryGetPropertyValue(nome, out var v) || v == null) return null;
        if (v is JsonValue jv)
        {
            if (jv.TryGetValue<bool>(out var b)) return b;
            if (jv.TryGetValue<string>(out var s)) return s.Trim().ToUpperInvariant() is "S" or "SIM" or "TRUE" ? true : s.Trim() == "" ? null : false;
        }
        return null;
    }

    internal static DateOnly? D(string s)
    {
        s = s.Trim();
        if (s == "" || s.StartsWith("0000")) return null;
        if (s.Length >= 10 && DateOnly.TryParseExact(s[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        if (s.Length >= 10 && DateOnly.TryParseExact(s[..10], "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d;
        if (s.Length == 8 && DateOnly.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d;
        return null;
    }

    internal static decimal? Dec(string s)
    {
        s = s.Trim();
        if (s == "") return null;
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>BrasilAPI e Minha Receita usam o mesmo formato (dados abertos da Receita).</summary>
    public static Empresa? BrasilApi(string json, string fonte = "Receita Federal (via BrasilAPI)")
    {
        JsonNode? n;
        try { n = JsonNode.Parse(json); } catch { return null; }
        if (n is not JsonObject || S(n, "cnpj") == "") return null;
        var e = new Empresa
        {
            Cnpj = Documento.Digitos(S(n, "cnpj")),
            RazaoSocial = S(n, "razao_social"),
            Fantasia = S(n, "nome_fantasia"),
            Matriz = S(n, "identificador_matriz_filial") != "2",
            Situacao = S(n, "descricao_situacao_cadastral"),
            DataSituacao = D(S(n, "data_situacao_cadastral")),
            MotivoSituacao = S(n, "descricao_motivo_situacao_cadastral"),
            SituacaoEspecial = S(n, "situacao_especial"),
            DataSituacaoEspecial = D(S(n, "data_situacao_especial")),
            Abertura = D(S(n, "data_inicio_atividade")),
            Natureza = S(n, "natureza_juridica"),
            CodigoNatureza = Documento.Digitos(S(n, "codigo_natureza_juridica")),
            Porte = S(n, "porte", "descricao_porte"),
            Capital = Dec(S(n, "capital_social")),
            Logradouro = string.Join(" ", new[] { S(n, "descricao_tipo_de_logradouro"), S(n, "logradouro") }.Where(x => x != "")),
            Numero = S(n, "numero"),
            Complemento = S(n, "complemento"),
            Bairro = S(n, "bairro"),
            Municipio = S(n, "municipio"),
            Uf = S(n, "uf"),
            Cep = Documento.Digitos(S(n, "cep")),
            CodigoIbge = S(n, "codigo_municipio_ibge"),
            Email = S(n, "email"),
            Telefone = S(n, "ddd_telefone_1"),
            Simples = B(n, "opcao_pelo_simples"),
            SimplesDesde = D(S(n, "data_opcao_pelo_simples")),
            SimplesAte = D(S(n, "data_exclusao_do_simples")),
            Mei = B(n, "opcao_pelo_mei"),
            MeiDesde = D(S(n, "data_opcao_pelo_mei")),
            MeiAte = D(S(n, "data_exclusao_do_mei")),
            Fonte = fonte,
        };
        if (e.Situacao == "") e.Situacao = S(n, "situacao_cadastral");
        if (e.Logradouro.StartsWith(S(n, "descricao_tipo_de_logradouro") + " " + S(n, "descricao_tipo_de_logradouro"))) e.Logradouro = S(n, "logradouro");
        var cod = S(n, "cnae_fiscal");
        if (cod != "" && cod != "0") e.Atividades.Add(new Atividade(cod, Cnae.Frase(S(n, "cnae_fiscal_descricao")), true));
        if (n["cnaes_secundarios"] is JsonArray sec)
            foreach (var c in sec)
            {
                var cc = S(c, "codigo");
                if (cc != "" && cc != "0") e.Atividades.Add(new Atividade(cc, Cnae.Frase(S(c, "descricao")), false));
            }
        if (n["qsa"] is JsonArray qsa)
            foreach (var s in qsa)
            {
                var tipo = S(s, "identificador_de_socio");
                var doc = S(s, "cnpj_cpf_do_socio");
                e.Socios.Add(new Socio(S(s, "nome_socio"), S(s, "qualificacao_socio"), D(S(s, "data_entrada_sociedade")), doc,
                    tipo == "1" || Documento.Digitos(doc).Length == 14, S(s, "faixa_etaria")));
            }
        return e;
    }

    /// <summary>CNPJá aberto (open.cnpja.com): formato próprio.</summary>
    public static Empresa? Cnpja(string json)
    {
        JsonNode? n;
        try { n = JsonNode.Parse(json); } catch { return null; }
        if (n is not JsonObject || S(n, "taxId") == "") return null;
        var co = n["company"];
        var ad = n["address"];
        var e = new Empresa
        {
            Cnpj = Documento.Digitos(S(n, "taxId")),
            RazaoSocial = S(co, "name"),
            Fantasia = S(n, "alias"),
            Matriz = B(n, "head") ?? true,
            Situacao = S(n["status"], "text"),
            DataSituacao = D(S(n, "statusDate")),
            MotivoSituacao = S(n["reason"], "text"),
            SituacaoEspecial = S(n["special"], "text"),
            DataSituacaoEspecial = D(S(n, "specialDate")),
            Abertura = D(S(n, "founded")),
            Natureza = S(co?["nature"], "text"),
            CodigoNatureza = Documento.Digitos(S(co?["nature"], "id")),
            Porte = S(co?["size"], "text"),
            Capital = Dec(S(co, "equity")),
            Logradouro = S(ad, "street"),
            Numero = S(ad, "number"),
            Complemento = S(ad, "details"),
            Bairro = S(ad, "district"),
            Municipio = S(ad, "city"),
            Uf = S(ad, "state"),
            Cep = Documento.Digitos(S(ad, "zip")),
            CodigoIbge = S(ad, "municipality"),
            Simples = B(co?["simples"], "optant"),
            SimplesDesde = D(S(co?["simples"], "since")),
            Mei = B(co?["simei"], "optant"),
            MeiDesde = D(S(co?["simei"], "since")),
            Fonte = "Receita Federal (via CNPJá)",
        };
        if (n["emails"] is JsonArray em && em.Count > 0) e.Email = S(em[0], "address");
        if (n["phones"] is JsonArray ph && ph.Count > 0) e.Telefone = S(ph[0], "area") + S(ph[0], "number");
        if (n["mainActivity"] is JsonObject ma) e.Atividades.Add(new Atividade(S(ma, "id"), Cnae.Frase(S(ma, "text")), true));
        if (n["sideActivities"] is JsonArray sa) foreach (var a in sa) e.Atividades.Add(new Atividade(S(a, "id"), Cnae.Frase(S(a, "text")), false));
        if (co?["members"] is JsonArray mem)
            foreach (var m in mem)
            {
                var p = m?["person"];
                var tipo = S(p, "type");
                e.Socios.Add(new Socio(S(p, "name"), S(m?["role"], "text"), D(S(m, "since")), S(p, "taxId"),
                    tipo.Equals("LEGAL", StringComparison.OrdinalIgnoreCase) || Documento.Digitos(S(p, "taxId")).Length == 14, S(p, "age")));
            }
        return e;
    }
}

// ----------------------------------------------------------------------------
//  Checagens, placar e linha do tempo
// ----------------------------------------------------------------------------

public enum StatusCheck { Carregando, NadaConsta, Consta, Atencao, Info, Atalho, SemChave, Erro }

public sealed class Checagem
{
    public string Id = "", Titulo = "", Fonte = "", Detalhe = "", Url = "", Dimensao = "";
    public StatusCheck Status = StatusCheck.Carregando;
    public List<(string texto, string? url)> Itens = new();

    public string Rotulo => Status switch
    {
        StatusCheck.Carregando => "consultando…",
        StatusCheck.NadaConsta => "nada consta",
        StatusCheck.Consta => "consta",
        StatusCheck.Atencao => "atenção",
        StatusCheck.Info => "informativo",
        StatusCheck.Atalho => "abrir",
        StatusCheck.SemChave => "sem chave",
        _ => "não respondeu",
    };
}

public sealed record Placa(string Dimensao, string Cor, string Texto);

public sealed record Evento(DateOnly Data, string Titulo, string Detalhe, string Fonte, string Cor);

public static class Radar
{
    public static string FmtCep(string c) { var d = Documento.Digitos(c); return d.Length == 8 ? $"{d[..5]}-{d[5..]}" : c; }

    public static string Dmy(DateOnly? d) => d is { } x ? x.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "—";

    public static string Dinheiro(decimal? v)
    {
        if (v is not { } x) return "—";
        var s = x.ToString("#,##0.00", CultureInfo.InvariantCulture);
        return "R$ " + s.Replace(",", "#").Replace(".", ",").Replace("#", ".");
    }

    public static string DinheiroCurto(decimal v)
    {
        if (v >= 1_000_000_000) return "R$ " + (v / 1_000_000_000m).ToString("0.#", CultureInfo.InvariantCulture).Replace('.', ',') + " bi";
        if (v >= 1_000_000) return "R$ " + (v / 1_000_000m).ToString("0.#", CultureInfo.InvariantCulture).Replace('.', ',') + " mi";
        if (v >= 1_000) return "R$ " + (v / 1_000m).ToString("0.#", CultureInfo.InvariantCulture).Replace('.', ',') + " mil";
        return Dinheiro(v);
    }

    /// <summary>Tira o prefixo "02-" etc. e deixa "Sócio-Administrador" legível.</summary>
    public static string Qualificacao(string q) => Regex.Replace(q.Trim(), @"^\d+\s*-\s*", "");

    /// <summary>Iniciais para o avatar.</summary>
    public static string Iniciais(string nome)
    {
        var p = nome.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(x => x.Length > 2).ToList();
        if (p.Count == 0) return nome.Length > 0 ? nome[..1].ToUpperInvariant() : "?";
        return (p[0][..1] + (p.Count > 1 ? p[^1][..1] : "")).ToUpperInvariant();
    }

    public static string NomeProprio(string s) => Texto.TitleCase(s.ToLowerInvariant());

    // ------------------------- placar -------------------------

    public static readonly string[] DimensoesPJ = { "Cadastro", "Sanções e compliance", "Ambiental", "Trabalhista", "Financeiro (Banco Central)", "Governo" };
    public static readonly string[] DimensoesPF = { "Sanções federais", "TCU e CNJ", "PEP", "Banco Central", "Ambiental e trabalhista", "Benefícios sociais" };

    /// <summary>Semáforo de uma dimensão a partir das checagens dela.</summary>
    public static Placa Placar(string dim, IEnumerable<Checagem> todas)
    {
        var l = todas.Where(c => c.Dimensao == dim).ToList();
        if (l.Count == 0) return new(dim, "cz", "sem dado");
        if (l.Any(c => c.Status == StatusCheck.Carregando)) return new(dim, "cz", "consultando…");
        int consta = l.Count(c => c.Status == StatusCheck.Consta), atencao = l.Count(c => c.Status == StatusCheck.Atencao);
        if (consta > 0) return new(dim, "rr", consta == 1 ? "1 registro" : $"{consta} registros");
        if (atencao > 0) return new(dim, "y", l.First(c => c.Status == StatusCheck.Atencao).Detalhe is { Length: > 0 and < 28 } d ? d : "atenção");
        if (l.Any(c => c.Status == StatusCheck.NadaConsta)) return new(dim, "g", "nada consta");
        var info = l.FirstOrDefault(c => c.Status == StatusCheck.Info);
        if (info != null) return new(dim, "b", info.Detalhe is { Length: > 0 and < 28 } d2 ? d2 : "informativo");
        if (l.All(c => c.Status == StatusCheck.SemChave)) return new(dim, "cz", "sem chave");
        if (l.Any(c => c.Status == StatusCheck.Erro)) return new(dim, "cz", "não respondeu");
        return new(dim, "cz", "conferir no site");
    }

    // ------------------------- linha do tempo -------------------------

    public static List<Evento> Linha(Empresa e, IEnumerable<Retrato> retratos, IEnumerable<Evento> extras)
    {
        var l = new List<Evento>();
        if (e.Abertura is { } ab) l.Add(new(ab, "Abertura da empresa", e.Matriz ? "Início das atividades (matriz)" : "Início das atividades (filial)", "Receita", "g"));
        if (e.DataSituacao is { } ds && ds != e.Abertura)
            l.Add(new(ds, "Situação cadastral: " + Cnae.Frase(e.Situacao), e.MotivoSituacao is { Length: > 0 } m && !m.Equals("SEM MOTIVO", StringComparison.OrdinalIgnoreCase) ? Cnae.Frase(m) : "Data da situação atual", "Receita", e.Ativa ? "b" : "r"));
        if (e.DataSituacaoEspecial is { } dse) l.Add(new(dse, "Situação especial", Cnae.Frase(e.SituacaoEspecial), "Receita", "y"));
        foreach (var s in e.Socios)
            if (s.Entrada is { } en) l.Add(new(en, "Entrada de " + NomeProprio(s.Nome), Qualificacao(s.Qualificacao), "Receita", "p"));
        if (e.SimplesDesde is { } sd) l.Add(new(sd, "Opção pelo Simples Nacional", "", "Receita", "c"));
        if (e.SimplesAte is { } sa) l.Add(new(sa, "Saída do Simples Nacional", "", "Receita", "y"));
        if (e.MeiDesde is { } md) l.Add(new(md, "Enquadramento como MEI", "", "Receita", "c"));
        if (e.MeiAte is { } ma) l.Add(new(ma, "Saída do MEI", "", "Receita", "y"));
        var rs = retratos.OrderBy(r => r.Quando).ToList();
        for (int i = 1; i < rs.Count; i++)
        {
            var dif = Retrato.Diferencas(rs[i - 1], rs[i]);
            if (dif.Count > 0) l.Add(new(DateOnly.FromDateTime(rs[i].Quando), "Mudou desde a consulta anterior", string.Join(" · ", dif), "radar", "k"));
        }
        l.AddRange(extras);
        return l.OrderByDescending(x => x.Data).ToList();
    }

    // ------------------------- Juntas Comerciais -------------------------

    public sealed record Junta(string Uf, string Estado, string Sigla, string Site, string Drei);

    static string Slug(string estado, string uf)
    {
        var s = Texto.SemAcento(estado).ToLowerInvariant().Replace(' ', '-');
        return $"{s}-{uf.ToLowerInvariant()}";
    }

    const string DreiBase = "https://www.gov.br/empresas-e-negocios/pt-br/drei/juntas-comerciais/";

    static Junta J(string uf, string estado, string sigla, string site, string? drei = null) => new(uf, estado, sigla, site, DreiBase + (drei ?? Slug(estado, uf)));

    public static readonly Junta[] Juntas =
    {
        J("AC", "Acre", "JUCEAC", "https://www.juceac.ac.gov.br"),
        J("AL", "Alagoas", "JUCEAL", "https://www.juceal.al.gov.br", "alagoas-al-alagoas-al-1"),
        J("AP", "Amapá", "JUCAP", "https://www.jucap.ap.gov.br"),
        J("AM", "Amazonas", "JUCEA", "https://www.jucea.am.gov.br"),
        J("BA", "Bahia", "JUCEB", "https://www.juceb.ba.gov.br"),
        J("CE", "Ceará", "JUCEC", "https://www.jucec.ce.gov.br"),
        J("DF", "Distrito Federal", "JCDF", "http://jucis.df.gov.br"),
        J("ES", "Espírito Santo", "JUCEES", "https://jucees.es.gov.br"),
        J("GO", "Goiás", "JUCEG", "https://www.juceg.go.gov.br"),
        J("MA", "Maranhão", "JUCEMA", "https://www.jucema.ma.gov.br"),
        J("MT", "Mato Grosso", "JUCEMAT", "https://www.jucemat.mt.gov.br"),
        J("MS", "Mato Grosso do Sul", "JUCEMS", "https://www.jucems.ms.gov.br"),
        J("MG", "Minas Gerais", "JUCEMG", "https://www.jucemg.mg.gov.br"),
        J("PA", "Pará", "JUCEPA", "https://www.jucepa.pa.gov.br"),
        J("PB", "Paraíba", "JUCEP", "https://jucep.pb.gov.br"),
        J("PR", "Paraná", "JUCEPAR", "https://www.juntacomercial.pr.gov.br"),
        J("PE", "Pernambuco", "JUCEPE", "https://portal.jucepe.pe.gov.br"),
        J("PI", "Piauí", "JUCEPI", "https://www.jucepi.pi.gov.br"),
        J("RJ", "Rio de Janeiro", "JUCERJA", "https://www.jucerja.rj.gov.br"),
        J("RN", "Rio Grande do Norte", "JUCERN", "https://www.jucern.rn.gov.br"),
        J("RS", "Rio Grande do Sul", "JUCISRS", "https://jucisrs.rs.gov.br"),
        J("RO", "Rondônia", "JUCER", "https://www.jucer.ro.gov.br"),
        J("RR", "Roraima", "JUCERR", "https://www.jucerr.rr.gov.br"),
        J("SC", "Santa Catarina", "JUCESC", "https://www.jucesc.sc.gov.br"),
        J("SP", "São Paulo", "JUCESP", "https://www.jucesponline.sp.gov.br"),
        J("SE", "Sergipe", "JUCESE", "https://www.jucese.se.gov.br"),
        J("TO", "Tocantins", "JUCETINS", "https://www.jucetins.to.gov.br"),
    };

    public static Junta? JuntaDe(string uf) => Juntas.FirstOrDefault(j => j.Uf.Equals(uf, StringComparison.OrdinalIgnoreCase));

    // ------------------------- atalhos oficiais (pedem captcha ou login) -------------------------

    public static readonly (string nome, string url)[] AtalhosPJ =
    {
        ("Certidão Receita/PGFN", "https://servicos.receitafederal.gov.br/servico/certidoes/#/home/cnpj"),
        ("CNDT · TST", "https://cndt-certidao.tst.jus.br/inicio.faces"),
        ("CRF · FGTS", "https://consulta-crf.caixa.gov.br/consultacrf/pages/consultaEmpregador.jsf"),
        ("Protestos", "https://www.pesquisaprotesto.com.br/"),
        ("Cartão CNPJ", "https://solucoes.receita.fazenda.gov.br/Servicos/cnpjreva/Cnpjreva_Solicitacao.asp"),
    };

    public static readonly (string nome, string url)[] AtalhosPF =
    {
        ("Situação do CPF (Receita)", "https://servicos.receita.fazenda.gov.br/servicos/cpf/consultasituacao/consultapublica.asp"),
        ("Certidão PGFN", "https://servicos.receitafederal.gov.br/servico/certidoes/#/home/cpf"),
        ("Protestos", "https://www.pesquisaprotesto.com.br/"),
    };

    public const string UrlIbama = "https://servicos.ibama.gov.br/ctf/publico/areasembargadas/ConsultaPublicaAreasEmbargadas.php";
    public const string UrlListaSuja = "https://www.gov.br/trabalho-e-emprego/pt-br/assuntos/inspecao-do-trabalho/areas-de-atuacao/cadastro_de_empregadores.pdf";
    public const string UrlTcuConsolidada = "https://certidoes-apf.apps.tcu.gov.br/";
    public const string UrlTcuCertidoes = "https://portal.tcu.gov.br/carta-de-servicos/certidoes";
    public const string UrlCnjImprobidade = "https://www.cnj.jus.br/improbidade_adm/consultar_requerido.php";
    public const string UrlBcInstituicao = "https://www.bcb.gov.br/estabilidadefinanceira/encontreinstituicao";
    public const string UrlBcProcessos = "https://www.bcb.gov.br/estabilidadefinanceira/processossfn";
    public const string UrlCvm = "https://www.rad.cvm.gov.br/ENET/frmConsultaExternaCVM.aspx";
    public const string UrlPortal = "https://portaldatransparencia.gov.br";
    public const string UrlPortalCadastro = "https://portaldatransparencia.gov.br/api-de-dados/cadastrar-email";

    // ------------------------- TCU · consulta consolidada -------------------------

    public static string UrlTcu(string cnpj) => $"https://certidoes-apf.apps.tcu.gov.br/api/rest/publico/certidoes/{Documento.Digitos(cnpj)}?seEmitirPDF=false";

    /// <summary>Lê a consulta consolidada (TCU, CNJ, CEIS, CNEP) e devolve uma checagem por cadastro.</summary>
    public static List<Checagem> LerTcu(string json)
    {
        var l = new List<Checagem>();
        JsonNode? n;
        try { n = JsonNode.Parse(json); } catch { return l; }
        if (n?["certidoes"] is not JsonArray arr) return l;
        foreach (var c in arr)
        {
            var emissor = LeitorCnpj.S(c, "emissor");
            var desc = LeitorCnpj.S(c, "descricao", "tipo");
            var sit = Texto.SemAcento(LeitorCnpj.S(c, "situacao", "resultadoConsulta", "resultado")).ToUpperInvariant();
            var obs = LeitorCnpj.S(c, "observacao");
            var nada = sit.Contains("NADA") || sit.Contains("NEGATIV") || sit.Contains("NAO CONSTA") || sit.Contains("NAO_CONSTA");
            var titulo = TituloTcu(emissor, desc);
            l.Add(new Checagem
            {
                Id = "tcu-" + l.Count, Titulo = titulo, Fonte = "Consulta consolidada TCU",
                Status = sit == "" ? StatusCheck.Info : nada ? StatusCheck.NadaConsta : StatusCheck.Consta,
                Detalhe = nada ? "" : obs, Url = UrlTcuConsolidada, Dimensao = "Sanções e compliance",
            });
        }
        return l;
    }

    static string TituloTcu(string emissor, string desc)
    {
        var d = Texto.SemAcento(desc + " " + emissor).ToUpperInvariant();
        if (d.Contains("INIDONE")) return "TCU · licitantes inidôneos";
        if (d.Contains("IMPROBIDADE") || d.Contains("CNJ")) return "CNJ · improbidade administrativa";
        if (d.Contains("CEIS")) return "CGU · CEIS (inidôneas e suspensas)";
        if (d.Contains("CNEP")) return "CGU · CNEP (empresas punidas)";
        return (emissor != "" ? emissor + " · " : "") + Cnae.Frase(desc);
    }

    // ------------------------- Portal da Transparência -------------------------

    public const string PortalBase = "https://api.portaldatransparencia.gov.br/api-de-dados/";
    public static string UrlPortalPJ(string cnpj) => PortalBase + "pessoa-juridica?cnpj=" + Documento.Digitos(cnpj);
    public static string UrlPortalPF(string cpf) => PortalBase + "pessoa-fisica?cpf=" + Documento.Digitos(cpf);
    public static string UrlPeps(string cpf) => PortalBase + "peps?cpf=" + Documento.Digitos(cpf) + "&pagina=1";
    public static string UrlLeniencia(string cnpj) => PortalBase + "acordos-leniencia?cnpjSancionado=" + Documento.Digitos(cnpj) + "&pagina=1";
    public static string UrlContratos(string doc) => PortalBase + "contratos/cpf-cnpj?cpfCnpj=" + Documento.Digitos(doc) + "&pagina=1";
    public static string UrlServidor(string cpf) => PortalBase + "servidores?cpf=" + Documento.Digitos(cpf) + "&pagina=1";
    /// <summary>Consulta leve para testar a chave.</summary>
    public static string UrlPortalTeste => PortalBase + "pessoa-juridica?cnpj=00000000000191";

    static readonly Dictionary<string, string> RotulosBeneficio = new(StringComparer.OrdinalIgnoreCase)
    {
        ["favorecidoBolsaFamilia"] = "Bolsa Família (até 2021)", ["favorecidoNovoBolsaFamilia"] = "Bolsa Família",
        ["favorecidoAuxilioBrasil"] = "Auxílio Brasil", ["favorecidoBpc"] = "BPC", ["favorecidoAuxilioEmergencial"] = "Auxílio Emergencial",
        ["favorecidoSeguroDefeso"] = "Seguro Defeso", ["favorecidoSafra"] = "Garantia-Safra", ["favorecidoPeti"] = "PETI",
        ["favorecidoAuxilioReconstrucao"] = "Auxílio Reconstrução", ["favorecidoGasDoPovo"] = "Gás do Povo", ["favorecidoAuxilioGas"] = "Auxílio Gás",
    };

    static readonly HashSet<string> NaoBeneficio = new(StringComparer.OrdinalIgnoreCase) { "favorecidoDespesas", "favorecidoTransferencias", "favorecidoCartaoPagamento" };

    public sealed class PessoaPortal
    {
        public string Nome = "", Nis = "";
        public bool Servidor, ServidorInativo, Pensionista, Ceis, Cnep, Ceaf, Cepim, Contratado, Despesas, Leniencia;
        /// <summary>Programa → recebe?</summary>
        public List<(string programa, bool recebe)> Beneficios = new();
        public List<string> OutrasMarcas = new();
        public bool RecebeBeneficio => Beneficios.Any(b => b.recebe);
    }

    /// <summary>Lê pessoa-fisica ou pessoa-juridica (marcadores verdadeiro/falso).</summary>
    public static PessoaPortal? LerPortal(string json)
    {
        JsonNode? n;
        try { n = JsonNode.Parse(json); } catch { return null; }
        if (n is JsonArray a) n = a.Count > 0 ? a[0] : null;
        if (n is not JsonObject o) return null;
        var p = new PessoaPortal { Nome = LeitorCnpj.S(o, "nome", "razaoSocial"), Nis = LeitorCnpj.S(o, "nis") };
        foreach (var (k, v) in o)
        {
            if (v is not JsonValue jv || !jv.TryGetValue<bool>(out var b)) continue;
            switch (k)
            {
                case "servidor": p.Servidor = b; break;
                case "servidorInativo": p.ServidorInativo = b; break;
                case "pensionistaOuRepresentante": p.Pensionista = b; break;
                case "sancionadoCEIS": p.Ceis = b; break;
                case "sancionadoCNEP": p.Cnep = b; break;
                case "sancionadoCEAF": p.Ceaf = b; break;
                case "sancionadoCEPIM": p.Cepim = b; break;
                case "contratado": case "possuiContratacao": p.Contratado |= b; break;
                case "favorecidoDespesas": p.Despesas = b; break;
                case "sancionadoLeniencia": case "acordoLeniencia": p.Leniencia = b; break;
                default:
                    if (k.StartsWith("favorecido", StringComparison.OrdinalIgnoreCase) && !NaoBeneficio.Contains(k))
                        p.Beneficios.Add((RotulosBeneficio.GetValueOrDefault(k) ?? Humanizar(k["favorecido".Length..]), b));
                    else if (b) p.OutrasMarcas.Add(Humanizar(k));
                    break;
            }
        }
        return p;
    }

    static string Humanizar(string k)
    {
        var s = Regex.Replace(k, "(?<=[a-z])(?=[A-Z])", " ");
        return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant() : s;
    }

    public sealed record Pep(string Funcao, string Orgao, string Inicio, string Fim, string FimCarencia);

    public static List<Pep> LerPeps(string json)
    {
        var l = new List<Pep>();
        try
        {
            if (JsonNode.Parse(json) is not JsonArray a) return l;
            foreach (var x in a)
                l.Add(new Pep(LeitorCnpj.S(x, "descricao_funcao", "descricaoFuncao", "funcao"), LeitorCnpj.S(x, "nome_orgao", "nomeOrgao", "orgao"),
                    LeitorCnpj.S(x, "dt_inicio_exercicio", "dataInicioExercicio"), LeitorCnpj.S(x, "dt_fim_exercicio", "dataFimExercicio"),
                    LeitorCnpj.S(x, "dt_fim_carencia", "dataFimCarencia")));
        }
        catch { }
        return l;
    }

    public sealed record Vinculo(string Cargo, string Orgao, string Situacao);

    public static List<Vinculo> LerServidor(string json)
    {
        var l = new List<Vinculo>();
        try
        {
            if (JsonNode.Parse(json) is not JsonArray a) return l;
            foreach (var x in a)
            {
                var f = x?["fichasCargoEfetivo"] is JsonArray fe && fe.Count > 0 ? fe[0] : x?["fichasFuncao"] is JsonArray ff && ff.Count > 0 ? ff[0] : x;
                var srv = x?["servidor"] ?? x;
                l.Add(new Vinculo(LeitorCnpj.S(f, "cargo", "funcao", "descricaoCargo"),
                    LeitorCnpj.S(f, "orgaoServidorLotacao", "orgaoLotacao", "uorgLotacao", "orgaoServidorExercicio"),
                    LeitorCnpj.S(srv, "situacao", "situacaoVinculo", "tipoServidor")));
            }
        }
        catch { }
        return l.Where(v => v.Cargo != "" || v.Orgao != "").ToList();
    }

    /// <summary>Contratos: quantidade e soma (o que vier na primeira página).</summary>
    public static (int qtd, decimal total, string ultimo) LerContratos(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonArray a) return (0, 0, "");
            decimal total = 0; DateOnly? ult = null;
            foreach (var x in a)
            {
                total += LeitorCnpj.Dec(LeitorCnpj.S(x, "valorFinalCompra", "valorInicialCompra", "valor")) ?? 0;
                var d = LeitorCnpj.D(LeitorCnpj.S(x, "dataAssinatura", "dataInicioVigencia"));
                if (d != null && (ult == null || d > ult)) ult = d;
            }
            return (a.Count, total, ult is { } u ? u.Year.ToString(CultureInfo.InvariantCulture) : "");
        }
        catch { return (0, 0, ""); }
    }

    public static int ContarLista(string json)
    {
        try { return JsonNode.Parse(json) is JsonArray a ? a.Count : 0; } catch { return 0; }
    }

    /// <summary>Mensagem amigável para erros do Portal.</summary>
    public static string ErroPortal(int status) => status switch
    {
        0 => "não respondeu ao navegador",
        401 or 403 => "chave do Portal inválida",
        429 => "limite de consultas do Portal atingido; tente em 1 minuto",
        _ => $"erro {status}",
    };

    // ------------------------- Banco Central -------------------------

    /// <summary>IF.data (cadastro trimestral). Tenta trimestres recentes.</summary>
    public static IEnumerable<string> UrlsIfData(string cnpj, DateOnly hoje)
    {
        var raiz = Documento.Digitos(cnpj)[..8];
        var d = new DateOnly(hoje.Year, (hoje.Month - 1) / 3 * 3 + 1, 1).AddMonths(-3);
        for (int i = 0; i < 4; i++, d = d.AddMonths(-3))
        {
            var am = d.AddMonths(2).ToString("yyyyMM", CultureInfo.InvariantCulture);
            yield return $"https://olinda.bcb.gov.br/olinda/servico/IFDATA/versao/v1/odata/IfDataCadastro(AnoMes=@AnoMes)?@AnoMes={am}&$format=json&$filter=CodInst%20eq%20'{raiz}'";
        }
    }

    public static string? LerIfData(string json)
    {
        try
        {
            if (JsonNode.Parse(json)?["value"] is not JsonArray a || a.Count == 0) return null;
            var x = a[0];
            var partes = new[]
            {
                LeitorCnpj.S(x, "Atividade"), LeitorCnpj.S(x, "SegmentoTb", "Segmento") is { Length: > 0 } seg ? "segmento " + seg : "",
                LeitorCnpj.S(x, "Situacao") is { Length: > 2 } s ? s.ToLowerInvariant() : "",
            }.Where(p => p != "");
            return string.Join(" · ", partes);
        }
        catch { return null; }
    }

    public const string UrlQgi = "https://olinda.bcb.gov.br/olinda/servico/Gepad_QuadrosGeraisInternet/versao/v1/odata/QuadroGeralInabilitados?$format=json&$top=20000";

    /// <summary>Procura no Quadro Geral de Inabilitados por nome (e CPF, se a base trouxer).</summary>
    public static (bool achou, string detalhe) BuscarQgi(string json, string nome, string cpf)
    {
        try
        {
            if (JsonNode.Parse(json)?["value"] is not JsonArray a) return (false, "");
            var alvo = Norm(nome);
            var dig = Documento.Digitos(cpf);
            foreach (var x in a)
            {
                if (x is not JsonObject o) continue;
                string n = "", c = "", prazo = "";
                foreach (var (k, v) in o)
                {
                    var kl = k.ToLowerInvariant();
                    var val = v?.ToString() ?? "";
                    if (kl.Contains("nome")) n = val;
                    else if (kl.Contains("cpf")) c = val;
                    else if (kl.Contains("prazo") || kl.Contains("fim") || kl.Contains("termino")) prazo = val;
                }
                var cd = Documento.Digitos(c);
                bool porCpf = cd.Length == 11 && cd == dig;
                bool porNome = alvo != "" && Norm(n) == alvo && (cd.Length != 11 || cd == dig);
                if (porCpf || porNome) return (true, prazo != "" ? "inabilitação até " + prazo : "consta no quadro de inabilitados");
            }
            return (false, "");
        }
        catch { return (false, ""); }
    }

    public static string Norm(string s) => Regex.Replace(Texto.SemAcento(s).ToUpperInvariant(), @"[^A-Z ]", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries) is var p ? string.Join(" ", p) : "";

    // ------------------------- Querido Diário, Registro.br, mapa -------------------------

    public static string UrlDiarios(string cnpj) => "https://api.queridodiario.ok.org.br/gazettes?querystring=" + Uri.EscapeDataString("\"" + Documento.FmtCnpj(cnpj) + "\"") + "&size=5&sort_by=descending_date";

    public static (int total, List<(string texto, string? url)> itens) LerDiarios(string json)
    {
        var l = new List<(string, string?)>();
        try
        {
            var n = JsonNode.Parse(json);
            var total = int.TryParse(LeitorCnpj.S(n, "total_gazettes"), out var t) ? t : 0;
            if (n?["gazettes"] is JsonArray a)
                foreach (var g in a.Take(5))
                {
                    var d = LeitorCnpj.D(LeitorCnpj.S(g, "date"));
                    l.Add(($"{Dmy(d)} · {LeitorCnpj.S(g, "territory_name")}/{LeitorCnpj.S(g, "state_code")}", LeitorCnpj.S(g, "url") is { Length: > 0 } u ? u : null));
                }
            return (total, l);
        }
        catch { return (0, l); }
    }

    static readonly HashSet<string> Provedores = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "hotmail.com", "outlook.com", "yahoo.com", "yahoo.com.br", "bol.com.br", "uol.com.br", "terra.com.br", "live.com", "icloud.com",
        "ig.com.br", "globo.com", "msn.com", "hotmail.com.br", "outlook.com.br", "r7.com", "zipmail.com.br", "contabil.com.br",
    };

    /// <summary>Domínio próprio da empresa (do e-mail cadastrado), se não for provedor comum.</summary>
    public static string? Dominio(string email)
    {
        var at = email.IndexOf('@');
        if (at < 0) return null;
        var d = email[(at + 1)..].Trim().ToLowerInvariant();
        if (d == "" || Provedores.Contains(d) || !d.Contains('.')) return null;
        return d;
    }

    public static string UrlRdap(string dominio) => "https://rdap.registro.br/domain/" + dominio;

    public static DateOnly? LerRdap(string json)
    {
        try
        {
            if (JsonNode.Parse(json)?["events"] is not JsonArray a) return null;
            foreach (var e in a)
                if (LeitorCnpj.S(e, "eventAction") == "registration") return LeitorCnpj.D(LeitorCnpj.S(e, "eventDate"));
        }
        catch { }
        return null;
    }

    public static string UrlGeocode(Empresa e)
    {
        var q = string.Join(", ", new[] { e.Logradouro + (e.Numero != "" && e.Numero != "S/N" ? " " + e.Numero : ""), e.Municipio, e.Uf, "Brasil" }.Where(x => x.Trim() != ""));
        return "https://nominatim.openstreetmap.org/search?format=json&limit=1&countrycodes=br&q=" + Uri.EscapeDataString(q);
    }

    public static string UrlGeocodeCidade(Empresa e) =>
        "https://nominatim.openstreetmap.org/search?format=json&limit=1&countrycodes=br&q=" + Uri.EscapeDataString($"{e.Municipio}, {e.Uf}, Brasil");

    public static (double lat, double lon)? LerGeocode(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonArray a || a.Count == 0) return null;
            var lat = double.Parse(LeitorCnpj.S(a[0], "lat"), CultureInfo.InvariantCulture);
            var lon = double.Parse(LeitorCnpj.S(a[0], "lon"), CultureInfo.InvariantCulture);
            return (lat, lon);
        }
        catch { return null; }
    }

    public static string UrlMapa((double lat, double lon) p, double zoom = 0.004)
    {
        string F(double x) => x.ToString("0.######", CultureInfo.InvariantCulture);
        return $"https://www.openstreetmap.org/export/embed.html?bbox={F(p.lon - zoom * 1.6)},{F(p.lat - zoom)},{F(p.lon + zoom * 1.6)},{F(p.lat + zoom)}&layer=mapnik&marker={F(p.lat)},{F(p.lon)}";
    }

    public static string UrlMapaAbrir(Empresa e, (double lat, double lon)? p)
    {
        if (p is { } x) return $"https://www.openstreetmap.org/?mlat={x.lat.ToString(CultureInfo.InvariantCulture)}&mlon={x.lon.ToString(CultureInfo.InvariantCulture)}#map=17/{x.lat.ToString(CultureInfo.InvariantCulture)}/{x.lon.ToString(CultureInfo.InvariantCulture)}";
        return "https://www.openstreetmap.org/search?query=" + Uri.EscapeDataString(e.Endereco);
    }

    public static string UrlIbge(string codigo) => "https://servicodados.ibge.gov.br/api/v1/localidades/municipios/" + Documento.Digitos(codigo);

    public static string? LerIbge(string json)
    {
        try
        {
            var n = JsonNode.Parse(json);
            var micro = n?["microrregiao"];
            var meso = micro?["mesorregiao"];
            var reg = meso?["UF"]?["regiao"];
            var partes = new[] { LeitorCnpj.S(micro, "nome") is { Length: > 0 } m ? "microrregião " + m : "", LeitorCnpj.S(reg, "nome") is { Length: > 0 } r ? "região " + r : "" }.Where(x => x != "");
            var s = string.Join(" · ", partes);
            return s == "" ? null : s;
        }
        catch { return null; }
    }

    // ------------------------- empresas ligadas a um CPF -------------------------

    /// <summary>O sócio bate com a pessoa? Nome igual e os dígitos visíveis do CPF (***456789**) iguais.</summary>
    public static bool MesmoSocio(Socio s, string nome, string cpf)
    {
        if (s.PJ) return false;
        var dig = Documento.Digitos(cpf);
        if (Norm(s.Nome) != Norm(nome) || Norm(nome) == "") return false;
        var vis = Documento.Digitos(s.Documento);
        if (vis.Length == 6 && dig.Length == 11) return dig.Substring(3, 6) == vis;
        if (vis.Length == 11) return vis == dig;
        return true;
    }

    // ------------------------- certidão da Junta (PDF) -------------------------

    static readonly Regex RxData = new(@"\b(\d{2})/(\d{2})/(\d{4})\b");

    /// <summary>Transforma as linhas de uma certidão da Junta em eventos (cada linha com data vira um evento).</summary>
    public static List<Evento> LerCertidaoJunta(IEnumerable<string> linhas)
    {
        var l = new List<Evento>();
        var vistos = new HashSet<string>();
        var lista = linhas.Select(x => Texto.ColapsarEspacos(x.Trim())).Where(x => x != "").ToList();
        for (int i = 0; i < lista.Count; i++)
        {
            var linha = lista[i];
            var m = RxData.Match(linha);
            if (!m.Success) continue;
            if (!DateOnly.TryParseExact(m.Value, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) continue;
            if (d.Year < 1900 || d > DateOnly.FromDateTime(DateTime.Today).AddDays(1)) continue;
            var resto = RxData.Replace(linha, "").Trim(' ', '-', ':', '.', '|', '·');
            var low = Texto.SemAcento(resto).ToLowerInvariant();
            if (low.Contains("emitid") || low.Contains("emissao") || low.Contains("valida") || low.Contains("autenticidade") || low.Contains("pagina")) continue;
            if (resto.Length < 4 && i + 1 < lista.Count && !RxData.IsMatch(lista[i + 1])) resto = lista[i + 1];
            if (resto.Length < 4) resto = "Arquivamento na Junta";
            if (resto.Length > 140) resto = resto[..137] + "…";
            var chave = d + "|" + resto;
            if (!vistos.Add(chave)) continue;
            l.Add(new Evento(d, Cnae.Frase(resto), "", "certidão Junta", "c"));
        }
        return l;
    }

    // ------------------------- texto para o Teams -------------------------

    public static string TextoTeams(Empresa e, IEnumerable<Placa> placar, IEnumerable<Checagem> checks, string? parecer, DateOnly hoje)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"📡 Radar Smart360 · {e.RazaoSocial}");
        sb.AppendLine($"CNPJ {e.CnpjFmt} · {(e.Matriz ? "Matriz" : "Filial")} · {Cnae.Frase(e.Situacao)} desde {Dmy(e.DataSituacao)}");
        sb.AppendLine($"Aberta em {Dmy(e.Abertura)} ({e.Idade(hoje)}) · {e.Municipio}/{e.Uf} · capital {Dinheiro(e.Capital)}");
        if (e.Principal is { } p) sb.AppendLine($"Atividade: {p.CodigoFmt} · {p.Descricao}");
        sb.AppendLine();
        sb.AppendLine("Placar:");
        foreach (var x in placar) sb.AppendLine($"{Emoji(x.Cor)} {x.Dimensao}: {x.Texto}");
        var alertas = checks.Where(c => c.Status is StatusCheck.Consta or StatusCheck.Atencao).ToList();
        if (alertas.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Pontos de atenção:");
            foreach (var c in alertas) sb.AppendLine($"• {c.Titulo}{(c.Detalhe != "" ? ": " + c.Detalhe : "")}");
        }
        if (!string.IsNullOrWhiteSpace(parecer)) { sb.AppendLine(); sb.AppendLine("Parecer do Mr. Smart: " + parecer); }
        sb.AppendLine();
        sb.Append($"Consulta pública em {DateTime.Now:dd/MM/yyyy HH:mm} · dados da Receita podem ter até 30 dias de atraso.");
        return sb.ToString();
    }

    public static string Emoji(string cor) => cor switch { "g" => "🟢", "y" => "🟡", "rr" => "🔴", "b" => "🔵", _ => "⚪" };
}

// ----------------------------------------------------------------------------
//  Histórico salvo (só CNPJ, criptografado no navegador; sem nome de sócio)
// ----------------------------------------------------------------------------

public sealed class Retrato
{
    public DateTime Quando { get; set; }
    public string Situacao { get; set; } = "";
    public string DataSituacao { get; set; } = "";
    public string Razao { get; set; } = "";
    public string Natureza { get; set; } = "";
    public string Porte { get; set; } = "";
    public decimal? Capital { get; set; }
    public string Cnae { get; set; } = "";
    public int Secundarias { get; set; }
    public string Endereco { get; set; } = "";
    public bool? Simples { get; set; }
    public bool? Mei { get; set; }
    /// <summary>Impressão digital de cada sócio (não guarda o nome).</summary>
    public List<string> Socios { get; set; } = new();

    public static string Digital(Socio s)
    {
        var b = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("qsmart-radar|" + Radar.Norm(s.Nome) + "|" + Documento.Digitos(s.Documento)));
        return Convert.ToHexString(b)[..16];
    }

    public static Retrato De(Empresa e, DateTime quando) => new()
    {
        Quando = quando, Situacao = e.Situacao, DataSituacao = Radar.Dmy(e.DataSituacao), Razao = e.RazaoSocial, Natureza = e.Natureza, Porte = e.Porte,
        Capital = e.Capital, Cnae = e.Principal?.Codigo ?? "", Secundarias = e.Atividades.Count(a => !a.Principal), Endereco = e.Endereco,
        Simples = e.Simples, Mei = e.Mei, Socios = e.Socios.Select(Digital).OrderBy(x => x).ToList(),
    };

    public static List<string> Diferencas(Retrato a, Retrato b)
    {
        var l = new List<string>();
        if (a.Situacao != b.Situacao) l.Add($"situação {Cnae_(a.Situacao)} → {Cnae_(b.Situacao)}");
        if (a.Razao != b.Razao) l.Add("razão social mudou");
        if (a.Natureza != b.Natureza) l.Add("natureza jurídica mudou");
        if (a.Porte != b.Porte) l.Add($"porte {a.Porte} → {b.Porte}");
        if (a.Capital != b.Capital) l.Add($"capital {Radar.Dinheiro(a.Capital)} → {Radar.Dinheiro(b.Capital)}");
        if (a.Cnae != b.Cnae) l.Add("atividade principal mudou");
        if (a.Secundarias != b.Secundarias) l.Add($"atividades secundárias {a.Secundarias} → {b.Secundarias}");
        if (a.Endereco != b.Endereco) l.Add("endereço mudou");
        if (a.Simples != b.Simples) l.Add(b.Simples == true ? "entrou no Simples" : "saiu do Simples");
        if (a.Mei != b.Mei) l.Add(b.Mei == true ? "virou MEI" : "deixou de ser MEI");
        var entr = b.Socios.Except(a.Socios).Count();
        var sai = a.Socios.Except(b.Socios).Count();
        if (entr > 0) l.Add(entr == 1 ? "1 sócio entrou" : $"{entr} sócios entraram");
        if (sai > 0) l.Add(sai == 1 ? "1 sócio saiu" : $"{sai} sócios saíram");
        return l;
    }

    static string Cnae_(string s) => Relacionamento.Cnae.Frase(s);
}

public sealed class Recente
{
    public string Cnpj { get; set; } = "";
    public string Nome { get; set; } = "";
    public DateTime Quando { get; set; }
    public bool Monitorado { get; set; }
    public string? UltimaMudanca { get; set; }
}

public sealed class BaseRadar
{
    public List<Recente> Recentes { get; set; } = new();
    public Dictionary<string, List<Retrato>> Retratos { get; set; } = new();

    public const int MaxRecentes = 30, MaxRetratos = 12;

    /// <summary>Registra uma consulta de CNPJ. Só guarda um retrato novo se algo mudou (ou o último tem mais de 1 dia).</summary>
    public List<string> Registrar(Empresa e, DateTime agora)
    {
        var r = Recentes.FirstOrDefault(x => x.Cnpj == e.Cnpj);
        if (r == null) { r = new Recente { Cnpj = e.Cnpj }; Recentes.Insert(0, r); }
        else { Recentes.Remove(r); Recentes.Insert(0, r); }
        r.Nome = e.Fantasia != "" ? e.Fantasia : e.RazaoSocial;
        r.Quando = agora;
        while (Recentes.Count > MaxRecentes)
        {
            var sai = Recentes.LastOrDefault(x => !x.Monitorado) ?? Recentes[^1];
            Recentes.Remove(sai);
            Retratos.Remove(sai.Cnpj);
        }
        var novo = Retrato.De(e, agora);
        if (!Retratos.TryGetValue(e.Cnpj, out var l)) Retratos[e.Cnpj] = l = new();
        var dif = l.Count > 0 ? Retrato.Diferencas(l[^1], novo) : new List<string>();
        if (l.Count == 0 || dif.Count > 0 || (agora - l[^1].Quando).TotalHours > 24) l.Add(novo);
        while (l.Count > MaxRetratos) l.RemoveAt(1);   // guarda sempre o primeiro
        if (dif.Count > 0) r.UltimaMudanca = $"{agora:dd/MM}: " + string.Join(", ", dif);
        return dif;
    }

    public List<Retrato> De(string cnpj) => Retratos.TryGetValue(cnpj, out var l) ? l : new();

    public byte[] ParaBytes() => JsonSerializer.SerializeToUtf8Bytes(this);

    public static BaseRadar DeBytes(byte[]? b)
    {
        if (b == null || b.Length == 0) return new();
        try { return JsonSerializer.Deserialize<BaseRadar>(b) ?? new(); } catch { return new(); }
    }
}

// ----------------------------------------------------------------------------
//  Parecer do Mr. Smart (Groq)
// ----------------------------------------------------------------------------

public static class RadarIA
{
    const string Instrucoes = """
        Você é o Mr. Smart, analista de cadastro e compliance de uma cooperativa de crédito (Sicoob).
        Recebe fatos públicos já apurados sobre uma empresa (CNPJ) ou uma pessoa (CPF) e escreve um parecer curto em português do Brasil.
        Regras:
        - 3 a 5 frases, linguagem simples e direta, sem jargão desnecessário.
        - Diga primeiro a situação geral; depois os pontos de atenção, se houver; termine com o que vale conferir antes de decidir.
        - Use só os fatos recebidos. Não invente dados, não suponha culpa e não chame registro de crime.
        - Fontes marcadas como "conferir no site" ou "sem chave" não foram consultadas: não diga que estão limpas.
        - Para pessoa politicamente exposta, lembre que a regra de prevenção à lavagem de dinheiro pede diligência reforçada.
        Responda só com JSON: {"parecer": "..."}
        """;

    public static string Pedido(string fatos, string modelo) => new JsonObject
    {
        ["model"] = modelo,
        ["temperature"] = 0.2,
        ["max_completion_tokens"] = 1800,
        ["response_format"] = new JsonObject { ["type"] = "json_object" },
        ["messages"] = new JsonArray(
            new JsonObject { ["role"] = "system", ["content"] = Instrucoes },
            new JsonObject { ["role"] = "user", ["content"] = fatos }),
    }.ToJsonString();

    public static string? Ler(string corpoHttp)
    {
        try
        {
            var conteudo = JsonNode.Parse(corpoHttp)?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? "";
            var i = conteudo.IndexOf('{'); var f = conteudo.LastIndexOf('}');
            if (i >= 0 && f > i)
            {
                var p = JsonNode.Parse(conteudo[i..(f + 1)])?["parecer"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(p)) return p.Trim();
            }
            return string.IsNullOrWhiteSpace(conteudo) ? null : conteudo.Trim();
        }
        catch { return null; }
    }

    /// <summary>Fatos de um CNPJ, compactos (limite do plano gratuito).</summary>
    public static string FatosPJ(Empresa e, IEnumerable<Checagem> checks, DateOnly hoje)
    {
        var sb = new StringBuilder("EMPRESA (CNPJ)\n");
        sb.AppendLine($"Razão social: {e.RazaoSocial}; {(e.Matriz ? "matriz" : "filial")}; natureza: {e.Natureza}; porte: {e.Porte}; capital: {Radar.Dinheiro(e.Capital)}");
        sb.AppendLine($"Situação: {e.Situacao} desde {Radar.Dmy(e.DataSituacao)}{(e.MotivoSituacao != "" ? " (" + e.MotivoSituacao + ")" : "")}; aberta em {Radar.Dmy(e.Abertura)} ({e.Idade(hoje)}); {e.Municipio}/{e.Uf}");
        if (e.Principal is { } p) sb.AppendLine($"Atividade principal: {p.Descricao}");
        sb.AppendLine($"Simples: {(e.Simples == true ? "sim" : "não")}; MEI: {(e.Mei == true ? "sim" : "não")}");
        sb.AppendLine($"Quadro de sócios/dirigentes: {e.Socios.Count} pessoas; entrada mais recente: {Radar.Dmy(e.Socios.Where(s => s.Entrada != null).Select(s => s.Entrada).DefaultIfEmpty().Max())}");
        Checks(sb, checks);
        return sb.ToString();
    }

    /// <summary>Fatos de um CPF, sem nome e sem número.</summary>
    public static string FatosPF(string finalidade, IEnumerable<Checagem> checks, int empresas)
    {
        var sb = new StringBuilder("PESSOA (CPF) — nome e número omitidos\n");
        sb.AppendLine($"Finalidade da consulta: {finalidade}");
        sb.AppendLine($"Empresas ligadas encontradas: {empresas}");
        Checks(sb, checks);
        return sb.ToString();
    }

    static void Checks(StringBuilder sb, IEnumerable<Checagem> checks)
    {
        sb.AppendLine("Checagens:");
        foreach (var c in checks.Where(c => c.Status != StatusCheck.Carregando))
        {
            var st = c.Status switch
            {
                StatusCheck.NadaConsta => "nada consta", StatusCheck.Consta => "CONSTA", StatusCheck.Atencao => "atenção",
                StatusCheck.Info => "informativo", StatusCheck.SemChave => "sem chave (não consultado)", StatusCheck.Erro => "não respondeu (não consultado)",
                _ => "conferir no site (não consultado)",
            };
            sb.AppendLine($"- {c.Titulo}: {st}{(c.Detalhe != "" ? " — " + c.Detalhe : "")}");
        }
    }
}
