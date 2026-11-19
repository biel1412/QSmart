using QSmart.Core.Relacionamento;

namespace QSmart.Core.Pdf;

/// <summary>Ficha do Radar Smart360 em PDF (tema claro, para imprimir ou anexar).</summary>
public static class RadarPdf
{
    static readonly Cor INK = Cor.Hex("#0F172A"), MUT = Cor.Hex("#64748B"), GRID = Cor.Hex("#CBD5E1"), ROSA = Cor.Hex("#DB2777"),
        VERDE = Cor.Hex("#16A34A"), AMAR = Cor.Hex("#D97706"), VERM = Cor.Hex("#DC2626"), AZUL = Cor.Hex("#0284C7"), CINZA = Cor.Hex("#94A3B8");

    static readonly Estilo StTitulo = new() { Fonte = Fonte.Negrito, Tamanho = 18, Entrelinha = 22, Cor = INK, Depois = 3 };
    static readonly Estilo StSub = new() { Tamanho = 9, Entrelinha = 12.5, Cor = MUT };
    static readonly Estilo StH2 = new() { Fonte = Fonte.Negrito, Tamanho = 12, Entrelinha = 16, Cor = INK, Antes = 12, Depois = 2 };
    static readonly Estilo StTxt = new() { Tamanho = 9, Entrelinha = 12.5, Cor = INK, Depois = 2 };
    static readonly Estilo StNota = new() { Tamanho = 7.5, Entrelinha = 10, Cor = MUT };

    static string Esc(string s) => System.Net.WebUtility.HtmlEncode(s);

    static List<Elemento> Secao(string t) => new() { new Paragrafo(Esc(t), StH2), new LinhaH { Cor = GRID, EspacoAntes = 1, EspacoDepois = 5 } };

    static Cor CorDe(string c) => c switch { "g" => VERDE, "y" => AMAR, "rr" => VERM, "b" => AZUL, _ => CINZA };

    static Desenho Topo(double L, string rotulo)
    {
        var d = new Desenho(L, 20);
        d.Txt(0, 7, rotulo, Fonte.Negrito, 8, ROSA);
        d.Txt(L, 7, "QSmart · Radar Smart360", Fonte.Negrito, 9, INK, 'd');
        return d;
    }

    static Desenho Placar(double L, IReadOnlyList<Placa> placar)
    {
        const double linha = 16;
        int metade = (placar.Count + 1) / 2;
        var d = new Desenho(L, linha * metade + 2);
        for (int i = 0; i < placar.Count; i++)
        {
            var col = i / metade; var row = i % metade;
            double x = col * L / 2, y = d.AlturaFixa - (row + 1) * linha + 3;
            d.Ret(x, y + 1, 8, 8, CorDe(placar[i].Cor));
            d.Txt(x + 14, y + 2, placar[i].Dimensao, Fonte.Normal, 9, INK);
            d.Txt(x + L / 2 - 14, y + 2, placar[i].Texto, Fonte.Negrito, 8.5, CorDe(placar[i].Cor), 'd');
        }
        return d;
    }

    static string StatusTxt(Checagem c) => c.Status switch
    {
        StatusCheck.NadaConsta => "nada consta", StatusCheck.Consta => "CONSTA", StatusCheck.Atencao => "atenção",
        StatusCheck.Info => "informativo", StatusCheck.SemChave => "sem chave", StatusCheck.Erro => "não respondeu",
        StatusCheck.Atalho => "conferir no site", _ => "—",
    };

    static void Checagens(List<Elemento> s, IEnumerable<Checagem> checks)
    {
        s.AddRange(Secao("Checagens automáticas"));
        foreach (var c in checks)
            s.Add(new Paragrafo($"<b>{Esc(c.Titulo)}</b> — {Esc(StatusTxt(c))}{(c.Detalhe != "" ? " · " + Esc(c.Detalhe) : "")} ({Esc(c.Fonte)})", StTxt));
    }

    public static byte[] Cnpj(Empresa e, IReadOnlyList<Placa> placar, IEnumerable<Checagem> checks, IEnumerable<Evento> linha, string? parecer, DateOnly hoje)
    {
        var doc = new Documento { Titulo = "Radar Smart360 · " + e.RazaoSocial, Rodape = $"QSmart · Radar Smart360 — CNPJ {e.CnpjFmt} · fontes públicas; dados da Receita podem ter até 30 dias de atraso" };
        double L = doc.LarguraUtil;
        var s = new List<Elemento> { Topo(L, "FICHA 360 · CNPJ"), new Paragrafo(Esc(e.RazaoSocial), StTitulo) };
        s.Add(new Paragrafo($"CNPJ <b>{e.CnpjFmt}</b> · {(e.Matriz ? "Matriz" : "Filial")}{(e.Fantasia != "" ? " · Nome fantasia: " + Esc(e.Fantasia) : "")} · Gerado em {DateTime.Now:dd/MM/yyyy HH:mm}", StSub));
        s.Add(new Espaco(8));
        s.AddRange(Secao("Ficha"));
        s.Add(new Paragrafo($"<b>Situação:</b> {Esc(Cnae.Frase(e.Situacao))} desde {Radar.Dmy(e.DataSituacao)}{(e.MotivoSituacao != "" && !e.MotivoSituacao.Equals("SEM MOTIVO", StringComparison.OrdinalIgnoreCase) ? " (" + Esc(Cnae.Frase(e.MotivoSituacao)) + ")" : "")}", StTxt));
        s.Add(new Paragrafo($"<b>Abertura:</b> {Radar.Dmy(e.Abertura)} ({e.Idade(hoje)}) · <b>Natureza:</b> {Esc(e.Natureza)} · <b>Porte:</b> {Esc(e.Porte)} · <b>Capital:</b> {Radar.Dinheiro(e.Capital)}", StTxt));
        s.Add(new Paragrafo($"<b>Simples:</b> {(e.Simples == true ? "optante desde " + Radar.Dmy(e.SimplesDesde) : "não optante")} · <b>MEI:</b> {(e.Mei == true ? "sim" : "não")}", StTxt));
        s.Add(new Paragrafo($"<b>Endereço:</b> {Esc(e.Endereco)}", StTxt));
        foreach (var a in e.Atividades.Take(8))
            s.Add(new Paragrafo($"<b>{(a.Principal ? "Atividade principal" : "Secundária")}:</b> {a.CodigoFmt} · {Esc(a.Descricao)}", StTxt));
        s.AddRange(Secao("Placar do radar"));
        s.Add(Placar(L, placar));
        if (!string.IsNullOrWhiteSpace(parecer)) { s.AddRange(Secao("Parecer do Mr. Smart")); s.Add(new Paragrafo(Esc(parecer), StTxt)); }
        Checagens(s, checks);
        if (e.Socios.Count > 0)
        {
            s.AddRange(Secao("Sócios e dirigentes (QSA)"));
            foreach (var p in e.Socios)
                s.Add(new Paragrafo($"<b>{Esc(Radar.NomeProprio(p.Nome))}</b> · {Esc(Radar.Qualificacao(p.Qualificacao))}{(p.Entrada != null ? " · desde " + Radar.Dmy(p.Entrada) : "")}{(p.PJ ? " · pessoa jurídica" : "")}", StTxt));
        }
        var ev = linha.ToList();
        if (ev.Count > 0)
        {
            s.AddRange(Secao("Linha do tempo"));
            foreach (var x in ev.Take(40))
                s.Add(new Paragrafo($"<b>{Radar.Dmy(x.Data)}</b> · {Esc(x.Titulo)}{(x.Detalhe != "" ? " — " + Esc(x.Detalhe) : "")} ({Esc(x.Fonte)})", StTxt));
        }
        s.Add(new Espaco(10));
        s.Add(new Paragrafo("Consulta feita só em fontes públicas. Itens marcados como “conferir no site” ou “sem chave” não foram verificados automaticamente.", StNota));
        return doc.Gerar(s);
    }

    public static byte[] Cpf(string nome, string cpfMascarado, string finalidade, IReadOnlyList<Placa> placar, IEnumerable<Checagem> checks,
        IEnumerable<string> empresas, string? parecer)
    {
        var doc = new Documento { Titulo = "Radar Smart360 · consulta de CPF", Rodape = $"QSmart · Radar Smart360 — CPF {cpfMascarado} · finalidade: {finalidade}" };
        double L = doc.LarguraUtil;
        var s = new List<Elemento> { Topo(L, "CONSULTA DE CPF"), new Paragrafo(Esc(nome != "" ? nome : "Pessoa consultada"), StTitulo) };
        s.Add(new Paragrafo($"CPF <b>{cpfMascarado}</b> · Finalidade: <b>{Esc(finalidade)}</b> · Gerado em {DateTime.Now:dd/MM/yyyy HH:mm}", StSub));
        s.Add(new Espaco(8));
        s.AddRange(Secao("Placar do radar"));
        s.Add(Placar(L, placar));
        if (!string.IsNullOrWhiteSpace(parecer)) { s.AddRange(Secao("Parecer do Mr. Smart")); s.Add(new Paragrafo(Esc(parecer), StTxt)); }
        Checagens(s, checks);
        var emp = empresas.ToList();
        if (emp.Count > 0)
        {
            s.AddRange(Secao("Empresas ligadas"));
            foreach (var x in emp) s.Add(new Paragrafo(Esc(x), StTxt));
        }
        s.Add(new Espaco(10));
        s.Add(new Paragrafo("Consulta feita só em fontes públicas, para a finalidade indicada (LGPD). Nada desta consulta fica salvo no QSmart.", StNota));
        return doc.Gerar(s);
    }
}
