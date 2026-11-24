using QSmart.Core.Admin;

namespace QSmart.Core.Pdf;

public static partial class Relatorios
{
    static string PctUso(double x) => (x > 0 && x < 0.01 ? "<0,01" : Texto.Fv(x, x < 10 ? 2 : 1)) + "%";
    static Cor CorNivel(NivelUso n) => n switch { NivelUso.Critico => RD, NivelUso.Atencao => AMB, NivelUso.Info => BL, _ => GRN };

    /// <summary>Barras de consumo: nome, barra com a parte usada e o texto "usado de limite".</summary>
    static Desenho Medidores(List<MedidorUso> ms, double L)
    {
        const double linha = 27;
        var d = new Desenho(L, linha * ms.Count + 4);
        double rot = 190, area = L - rot - 150;
        for (int i = 0; i < ms.Count; i++)
        {
            var m = ms[i];
            var y = d.AlturaFixa - (i + 1) * linha + 6;
            d.Txt(0, y + 9, Fontes.Cortar(m.Nome, rot - 8, Fonte.Negrito, 8.5), Fonte.Negrito, 8.5, INK);
            d.Txt(0, y, $"{m.Grupo} · {m.Periodo}", Fonte.Normal, 7, MUT);
            d.Ret(rot, y + 2, area, 9, ZEBRA);
            var cor = CorNivel(m.Nivel);
            d.Ret(rot, y + 2, Math.Max(1.5, area * Math.Min(1, m.Pct / 100)), 9, cor);
            if (m.Previsto is double pv && m.Limite > 0 && pv > m.Usado)
            {
                var xp = rot + area * Math.Min(1, pv / m.Limite);
                d.Lin(xp, y, xp, y + 13, MUT, 0.8);
            }
            d.Txt(rot + area + 8, y + 9, $"{m.UsadoTxt} de {m.LimiteTxt}", Fonte.Negrito, 8, INK);
            d.Txt(rot + area + 8, y, $"{PctUso(m.Pct)} · resta {m.Resta}", Fonte.Normal, 7, cor);
        }
        return d;
    }

    /// <summary>Colunas simples por dia (histórico).</summary>
    static Desenho Colunas(List<(string rot, long v)> itens, double L, Cor cor, string titulo, double h = 110)
    {
        var d = new Desenho(L, h);
        d.Txt(0, h - 9, titulo, Fonte.Negrito, 8.5, INK);
        if (itens.Count == 0) { d.Txt(0, h / 2, "Sem dados medidos ainda.", Fonte.Normal, 8, MUT); return d; }
        double topo = h - 22, baseY = 16, larg = L / Math.Max(itens.Count, 1);
        var max = Math.Max(1, itens.Max(x => x.v));
        for (int i = 0; i < itens.Count; i++)
        {
            var (r, v) = itens[i];
            var alt = (topo - baseY) * v / max;
            d.Ret(i * larg + larg * 0.15, baseY, larg * 0.7, Math.Max(alt, 0.8), cor);
            if (itens.Count <= 16 || i % 3 == 0) d.Txt(i * larg + larg / 2, baseY - 10, r, Fonte.Normal, 6, MUT, 'c');
            if (v == max) d.Txt(i * larg + larg / 2, baseY + alt + 3, Texto.Fmt(v), Fonte.Negrito, 6.5, INK, 'c');
        }
        d.Lin(0, baseY, L, baseY, GRID, 0.6);
        return d;
    }

    /// <summary>Relatório de uso e armazenamento (painel do administrador).</summary>
    public static byte[] Uso(UsoSistema u, string? navegador = null)
    {
        var brt = u.GeradoEm.AddHours(-3);
        var doc = new Documento { Titulo = "Uso e armazenamento — QSmart", Rodape = $"QSmart · Uso e armazenamento — {brt:dd/MM/yyyy HH:mm}" };
        double L = doc.LarguraUtil;
        var avisos = u.Avisos();
        var ms = u.Medidores();
        var story = new List<Elemento> { Marca(L) };
        story.Add(new Paragrafo("Uso e armazenamento", StTitulo));
        story.Add(new Paragrafo($"Gerado em <b>{brt:dd/MM/yyyy HH:mm}</b> (Brasília) &nbsp;•&nbsp; {Esc(u.Lim.Plano)} &nbsp;•&nbsp; " +
                                $"limites diários zeram às 21h de Brasília", StSub));
        story.Add(new Espaco(10));

        int crit = avisos.Count(a => a.Nivel == NivelUso.Critico), aten = avisos.Count(a => a.Nivel == NivelUso.Atencao);
        var geral = u.NivelGeral;
        story.Add(TabelaKpis(new List<List<Elemento>>
        {
            Kpi("SITUAÇÃO GERAL", UsoSistema.NomeNivel(geral), CorNivel(geral), crit + aten == 0 ? "nenhum ponto de atenção" : $"{crit} crítico(s) · {aten} de atenção", 15),
            Kpi("BANCO DE DADOS", UsoSistema.Tam(u.BancoBytes), BL, $"{PctUso(ms[0].Pct)} de {UsoSistema.Tam(u.Lim.BancoBytes)} · {Fmt(u.LinhasBanco)} linhas", 15),
            Kpi("ARQUIVOS (R2)", u.R2 ? UsoSistema.Tam(u.R2Bytes) : "Desligado", u.R2 ? PUR : AMB, u.R2 ? $"{Fmt(u.R2Objetos)} arquivo(s) · {PctUso(ms[1].Pct)} de 10 GB" : "ligue o bucket ARQUIVOS", 15),
            Kpi("PEDIDOS HOJE", Fmt(u.Hoje["req"]), ms[2].Nivel == NivelUso.Ok ? GRN : CorNivel(ms[2].Nivel), $"{PctUso(ms[2].Pct)} do limite diário · {Fmt(u.AtivosHoje)} pessoa(s) ativa(s)", 15),
        }, L, new[] { CorNivel(geral), BL, u.R2 ? PUR : AMB, ms[2].Nivel == NivelUso.Ok ? GRN : CorNivel(ms[2].Nivel) }, 8));

        story.AddRange(Secao("Avisos de atenção", "Ordenados do mais grave para o menos grave. Crítico = 90% ou mais do limite (ou estouro previsto em até 30 dias); Atenção = 70% ou mais."));
        var linhasA = avisos.Select(a => new object?[]
        {
            UsoSistema.NomeNivel(a.Nivel),
            new List<Elemento> { new Paragrafo($"<b>{Esc(a.Titulo)}</b>", new Estilo { Tamanho = 8.5, Entrelinha = 11, Cor = INK }),
                                 new Paragrafo(Esc(a.Texto), new Estilo { Tamanho = 7.5, Entrelinha = 10, Cor = MUT }) },
            new List<Elemento> { new Paragrafo(Esc(a.Acao), new Estilo { Tamanho = 7.5, Entrelinha = 10, Cor = INK }) },
        }).ToList();
        var destA = avisos.Select((a, i) => new EstiloTab("TEXTCOLOR", (0, i + 1), (0, i + 1), CorNivel(a.Nivel))).ToList();
        destA.Add(new EstiloTab("FONTNAME", (0, 1), (0, -1), Fonte.Negrito));
        destA.Add(new EstiloTab("VALIGN", (0, 0), (-1, -1), 't'));
        story.Add(Tabela(new[] { "Nível", "Aviso", "O que fazer" }, linhasA, new[] { 62, (L - 62) * 0.55, (L - 62) * 0.45 }, new[] { 1, 2 }, destA));

        story.AddRange(Secao("Consumo comparado aos limites do plano",
            "Barra = quanto já foi usado. O traço cinza mostra a previsão para o fim do dia, no ritmo atual. Os números diários são medidos pelo próprio QSmart desde " +
            $"{u.MedindoDesde:dd/MM/yyyy}; o número oficial fica no painel da Cloudflare."));
        story.Add(Medidores(ms, L));

        story.AddRange(Secao("Banco de dados por módulo", $"Tamanho real do banco: <b>{UsoSistema.Tam(u.BancoBytes)}</b> (dados + {u.Indices} índices + estrutura). " +
            "Abaixo, o tamanho dos dados guardados em cada parte do QSmart, <b>em KB</b> (textos criptografados contam pelo tamanho cifrado)."));
        var mods = u.Modulos().Where(m => m.bytes > 0).ToList();
        story.Add(BarrasH(mods.Select(m => (m.modulo, Math.Max(1, (m.bytes + 512) / 1024))).ToList(), Math.Max(1, mods.Sum(m => Math.Max(1, (m.bytes + 512) / 1024))), BL, Cor.Hex("#7DD3FC"), L, 140));
        story.Add(new Espaco(6));
        long somaT = Math.Max(1, u.Tabelas.Sum(t => t.Bytes));
        story.Add(Tabela(new[] { "Tabela", "Módulo", "Linhas", "Tamanho", "% dos dados" },
            u.Tabelas.Where(t => t.Linhas > 0).Select(t => new object?[] { t.Nome, t.Modulo, Fmt(t.Linhas), UsoSistema.Tam(t.Bytes), Pct(t.Bytes * 100.0 / somaT) }).ToList(),
            new[] { L * 0.3, L * 0.28, L * 0.12, L * 0.15, L * 0.15 }, new[] { 0, 1 }));

        story.Add(new QuebraPagina());
        story.AddRange(Secao("Arquivos (R2)", u.R2 ? $"<b>{Fmt(u.R2Objetos)}</b> arquivo(s), <b>{UsoSistema.Tam(u.R2Bytes)}</b> de {UsoSistema.Tam(u.Lim.R2Bytes)} grátis. " +
            $"Neste mês: {Fmt(u.Mes("r2a"))} envios/listagens e {Fmt(u.Mes("r2b"))} downloads." : "O armazenamento de arquivos (R2) não está ligado ao servidor."));
        if (u.Maiores.Count > 0)
            story.Add(Tabela(new[] { "Arquivo", "Download", "Tamanho", "Enviado em" },
                u.Maiores.Select(a => new object?[] { a.Nome, Fontes.Cortar(a.Titulo, L * 0.4 - 10), UsoSistema.Tam(a.Bytes), UsoSistema.Data(a.Em) }).ToList(),
                new[] { L * 0.22, L * 0.42, L * 0.14, L * 0.22 }, new[] { 0, 1 }));

        var hist = u.Historico.OrderBy(d => d.Dia).TakeLast(30).ToList();
        story.AddRange(Secao("Últimos 30 dias", "Dia contado em UTC (o mesmo da Cloudflare). Dias sem uso não aparecem."));
        story.Add(Colunas(hist.Select(d => (d.Dia.ToString("dd/MM"), d["req"])).ToList(), L, GRN, "Pedidos ao servidor por dia"));
        story.Add(new Espaco(6));
        story.Add(Colunas(hist.Select(d => (d.Dia.ToString("dd/MM"), d["rd"])).ToList(), L, BL, "Linhas lidas do banco por dia", 100));
        story.Add(new Espaco(6));
        story.Add(Tabela(new[] { "Dia", "Pedidos", "Lidas", "Gravadas", "IA", "Tokens IA", "Erros", "Banco" },
            hist.OrderByDescending(d => d.Dia).Select(d => new object?[] { d.Dia.ToString("dd/MM/yyyy"), Fmt(d["req"]), Fmt(d["rd"]), Fmt(d["wr"]), Fmt(d["ia"]), Fmt(d["iatok"]), Fmt(d["err"]),
                d["banco_bytes"] > 0 ? UsoSistema.Tam(d["banco_bytes"]) : "—" }).ToList(),
            new[] { L * 0.16, L * 0.12, L * 0.12, L * 0.12, L * 0.1, L * 0.14, L * 0.1, L * 0.14 }, new[] { 0 }));

        var rotas = u.Rotas();
        if (rotas.Count > 0)
        {
            story.AddRange(Secao("Pedidos de hoje por área", "Onde os pedidos ao servidor estão sendo gastos hoje."));
            story.Add(BarrasH(rotas.Take(12).Select(r => (r.rota, r.n)).ToList(), Math.Max(1, rotas.Sum(r => r.n)), GRN, Cor.Hex("#86EFAC"), L, 140));
        }

        story.AddRange(Secao("Pessoas e ferramentas"));
        story.Add(Tabela(new[] { "Contas", "Aguardando", "Ativas hoje", "Ativas 7 dias", "Ativas 30 dias", "Sessões abertas" },
            new List<object?[]> { new object?[] { Fmt(u.UsuariosTotal), Fmt(u.Pendentes), Fmt(u.AtivosHoje), Fmt(u.Ativos7), Fmt(u.Ativos30), Fmt(u.Sessoes) } },
            Enumerable.Repeat(L / 6, 6).ToArray(), Array.Empty<int>()));
        story.Add(new Espaco(8));
        if (u.Ferramentas.Count > 0)
            story.Add(Tabela(new[] { "Ferramenta / ação", "Usos", "Pessoas", "Último uso" },
                u.Ferramentas.Take(15).Select(f => new object?[] { UsoSistema.NomeFerramenta(f.Chave), Fmt(f.Usos), Fmt(f.Pessoas), UsoSistema.Data(f.Ultimo) }).ToList(),
                new[] { L * 0.46, L * 0.14, L * 0.14, L * 0.26 }, new[] { 0 }));
        if (navegador != null) { story.Add(new Espaco(8)); story.Add(new Paragrafo($"<b>Este navegador:</b> {Esc(navegador)}", StNota)); }

        story.Add(new Espaco(10));
        story.Add(new Paragrafo("Como os números são obtidos: o servidor do QSmart soma, a cada pedido, as linhas que o banco informa ter lido e gravado, " +
            "as chamadas à IA (e o limite que a própria Groq devolve) e as operações com arquivos, e grava esse total por dia. É uma estimativa muito próxima, " +
            "mas o valor oficial para cobrança é o do painel da Cloudflare (Workers & Pages › Métricas e D1 › Métricas).", StNota));
        return doc.Gerar(story);
    }
}
