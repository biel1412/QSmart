using QSmart.Core.Planilhas;

namespace QSmart.Core;

public static class Classes
{
    public const string Aprov = "aprov", Reprov = "reprov", Fora = "fora";
    public static readonly HashSet<string> ProcAprovar = new() { "APROVAR" };
    public static readonly HashSet<string> ProcReprovar = new() { "REPROVAR", "DEVOLVER PARA CORREÇÃO", "DEVOLVER PARA CORRECAO" };
    public static readonly Dictionary<string, string> Rotulo = new()
    {
        [Aprov] = "Aprovação", [Reprov] = "Reprovação (devolutiva)", [Fora] = "Fora da comparação",
    };
    public static readonly string[] Ordem = { Aprov, Reprov, Fora };

    public static string Padrao(string proc)
    {
        var p = proc.Trim().ToUpperInvariant();
        if (ProcAprovar.Contains(p)) return Aprov;
        if (ProcReprovar.Contains(p)) return Reprov;
        return Fora;
    }

    /// <summary>Classe do procedimento: escolha do usuário (preferências) ou, se não houver, a regra padrão.</summary>
    public static string De(string proc, IReadOnlyDictionary<string, string>? classes)
        => classes != null && classes.TryGetValue(proc, out var c) && !string.IsNullOrEmpty(c) ? c : Padrao(proc);

    /// <summary>Nome curto do procedimento para textos de resumo.</summary>
    public static string RotuloProc(string p, bool curto = false)
    {
        if (curto)
            return p switch
            {
                "DEVOLVER PARA CORREÇÃO" or "DEVOLVER PARA CORRECAO" => "dev.",
                "REPROVAR" => "rep.",
                "CANCELAR FLUXO" => "canc.",
                _ => p.ToLowerInvariant().Length > 14 ? p.ToLowerInvariant()[..14] : p.ToLowerInvariant(),
            };
        return p switch
        {
            "DEVOLVER PARA CORREÇÃO" or "DEVOLVER PARA CORRECAO" => "devolvidas",
            "REPROVAR" => "reprovadas",
            "CANCELAR FLUXO" => "canceladas",
            _ => p.ToLowerInvariant(),
        };
    }
}

public sealed record CuboVolume(string Usuario, string Data, string Proc, string Seg, string Tipo, long N);

public sealed class OpcoesVolume
{
    public List<string> Usuarios { get; init; } = new();
    public List<string> Segmentos { get; init; } = new();
    public List<string> Dias { get; init; } = new();
    public List<(string proc, long n)> Procedimentos { get; init; } = new();
}

public sealed class FiltrosVolume
{
    public HashSet<string> Usuarios { get; set; } = new();
    public HashSet<string> Segmentos { get; set; } = new();
    public HashSet<string> Dias { get; set; } = new();
    public bool Algum => Usuarios.Count > 0 || Segmentos.Count > 0 || Dias.Count > 0;
    public HashSet<string> Get(string chave) => chave switch { "usuarios" => Usuarios, "segmentos" => Segmentos, _ => Dias };
    public void Set(string chave, HashSet<string> v)
    {
        if (chave == "usuarios") Usuarios = v; else if (chave == "segmentos") Segmentos = v; else Dias = v;
    }
}

public sealed record LinhaUsuario(string Usuario, long Acoes, long Aprovacoes, long Devolutivas, double Taxa, string Busca);
public sealed record LinhaSegmento(string Segmento, string Nome, long Dev, long Apr);
public sealed record LinhaProcedimento(string Procedimento, long Qtd, string Classe);

public sealed class ResultadoResumo
{
    public string Arquivo { get; init; } = "";
    public long Linhas { get; init; }
    public long LinhasTotal { get; init; }
    public int Usuarios { get; init; }
    public long Aprov { get; init; }
    public long Reprov { get; init; }
    public long Devolver { get; init; }
    public long Reprovar { get; init; }
    public List<(string proc, long n)> ReprovDetalhe { get; init; } = new();
    public long Outros { get; init; }
    public double PctAprov { get; init; }
    public double PctReprov { get; init; }
    public List<(string tipo, long qtd)> Tipos { get; init; } = new();
    public List<LinhaUsuario> Todos { get; init; } = new();
    public List<LinhaSegmento> Segmentos { get; init; } = new();
    public List<LinhaProcedimento> Procedimentos { get; init; } = new();
    public string RegrasTexto { get; init; } = "";
    public string FiltrosTexto { get; init; } = "";
}

public static class Volume
{
    public static readonly HashSet<string> ColunasUsadas = new()
        { "USUARIO", "PROCEDIMENTO", "JUSTIFICATIVA", "IDREGISTROCONTROLADO", "DATAHORAFIMATIVIDADE", "DATAHORAINICIOATIVIDADE" };

    public static bool UsarColuna(string c) => ColunasUsadas.Contains(c.Trim().ToUpperInvariant());

    public static string? ColunaData(IEnumerable<string> colunas)
    {
        var set = colunas.ToHashSet();
        return new[] { "DATAHORAFIMATIVIDADE", "DATAHORAINICIOATIVIDADE" }.FirstOrDefault(set.Contains);
    }

    /// <summary>Tabela "empilhada" (concat do pandas): colunas = união, na ordem de aparição.</summary>
    public sealed class Tabela
    {
        public List<string> Colunas = new();
        public List<string?[]> Linhas = new();
        public int Col(string nome) => Colunas.IndexOf(nome);
        public string?[] Coluna(string nome)
        {
            var c = Col(nome);
            var r = new string?[Linhas.Count];
            if (c < 0) return r;
            for (int i = 0; i < Linhas.Count; i++) r[i] = c < Linhas[i].Length ? Linhas[i][c] : null;
            return r;
        }
    }

    public static Tabela Empilhar(IEnumerable<Folha> folhas, Func<string, string>? nome = null)
    {
        nome ??= c => c.Trim().ToUpperInvariant();
        var t = new Tabela();
        foreach (var f in folhas)
        {
            var nomes = f.Colunas.Select(nome).ToList();
            foreach (var n in nomes) if (!t.Colunas.Contains(n)) t.Colunas.Add(n);
            var mapa = nomes.Select(n => t.Colunas.IndexOf(n)).ToArray();
            foreach (var l in f.Linhas)
            {
                var row = new string?[t.Colunas.Count];
                for (int j = 0; j < mapa.Length; j++) row[mapa[j]] = j < l.Length && l[j].Tem ? l[j].Texto : null;
                t.Linhas.Add(row);
            }
        }
        // normaliza tamanho das linhas
        for (int i = 0; i < t.Linhas.Count; i++)
            if (t.Linhas[i].Length < t.Colunas.Count)
            {
                var r = t.Linhas[i];
                Array.Resize(ref r, t.Colunas.Count);
                t.Linhas[i] = r;
            }
        return t;
    }

    /// <summary>ler_planilha: todas as abas com USUARIO e PROCEDIMENTO (só as colunas usadas), empilhadas.</summary>
    public static async Task<Tabela> LerPlanilha(Arquivo a, Progresso? prog = null)
    {
        var folhas = await Leitor.Ler(a, new OpcoesLeitura { Colunas = UsarColuna }, prog);
        List<Folha> validas;
        if (Leitor.EhCsv(a)) validas = folhas;
        else
        {
            validas = folhas.Where(f =>
            {
                var cols = f.Colunas.Select(c => c.Trim().ToUpperInvariant()).ToHashSet();
                return cols.Contains("USUARIO") && cols.Contains("PROCEDIMENTO");
            }).ToList();
            if (validas.Count == 0) throw new InvalidDataException("Nenhuma aba da planilha tem as colunas USUARIO e PROCEDIMENTO.");
        }
        var t = Empilhar(validas);
        foreach (var col in new[] { "USUARIO", "PROCEDIMENTO" })
            if (!t.Colunas.Contains(col)) throw new InvalidDataException($"A coluna '{col}' não foi encontrada na planilha.");
        return t;
    }

    public sealed class Derivado
    {
        public string[] Usuario = Array.Empty<string>(), Proc = Array.Empty<string>(), Seg = Array.Empty<string>(),
            Data = Array.Empty<string>(), Tipo = Array.Empty<string>();
        public string?[] Justificativa = Array.Empty<string?>();
    }

    static string[] Mapear(string?[] serie, Func<string?, string> f)
    {
        var cache = new Dictionary<string, string>(StringComparer.Ordinal);
        string? nulo = null;
        var r = new string[serie.Length];
        for (int i = 0; i < serie.Length; i++)
        {
            var v = serie[i];
            if (v == null) { r[i] = nulo ??= f(null); continue; }
            if (!cache.TryGetValue(v, out var x)) cache[v] = x = f(v);
            r[i] = x;
        }
        return r;
    }

    /// <summary>derivar_volume: regra única de leitura do volume (usuário, procedimento, segmento, data, motivo).</summary>
    public static Derivado Derivar(Tabela df)
    {
        var d = new Derivado();
        d.Proc = Mapear(df.Coluna("PROCEDIMENTO"), v => (v ?? "nan").Trim().ToUpperInvariant());
        d.Usuario = Mapear(df.Coluna("USUARIO"), v => (v ?? "(SEM USUARIO)").Trim().ToUpperInvariant());
        if (df.Colunas.Contains("IDREGISTROCONTROLADO"))
            d.Seg = Mapear(df.Coluna("IDREGISTROCONTROLADO"), v => (v ?? "nan").Split('#')[0].Trim().ToUpperInvariant());
        else d.Seg = Enumerable.Repeat("SEM SEGMENTO", df.Linhas.Count).ToArray();
        var colData = ColunaData(df.Colunas);
        d.Data = colData != null ? Datas.DatasDia(df.Coluna(colData)) : Enumerable.Repeat("", df.Linhas.Count).ToArray();
        d.Justificativa = df.Colunas.Contains("JUSTIFICATIVA") ? df.Coluna("JUSTIFICATIVA") : new string?[df.Linhas.Count];
        d.Tipo = Mapear(d.Justificativa, v => v == null ? "SEM JUSTIFICATIVA" : Texto.ExtrairTipo(v));
        return d;
    }

    /// <summary>carregar_volume: devolve o CUBO compacto (uma linha por combinação, com a contagem N).</summary>
    public static async Task<List<CuboVolume>> Carregar(IEnumerable<Arquivo> arquivos, Progresso? prog = null)
    {
        var tabs = new List<Tabela>();
        foreach (var a in arquivos) tabs.Add(await LerPlanilha(a, prog));
        Tabela df = tabs.Count == 1 ? tabs[0] : Concat(tabs);
        var d = Derivar(df);
        var cont = new Dictionary<(string, string, string, string, string), long>();
        for (int i = 0; i < df.Linhas.Count; i++)
        {
            var k = (d.Usuario[i], d.Data[i], d.Proc[i], d.Seg[i], d.Tipo[i]);
            cont[k] = cont.TryGetValue(k, out var n) ? n + 1 : 1;
        }
        return cont.OrderBy(kv => kv.Key.Item1, StringComparer.Ordinal).ThenBy(kv => kv.Key.Item2, StringComparer.Ordinal)
            .ThenBy(kv => kv.Key.Item3, StringComparer.Ordinal).ThenBy(kv => kv.Key.Item4, StringComparer.Ordinal)
            .ThenBy(kv => kv.Key.Item5, StringComparer.Ordinal)
            .Select(kv => new CuboVolume(kv.Key.Item1, kv.Key.Item2, kv.Key.Item3, kv.Key.Item4, kv.Key.Item5, kv.Value)).ToList();
    }

    public static Tabela Concat(List<Tabela> tabs)
    {
        var t = new Tabela();
        foreach (var x in tabs) foreach (var c in x.Colunas) if (!t.Colunas.Contains(c)) t.Colunas.Add(c);
        foreach (var x in tabs)
        {
            var mapa = x.Colunas.Select(c => t.Colunas.IndexOf(c)).ToArray();
            foreach (var l in x.Linhas)
            {
                var row = new string?[t.Colunas.Count];
                for (int j = 0; j < mapa.Length; j++) row[mapa[j]] = l[j];
                t.Linhas.Add(row);
            }
        }
        return t;
    }

    public static OpcoesVolume Opcoes(List<CuboVolume> cubo)
    {
        var procs = cubo.GroupBy(c => c.Proc).Select(g => (g.Key, g.Sum(x => x.N)))
            .OrderBy(x => x.Key, StringComparer.Ordinal).OrderByDescending(x => x.Item2).ToList();
        return new OpcoesVolume
        {
            Usuarios = cubo.Select(c => c.Usuario).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList(),
            Segmentos = cubo.Select(c => c.Seg).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList(),
            Dias = Texto.OrdenarDias(cubo.Select(c => c.Data).Distinct()),
            Procedimentos = procs,
        };
    }

    static List<CuboVolume> Filtrar(List<CuboVolume> cubo, FiltrosVolume f)
        => cubo.Where(c => (f.Usuarios.Count == 0 || f.Usuarios.Contains(c.Usuario)) &&
                           (f.Segmentos.Count == 0 || f.Segmentos.Contains(c.Seg)) &&
                           (f.Dias.Count == 0 || f.Dias.Contains(c.Data))).ToList();

    public static string TextoFiltros(IEnumerable<(string rotulo, IEnumerable<string> valores)> filtros)
    {
        var partes = new List<string>();
        foreach (var (rot, vals) in filtros)
        {
            var v = vals.OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (v.Count > 0) partes.Add($"{rot}: " + (v.Count <= 2 ? string.Join(", ", v) : $"{v.Count} selecionados"));
        }
        return string.Join("  ·  ", partes);
    }

    /// <summary>calcular_resumo: todos os números do Resumo a partir do cubo + classificação + filtros.</summary>
    public static ResultadoResumo CalcularResumo(List<CuboVolume> cubo, IReadOnlyDictionary<string, string> classes, FiltrosVolume filtros, string arquivo = "")
    {
        var sub = Filtrar(cubo, filtros);
        var classeDe = sub.Select(c => c.Proc).Distinct().ToDictionary(p => p, p => Classes.De(p, classes));
        var ap = sub.Where(c => classeDe[c.Proc] == Classes.Aprov).ToList();
        var rp = sub.Where(c => classeDe[c.Proc] == Classes.Reprov).ToList();
        long nAprov = ap.Sum(c => c.N), nReprov = rp.Sum(c => c.N), total = nAprov + nReprov;
        long linhas = sub.Sum(c => c.N);

        var procs = sub.GroupBy(c => (c.Proc, cls: classeDe[c.Proc])).Select(g => (g.Key.Proc, g.Key.cls, n: g.Sum(x => x.N)))
            .OrderBy(x => x.Proc, StringComparer.Ordinal).ThenBy(x => x.cls, StringComparer.Ordinal)
            .OrderByDescending(x => x.n)
            .Select(x => new LinhaProcedimento(x.Proc, x.n, Classes.Rotulo[x.cls])).ToList();

        var tipos = new List<(string, long)>();
        if (rp.Count > 0)
        {
            var g = rp.GroupBy(c => (chave: Texto.SemAcento(c.Tipo), c.Tipo)).Select(x => (x.Key.chave, x.Key.Tipo, n: x.Sum(y => y.N))).ToList();
            var nome = g.GroupBy(x => x.chave).ToDictionary(x => x.Key,
                x => x.OrderByDescending(y => y.n).ThenBy(y => y.Tipo, StringComparer.Ordinal).First().Tipo);
            tipos = g.GroupBy(x => x.chave).Select(x => (chave: x.Key, n: x.Sum(y => y.n)))
                .OrderByDescending(x => x.n).ThenBy(x => x.chave, StringComparer.Ordinal)
                .Select(x => (nome[x.chave], x.n)).ToList();
        }

        var usuarios = sub.Select(c => c.Usuario).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        var acoes = sub.GroupBy(c => c.Usuario).ToDictionary(g => g.Key, g => g.Sum(x => x.N));
        var aprovU = ap.GroupBy(c => c.Usuario).ToDictionary(g => g.Key, g => g.Sum(x => x.N));
        var devU = rp.GroupBy(c => c.Usuario).ToDictionary(g => g.Key, g => g.Sum(x => x.N));
        var porUsuario = usuarios.Select(u =>
        {
            long a = aprovU.GetValueOrDefault(u), d = devU.GetValueOrDefault(u);
            return new LinhaUsuario(u, acoes.GetValueOrDefault(u), a, d, a + d > 0 ? d / (double)(a + d) : 0.0, Texto.NormalizarBusca(u));
        }).ToList();

        var segDev = rp.GroupBy(c => c.Seg).ToDictionary(g => g.Key, g => g.Sum(x => x.N));
        var segApr = ap.GroupBy(c => c.Seg).ToDictionary(g => g.Key, g => g.Sum(x => x.N));
        var segmentos = segDev.Keys.Union(segApr.Keys).OrderBy(x => x, StringComparer.Ordinal)
            .Select(s => new LinhaSegmento(s, Texto.NomeSegmento(s), segDev.GetValueOrDefault(s), segApr.GetValueOrDefault(s)))
            .OrderByDescending(x => x.Dev).ThenByDescending(x => x.Apr).ToList();

        var reprovDet = rp.GroupBy(c => c.Proc).Select(g => (g.Key, g.Sum(x => x.N)))
            .OrderBy(x => x.Key, StringComparer.Ordinal).OrderByDescending(x => x.Item2).ToList();
        long reprovar = rp.Where(c => c.Proc == "REPROVAR").Sum(c => c.N);

        var todas = cubo.GroupBy(c => c.Proc).Select(g => (g.Key, n: g.Sum(x => x.N)))
            .OrderBy(x => x.Key, StringComparer.Ordinal).OrderByDescending(x => x.n).Select(x => x.Key).ToList();
        var porCls = Classes.Ordem.ToDictionary(c => c, c => todas.Where(p => Classes.De(p, classes) == c).ToList());
        var regras = new List<string>();
        if (porCls[Classes.Aprov].Count > 0) regras.Add("Aprovação = " + string.Join(" + ", porCls[Classes.Aprov]));
        if (porCls[Classes.Reprov].Count > 0) regras.Add("Reprovação (devolutiva) = " + string.Join(" + ", porCls[Classes.Reprov]));
        if (porCls[Classes.Fora].Count > 0) regras.Add("Fora da comparação: " + string.Join(", ", porCls[Classes.Fora]));

        return new ResultadoResumo
        {
            Arquivo = arquivo,
            Linhas = linhas,
            LinhasTotal = cubo.Sum(c => c.N),
            Usuarios = usuarios.Count,
            Aprov = nAprov,
            Reprov = nReprov,
            Devolver = nReprov - reprovar,
            Reprovar = reprovar,
            ReprovDetalhe = reprovDet,
            Outros = linhas - nAprov - nReprov,
            PctAprov = total > 0 ? nAprov / (double)total * 100 : 0,
            PctReprov = total > 0 ? nReprov / (double)total * 100 : 0,
            Tipos = tipos,
            Todos = porUsuario,
            Segmentos = segmentos,
            Procedimentos = procs,
            RegrasTexto = string.Join(".  ", regras) + ".",
            FiltrosTexto = TextoFiltros(new (string, IEnumerable<string>)[]
                { ("Usuários", filtros.Usuarios), ("Segmentos", filtros.Segmentos), ("Dias", filtros.Dias) }),
        };
    }
}
