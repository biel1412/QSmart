namespace QSmart.Core.Pdf;

/// <summary>Relatórios em PDF (Resumo e Evolução), sempre em tema claro, bons para imprimir.</summary>
public static class Relatorios
{
    static readonly Cor INK = Cor.Hex("#0F172A"), MUT = Cor.Hex("#64748B"), GRN = Cor.Hex("#16A34A"), RD = Cor.Hex("#DC2626"),
        BL = Cor.Hex("#0284C7"), AMB = Cor.Hex("#D97706"), PUR = Cor.Hex("#7C3AED"), HEAD = Cor.Hex("#1E293B"),
        ZEBRA = Cor.Hex("#F1F5F9"), GRID = Cor.Hex("#CBD5E1"), MARCA = Cor.Hex("#4F46E5");

    static readonly Estilo StTitulo = new() { Fonte = Fonte.Negrito, Tamanho = 22, Entrelinha = 26, Cor = INK, Depois = 6 };
    static readonly Estilo StSub = new() { Tamanho = 9.5, Entrelinha = 13, Cor = MUT };
    static readonly Estilo StH2 = new() { Fonte = Fonte.Negrito, Tamanho = 13.5, Entrelinha = 18, Cor = INK, Antes = 14, Depois = 2 };
    static readonly Estilo StNota = new() { Tamanho = 8, Entrelinha = 10.5, Cor = MUT };
    static readonly Estilo StKLbl = new() { Tamanho = 7.5, Entrelinha = 9, Cor = MUT };
    static readonly Estilo StKDet = new() { Tamanho = 7.5, Entrelinha = 9.5, Cor = MUT };

    static string Pct(double x) => Texto.F(x) + "%";
    static string Esc(string s) => System.Net.WebUtility.HtmlEncode(s);
    static string Fmt(long n) => Texto.Fmt(n);

    static List<Elemento> Secao(string titulo, string? nota = null)
    {
        var itens = new List<Elemento> { new Paragrafo(Esc(titulo), StH2), new LinhaH { Cor = GRID, Espessura = 0.8, EspacoAntes = 1, EspacoDepois = 6 } };
        if (nota != null) itens.AddRange(new Elemento[] { new Paragrafo(nota, StNota), new Espaco(4) });
        return itens;
    }

    static Desenho BarrasH(List<(string nome, long qtd)> itens, long total, Cor corPrimeira, Cor cor, double largura = 500, double rotuloW = 150,
        double linha = 17, List<Cor>? cores = null)
    {
        var h = Math.Max(linha * itens.Count + 6, 20);
        var d = new Desenho(largura, h);
        if (itens.Count == 0) return d;
        var maximo = Math.Max(1, itens.Max(x => x.qtd));
        var area = largura - rotuloW - 85;
        for (int i = 0; i < itens.Count; i++)
        {
            var (nome, qtd) = itens[i];
            var y = h - (i + 1) * linha + 2;
            d.Txt(0, y + 4, Fontes.Cortar(nome, rotuloW - 6), Fonte.Normal, 8, INK);
            var comp = Math.Max(area * qtd / maximo, 1.5);
            d.Ret(rotuloW, y, comp, linha - 6, cores != null ? cores[i] : i == 0 ? corPrimeira : cor);
            d.Txt(rotuloW + comp + 5, y + 3, $"{Fmt(qtd)}  ({Pct(qtd / (double)(total == 0 ? 1 : total) * 100)})", Fonte.Negrito, 7.5, MUT);
        }
        return d;
    }

    static Desenho BarrasV(long aprov, long reprov, double pa, double pr, double w = 150, double h = 150)
    {
        var d = new Desenho(w, h);
        var maximo = Math.Max(Math.Max(aprov, reprov), 1);
        double baseY = 28, topo = h - 24, larg = 44;
        var itens = new[] { ("Aprovações", aprov, GRN, pa), ("Reprovações", reprov, RD, pr) };
        for (int i = 0; i < 2; i++)
        {
            var (nome, val, cor, p) = itens[i];
            var cx = w / 4 + i * w / 2;
            var alt = Math.Max((topo - baseY) * val / maximo, 1.5);
            d.Ret(cx - larg / 2, baseY, larg, alt, cor);
            d.Txt(cx, baseY + alt + 4, Fmt(val), Fonte.Negrito, 10, INK, 'c');
            d.Txt(cx, baseY - 11, nome, Fonte.Negrito, 8, INK, 'c');
            d.Txt(cx, baseY - 21, Pct(p), Fonte.Normal, 7.5, MUT, 'c');
        }
        d.Lin(8, baseY, w - 8, baseY, GRID, 0.8);
        return d;
    }

    static TabelaPdf Tabela(string[] cab, List<object?[]> linhas, double[] larguras, int[]? alinharEsq = null, List<EstiloTab>? destaque = null)
    {
        alinharEsq ??= new[] { 1 };
        var dados = new List<object?[]> { cab.Cast<object?>().ToArray() };
        dados.AddRange(linhas);
        var est = new List<EstiloTab>
        {
            new("BACKGROUND", (0, 0), (-1, 0), HEAD), new("TEXTCOLOR", (0, 0), (-1, 0), Cor.Branco),
            new("FONTNAME", (0, 0), (-1, 0), Fonte.Negrito), new("FONTSIZE", (0, 0), (-1, -1), 8),
            new("TEXTCOLOR", (0, 1), (-1, -1), INK), new("ALIGN", (0, 0), (-1, -1), 'c'),
            new("VALIGN", (0, 0), (-1, -1), 'm'), new("TOPPADDING", (0, 0), (-1, -1), 3.2),
            new("BOTTOMPADDING", (0, 0), (-1, -1), 3.2), new("LINEBELOW", (0, 0), (-1, -1), 0.25, GRID),
            new("ROWBACKGROUNDS", (0, 1), (-1, -1), new[] { Cor.Branco, ZEBRA }),
        };
        foreach (var c in alinharEsq) { est.Add(new("ALIGN", (c, 0), (c, -1), 'e')); est.Add(new("LEFTPADDING", (c, 0), (c, -1), 6)); }
        if (destaque != null) est.AddRange(destaque);
        return new TabelaPdf(dados, larguras, est, 1);
    }

    static List<Elemento> Kpi(string lbl, string valor, Cor cor, string det, double tam = 17)
        => new() { new Paragrafo(Esc(lbl), StKLbl), new Paragrafo(valor, new Estilo { Fonte = Fonte.Negrito, Tamanho = tam, Entrelinha = tam + 4, Cor = cor }), new Paragrafo(det, StKDet) };

    static TabelaPdf TabelaKpis(List<List<Elemento>> celulas, double L, Cor[] barras, double padE)
    {
        var est = new List<EstiloTab>
        {
            new("BOX", (0, 0), (-1, -1), 0.6, GRID), new("INNERGRID", (0, 0), (-1, -1), 0.6, GRID),
            new("BACKGROUND", (0, 0), (-1, -1), Cor.Hex("#F8FAFC")), new("VALIGN", (0, 0), (-1, -1), 't'),
            new("LEFTPADDING", (0, 0), (-1, -1), padE), new("TOPPADDING", (0, 0), (-1, -1), 7), new("BOTTOMPADDING", (0, 0), (-1, -1), 7),
        };
        for (int i = 0; i < barras.Length; i++) est.Add(new("LINEBEFORE", (i, 0), (i, 0), 3, barras[i]));
        return new TabelaPdf(new List<object?[]> { celulas.Cast<object?>().ToArray() }, Enumerable.Repeat(L / celulas.Count, celulas.Count).ToArray(), est);
    }

    /// <summary>Faixa da marca no topo da primeira página.</summary>
    static Desenho Marca(double L)
    {
        var d = new Desenho(L, 22);
        d.Ret(L - 112, 3, 16, 16, MARCA);
        d.Txt(L - 104, 7, "Q", Fonte.Negrito, 11, Cor.Branco, 'c');
        d.Txt(L - 91, 10, "QSmart", Fonte.Negrito, 11, INK);
        d.Txt(L - 91 + Fontes.Largura("QSmart ", Fonte.Negrito, 11), 10, "by Insi", Fonte.Normal, 8, MUT);
        return d;
    }

    static Documento Doc(string arquivo, string titulo) => new()
    {
        Titulo = titulo, Rodape = $"QSmart · Relatório da Qualidade — {arquivo}",
    };

    /// <summary>gerar_pdf: relatório completo do Resumo.</summary>
    public static byte[] Resumo(ResultadoResumo r)
    {
        var doc = Doc(r.Arquivo, "Relatório da Qualidade");
        double L = doc.LarguraUtil;
        var story = new List<Elemento> { Marca(L) };
        story.Add(new Paragrafo("Relatório da Qualidade", StTitulo));
        story.Add(new Paragrafo($"Arquivo: <b>{Esc(r.Arquivo)}</b> &nbsp;•&nbsp; {Fmt(r.Linhas)} registros &nbsp;•&nbsp; {r.Usuarios} usuários " +
                                $"&nbsp;•&nbsp; Gerado em {Texto.Agora()}", StSub));
        if (r.FiltrosTexto != "") story.Add(new Paragrafo($"<b>Filtros aplicados:</b> {Esc(r.FiltrosTexto)}", StSub));
        story.Add(new Espaco(10));

        var (vence, corV) = r.Aprov > r.Reprov ? ("Aprovações", GRN) : r.Reprov > r.Aprov ? ("Reprovações", RD) : ("Empate", AMB);
        (string tipo, long qtd)? t0 = r.Tipos.Count > 0 ? r.Tipos[0] : null;
        var celulas = new List<List<Elemento>>
        {
            Kpi("APROVAÇÕES", Fmt(r.Aprov), GRN, $"{Pct(r.PctAprov)} das decisões"),
            Kpi("REPROVAÇÕES", Fmt(r.Reprov), RD, $"{Pct(r.PctReprov)} • " + string.Join(" + ", r.ReprovDetalhe.Take(3).Select(x => $"{Fmt(x.n)} {Esc(Classes.RotuloProc(x.proc))}"))),
            Kpi("QUEM TEM MAIS", vence, corV, $"Diferença de {Fmt(Math.Abs(r.Aprov - r.Reprov))}", 14),
            Kpi("MAIOR TIPO REPROVADO", t0 != null ? Esc(Texto.Rotulo(t0.Value.tipo)) : "—", PUR,
                t0 != null ? $"{Fmt(t0.Value.qtd)} ocorrências ({Pct(t0.Value.qtd / (double)(r.Reprov == 0 ? 1 : r.Reprov) * 100)})" : "Sem reprovações", 11),
        };
        story.Add(TabelaKpis(celulas, L, new[] { GRN, RD, AMB, PUR }, 8));

        story.AddRange(Secao("Aprovações x Reprovações", Esc(r.RegrasTexto != "" ? r.RegrasTexto :
            "Aprovação = APROVAR. Reprovação (devolutiva) = REPROVAR + DEVOLVER PARA CORREÇÃO. Demais procedimentos ficam fora da comparação.")));
        double totalReg = r.Linhas == 0 ? 1 : r.Linhas;
        var corClasse = new Dictionary<string, Cor> { ["Aprovação"] = GRN, ["Reprovação (devolutiva)"] = RD };
        var linhasProc = r.Procedimentos.Select(p => new object?[] { p.Procedimento, Fmt(p.Qtd), Pct(p.Qtd / totalReg * 100), p.Classe }).ToList();
        var dest = r.Procedimentos.Select((p, i) => new EstiloTab("TEXTCOLOR", (3, i + 1), (3, i + 1), corClasse.GetValueOrDefault(p.Classe, MUT))).ToList();
        var tProc = Tabela(new[] { "Procedimento", "Registros", "% do total", "Classificação" }, linhasProc, new double[] { 140, 46, 46, 108 }, new[] { 0, 3 }, dest);
        var lado = new TabelaPdf(new List<object?[]> { new object?[] { new List<Elemento> { BarrasV(r.Aprov, r.Reprov, r.PctAprov, r.PctReprov) }, new List<Elemento> { tProc } } },
            new[] { L - 340, 340 }, new List<EstiloTab> { new("VALIGN", (0, 0), (-1, -1), 't'), new("LEFTPADDING", (0, 0), (-1, -1), 0), new("RIGHTPADDING", (0, 0), (-1, -1), 0) });
        story.Add(lado);

        var seg = new List<Elemento>();
        seg.AddRange(Secao("Devolutivas por segmento", "Segmento = prefixo de IDREGISTROCONTROLADO. A taxa de devolução do segmento é " +
                                                        "devolutivas ÷ (devolutivas + aprovações) dentro dele."));
        seg.Add(BarrasH(r.Segmentos.Select(s => (s.Nome, s.Dev)).ToList(), r.Reprov, AMB, BL, L, 110));
        seg.Add(new Espaco(8));
        var linhasS = r.Segmentos.Select(s =>
        {
            var dec = s.Dev + s.Apr;
            return new object?[] { s.Nome, Fmt(s.Dev), Pct(s.Dev / (double)(r.Reprov == 0 ? 1 : r.Reprov) * 100), Fmt(s.Apr), Pct(dec > 0 ? s.Dev / (double)dec * 100 : 0) };
        }).ToList();
        seg.Add(Tabela(new[] { "Segmento", "Devolutivas", "% das devolutivas", "Aprovações", "Taxa de devolução" }, linhasS,
            new[] { L - 4 * 88, 88, 88, 88, 88 }, new[] { 0 }));
        story.Add(new Junto(seg));

        story.AddRange(Secao($"Tipos de reprovação ({r.Tipos.Count} tipos)"));
        story.Add(BarrasH(r.Tipos.Take(10).Select(x => (Texto.Rotulo(x.tipo), x.qtd)).ToList(), r.Reprov, PUR, BL, L));
        story.Add(new Espaco(8));
        var freq = r.Tipos.Where(x => x.qtd >= 2).ToList();
        var raros = r.Tipos.Where(x => x.qtd < 2).ToList();
        double rep = r.Reprov == 0 ? 1 : r.Reprov;
        var linhasT = freq.Select((x, i) => new object?[] { i + 1, Texto.Rotulo(x.tipo), Fmt(x.qtd), Pct(x.qtd / rep * 100) }).ToList();
        List<EstiloTab>? destT = null;
        if (raros.Count > 0)
        {
            var q = raros.Sum(x => x.qtd);
            linhasT.Add(new object?[] { "", $"Demais tipos ({raros.Count} tipos com 1 ocorrência cada)", Fmt(q), Pct(q / rep * 100) });
            destT = new() { new("TEXTCOLOR", (0, linhasT.Count), (-1, linhasT.Count), MUT), new("FONTNAME", (0, linhasT.Count), (-1, linhasT.Count), Fonte.Italico) };
        }
        story.Add(Tabela(new[] { "#", "Tipo de reprovação", "Quantidade", "% das reprovações" }, linhasT, new[] { 28, L - 28 - 180, 80, 100 }, null, destT));

        var usu = r.Todos.OrderBy(x => x.Usuario, StringComparer.Ordinal).OrderByDescending(x => x.Acoes).OrderByDescending(x => x.Devolutivas).ToList();
        story.Add(new QuebraPagina());
        story.AddRange(Secao($"Ranking completo de usuários ({usu.Count})",
            "Ordenado de quem mais devolveu para quem menos devolveu. Em empate, aparece primeiro quem tem mais ações."));
        story.Add(Tabela(new[] { "#", "Usuário", "Devolutivas", "Aprovações", "Total de ações", "% devolvidas" },
            usu.Select((u, i) => new object?[] { i + 1, u.Usuario, Fmt(u.Devolutivas), Fmt(u.Aprovacoes), Fmt(u.Acoes), Pct(u.Taxa * 100) }).ToList(),
            new[] { 30, L - 30 - 4 * 76, 76, 76, 76, 76 }));
        return doc.Gerar(story);
    }

    /// <summary>gerar_pdf_evolucao: desvios Interno x Externo (x Chamados), por autorização e por inspetor.</summary>
    public static byte[] Evolucao(ResultadoInspecao e)
    {
        var doc = Doc(e.Arquivo, "Relatório da Qualidade — Evolução dos desvios");
        double L = doc.LarguraUtil;
        var origens = e.Origens;
        Cor CorO(string o) => o switch { "Interno" => BL, "Externo" => AMB, "Chamados" => PUR, _ => MUT };
        long tot = e.Avaliacoes, des = e.Desvios;
        double desD = des == 0 ? 1 : des;
        var story = new List<Elemento> { Marca(L) };
        var periodo = e.Periodo != null ? $" &nbsp;•&nbsp; Inspeções de {Texto.Dmy(e.Periodo.Value.ini)} a {Texto.Dmy(e.Periodo.Value.fim)}" : "";
        story.Add(new Paragrafo("Relatório da Qualidade", StTitulo));
        story.Add(new Paragrafo("<b>Evolução dos desvios</b> — leitura de todas as abas de inspetores", StSub));
        story.Add(new Paragrafo($"Arquivo: <b>{Esc(e.Arquivo)}</b> &nbsp;•&nbsp; {e.Abas.Count} abas (inspetores) &nbsp;•&nbsp; {Fmt(tot)} avaliações{periodo} " +
                                $"&nbsp;•&nbsp; Gerado em {Texto.Agora()}", StSub));
        foreach (var (dup, orig) in e.Ignoradas)
            story.Add(new Paragrafo($"Aba “{Esc(dup)}” ignorada: cópia idêntica de “{Esc(orig)}” (evita contar em dobro).", StNota.Com(x => x.Cor = AMB)));
        story.Add(new Espaco(10));
        long Q(string o) => e.Q(o);
        var celulas = new List<List<Elemento>>
        {
            Kpi("INSPEÇÕES AVALIADAS", Fmt(tot), INK, $"em {e.Abas.Count} abas"),
            Kpi("DESVIOS", Fmt(des), RD, $"{Pct(des / (double)(tot == 0 ? 1 : tot) * 100)} das avaliações"),
            Kpi("DESVIOS INTERNOS", Fmt(Q("Interno")), BL, $"{Pct(Q("Interno") / desD * 100)} dos desvios"),
            Kpi("DESVIOS EXTERNOS", Fmt(Q("Externo")), AMB, $"{Pct(Q("Externo") / desD * 100)} dos desvios"),
            Kpi("DESVIOS DE CHAMADOS", Fmt(Q("Chamados")), PUR, $"{Pct(Q("Chamados") / desD * 100)} dos desvios"),
        };
        story.Add(TabelaKpis(celulas, L, new[] { INK, RD, BL, AMB, PUR }, 7));

        List<EstiloTab> EstiloTotal(int i) => new()
        {
            new("FONTNAME", (0, i), (-1, i), Fonte.Negrito), new("BACKGROUND", (0, i), (-1, i), Cor.Hex("#E2E8F0")), new("LINEABOVE", (0, i), (-1, i), 0.8, INK),
        };

        Desenho Empilhadas(List<(string rot, LinhaOrigem v)> linhas, double rotuloW = 110, double linha = 15)
        {
            double topo = 20, h = topo + linha * linhas.Count + 4;
            var d = new Desenho(L, h);
            double x = 0;
            foreach (var o in origens)
            {
                d.Ret(x, h - 11, 9, 9, CorO(o));
                d.Txt(x + 13, h - 10, o, Fonte.Negrito, 7.5, INK);
                x += 13 + 5.2 * o.Length + 18;
            }
            var maximo = Math.Max(1, linhas.Select(l => origens.Sum(o => l.v.O(o))).DefaultIfEmpty(0).Max());
            var area = L - rotuloW - 40;
            for (int i = 0; i < linhas.Count; i++)
            {
                var (nome, v) = linhas[i];
                var y = h - topo - (i + 1) * linha + 4;
                d.Txt(0, y + 3.5, Fontes.Cortar(nome, rotuloW - 6), Fonte.Normal, 8, INK);
                double x0 = rotuloW;
                foreach (var o in origens)
                {
                    var n = v.O(o);
                    if (n <= 0) continue;
                    var comp = Math.Max(area * n / maximo, 2);
                    d.Ret(x0, y - 1, comp, linha - 4, CorO(o));
                    if (comp >= 13) d.Txt(x0 + comp / 2, y + 2.2, n.ToString(), Fonte.Negrito, 7, Cor.Branco, 'c');
                    x0 += comp;
                }
                d.Txt(x0 + 5, y + 3, Fmt(origens.Sum(o => v.O(o))), Fonte.Negrito, 8, INK);
            }
            return d;
        }

        story.AddRange(Secao("Desvios: Interno x Externo",
            "Desvio = avaliação com tipo de erro diferente de “Sem erro”. A coluna “Interno ou Externo” da planilha tem três valores: " +
            "Interno, Externo e Chamados; os três são mostrados para o total fechar."));
        story.Add(BarrasH(origens.Select(o => (o, Q(o))).ToList(), des, BL, BL, L, 90, cores: origens.Select(CorO).ToList()));
        story.Add(new Espaco(8));
        var linhasO = origens.Select(o => new object?[] { o, Fmt(Q(o)), Pct(Q(o) / desD * 100) }).ToList();
        linhasO.Add(new object?[] { "Total de desvios", Fmt(des), Pct(des > 0 ? 100 : 0) });
        story.Add(Tabela(new[] { "Origem", "Desvios", "% dos desvios" }, linhasO, new[] { L - 2 * 110, 110, 110 }, new[] { 0 }, EstiloTotal(linhasO.Count)));

        var aut = new List<Elemento>();
        aut.AddRange(Secao("Desvios por tipo de autorização", "Cada barra é dividida por origem do desvio (Interno / Externo / Chamados)."));
        aut.Add(Empilhadas(e.PorAut.Select(r => (r.Exibicao, r)).ToList()));
        aut.Add(new Espaco(6));
        var linhasA = e.PorAut.Select(r => new object?[] { r.Exibicao }.Concat(origens.Select(o => (object?)Fmt(r.O(o))))
            .Concat(new object?[] { Fmt(r.Total), Pct(r.Total / desD * 100) }).ToArray()).ToList();
        linhasA.Add(new object?[] { "Total" }.Concat(origens.Select(o => (object?)Fmt(Q(o)))).Concat(new object?[] { Fmt(des), Pct(des > 0 ? 100 : 0) }).ToArray());
        var wA = Math.Min(80.0, (L - 120) / (origens.Count + 2));
        var largA = new[] { L - wA * (origens.Count + 2) }.Concat(Enumerable.Repeat(wA, origens.Count + 2)).ToArray();
        aut.Add(Tabela(new[] { "Tipo de autorização" }.Concat(origens).Concat(new[] { "Total", "% dos desvios" }).ToArray(), linhasA, largA, new[] { 0 }, EstiloTotal(linhasA.Count)));
        story.Add(new Junto(aut));

        var ins = new List<Elemento>();
        ins.AddRange(Secao($"Desvios por inspetor — uma aba por inspetor ({e.PorInspetor.Count})",
            "Ordenado do inspetor com mais desvios para o com menos. “% desvios” = desvios ÷ avaliações da aba."));
        ins.Add(Empilhadas(e.PorInspetor.Select(r => (r.Nome, r)).ToList(), linha: 17));
        ins.Add(new Espaco(6));
        var linhasI = e.PorInspetor.Select(r => new object?[] { r.Nome, Fmt(r.Aval), Fmt(r.Desv), Pct(r.Desv / (double)(r.Aval == 0 ? 1 : r.Aval) * 100) }
            .Concat(origens.Select(o => (object?)Fmt(r.O(o)))).ToArray()).ToList();
        linhasI.Add(new object?[] { "Total", Fmt(tot), Fmt(des), Pct(des / (double)(tot == 0 ? 1 : tot) * 100) }.Concat(origens.Select(o => (object?)Fmt(Q(o)))).ToArray());
        var wI = Math.Min(62.0, (L - 110) / (3 + origens.Count));
        var largI = new[] { L - wI * (3 + origens.Count) }.Concat(Enumerable.Repeat(wI, 3 + origens.Count)).ToArray();
        ins.Add(Tabela(new[] { "Inspetor (aba)", "Avaliações", "Desvios", "% desvios" }.Concat(origens).ToArray(), linhasI, largI, new[] { 0 }, EstiloTotal(linhasI.Count)));
        story.Add(new Junto(ins));
        return doc.Gerar(story);
    }
}
