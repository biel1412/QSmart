using System.Globalization;
using System.Text.RegularExpressions;
using QSmart.Core.Planilhas;

namespace QSmart.Core;

public sealed class ColDep
{
    public string Original = "", Normalizada = "", Status = "", Papel = "", Exemplo = "";
    public int Preenchidas, Vazias;
}

public sealed class Inconsistencia
{
    public string Aba = "", Celulas = "", Colaborador = "", Tipo = "", Desvio = "", Gravidade = "", Programa = "", Categoria = "", Problema = "";
    public int Linha;
    public List<int> Cats = new();
}

public sealed class AbaDep
{
    public string Nome = "", Status = "lida", Motivo = "";
    public Folha Dados = new();
    /// <summary>Número da linha no Excel (cabeçalho = linha 1).</summary>
    public int[] Linhas = Array.Empty<int>();
    public string[] Estado = Array.Empty<string>();
    public string[] MotivoLinha = Array.Empty<string>();
    public List<(string nome, string?[] valores)> Derivadas = new();
    public Dictionary<string, string?[]> Celulas = new();
    public List<ColDep> Colunas = new();
    public int N;
    public Dictionary<string, int> Contagem = new();
    public (string? desvio, string? gravidade)? IncCols;
    public List<Inconsistencia> Inconsistencias = new();
}

public sealed record ColunaValor(string Titulo, int Largura, string Alinhamento);
public sealed class TabelaValores
{
    public string Titulo = "", Nota = "";
    public List<ColunaValor> Colunas = new();
    public List<object[]> Linhas = new();
}

public sealed class Bruto
{
    public string Arquivo = "", Info = "";
    public bool Csv, Truncado;
    public int? Limite;
    public List<Folha> Folhas = new();
}

public sealed class ResultadoDep
{
    public string Arquivo = "", Info = "", Modo = "", ModoPedido = "", Detectado = "";
    public List<AbaDep> Abas = new();
    public List<TabelaValores> Valores = new();
    public List<(string nivel, string aba, string texto)> Alertas = new();
    public Dictionary<string, int> Total = new();
    public int Linhas, AbasLidas, ColunasUsadas, ColunasTotal;
    public List<Inconsistencia> Inconsistencias = new();
    public Dictionary<string, (string? desvio, string? gravidade)> IncCols = new();
    public List<string> Candidatas = new();
}

public sealed class CtxDep
{
    public Dictionary<string, string> Classes = new();
    public List<string> Padroes = new(), Ignorar = new(), ProcExcluidos = new();
    public bool Acentos = true;
    /// <summary>null = automático, "" = nenhuma, nome = coluna escolhida.</summary>
    public string? IncDesvio, IncGrav;

    public string Assinatura() => string.Join("|", Classes.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value)) + "#" +
        string.Join("|", Padroes) + "#" + string.Join("|", Ignorar) + "#" + string.Join("|", ProcExcluidos) + "#" + Acentos + "#" + IncDesvio + "#" + IncGrav;
}

/// <summary>Depurador de leitura: mostra, linha a linha e coluna a coluna, o que cada leitor aproveita e o que descarta.</summary>
public static class Depurador
{
    public static readonly (string chave, string nome)[] Modos = { ("auto", "Automático"), ("volume", "Volume"), ("inspecao", "Inspeção"), ("pareceres", "Pareceres") };
    public static string NomeModo(string m) => Modos.First(x => x.chave == m).nome;
    public static readonly Dictionary<string, string> OndeModo = new() { ["volume"] = "Resumo e Usuários", ["inspecao"] = "Evolução", ["pareceres"] = "Pareceres" };
    public static readonly (string chave, string nome)[] StatusDep = { ("lida", "Lida"), ("fora", "Fora da comparação"), ("alerta", "Lida com alerta"), ("descartada", "Descartada") };
    public static string NomeStatus(string k) => StatusDep.First(x => x.chave == k).nome;
    public static readonly Dictionary<string, string> StatusCol = new()
    {
        ["obrigatoria"] = "Obrigatória", ["usada"] = "Usada", ["sem_uso"] = "Lida, sem uso", ["conferencia"] = "Conferida",
        ["ignorada"] = "Ignorada", ["faltando"] = "Não encontrada",
    };
    static readonly Dictionary<string, string> PapelVolume = new()
    {
        ["USUARIO"] = "Quem executou a atividade (vazio vira “(SEM USUARIO)”)",
        ["PROCEDIMENTO"] = "Define aprovação, reprovação ou fora da comparação",
        ["JUSTIFICATIVA"] = "O texto antes do “:” vira o motivo da reprovação",
        ["IDREGISTROCONTROLADO"] = "O texto antes do “#” vira o segmento",
        ["DATAHORAFIMATIVIDADE"] = "Data do registro (filtro por dia)",
        ["DATAHORAINICIOATIVIDADE"] = "Data do registro (usada só se não houver DATAHORAFIMATIVIDADE)",
    };
    static readonly Dictionary<string, string> PapelInspecao = new()
    {
        ["INSPETOR"] = "Obrigatória para reconhecer a aba de inspetor (a contagem usa o nome da aba)",
        ["INTERNO OU EXTERNO"] = "Origem do desvio (Interno / Externo / Chamados)",
        ["TIPO DE AUTORIZACAO"] = "Tipo de autorização",
        ["TIPO DE ERRO"] = "Vazio = linha não avaliada; diferente de “Sem erro” = desvio",
        ["COLABORADOR"] = "Pessoa avaliada (desvios por colaborador)",
        ["COOP"] = "Cooperativa (texto antes do “_”)",
        ["DATA DA INSPECAO"] = "Período da planilha e mês sugerido para gravar no histórico",
    };
    static readonly Dictionary<string, string> PapelPareceres = new()
    {
        ["USUARIO"] = "Quem escreveu o parecer",
        ["JUSTIFICATIVA"] = "O parecer analisado (tipo = texto antes do “:”)",
        ["PROCEDIMENTO"] = "Permite tirar procedimentos da análise",
        ["DATAHORAFIMATIVIDADE"] = "Data (filtro por dia)",
        ["DATAHORAINICIOATIVIDADE"] = "Data (usada só se não houver DATAHORAFIMATIVIDADE)",
        ["NOMEPROCESSO"] = "Processo (aparece na exportação)",
    };
    static readonly Regex ReIso = new(@"^\s*(\d{4})-(\d{2})-(\d{2})");
    static readonly Regex ReDma = new(@"^\s*(\d{1,2})/(\d{1,2})/(\d{4})");
    public static readonly string[] CategoriasInc = { "Sem erro × desvio Sim", "Erro × desvio Não", "Sem erro × gravidade", "Erro × grav. sem erro",
        "Desvio × gravidade", "Desvio sem tipo de erro", "Campo vazio", "Valor inválido" };
    public static readonly (int? limite, string nome)[] Limites = { (null, "Todas as linhas"), (1000, "Primeiras 1.000"), (10000, "Primeiras 10.000"), (100000, "Primeiras 100.000") };

    // ---------------- leitura ----------------
    public static async Task<Bruto> LerBruto(Arquivo a, int? limite, Progresso? prog = null)
    {
        var br = new Bruto { Arquivo = a.Nome, Limite = limite };
        if (Leitor.EhCsv(a))
        {
            var r = await LeitorCsv.Ler(a.Dados, new OpcoesLeitura { Limite = limite }, prog);
            br.Folhas = new List<Folha> { r.Folha };
            br.Csv = true;
            br.Info = $"CSV, codificação {r.Codificacao}, separador “{(r.Separador == '\t' ? "\\t" : r.Separador.ToString())}”";
        }
        else
        {
            br.Folhas = await LeitorXlsx.Ler(a.Dados, new OpcoesLeitura { Limite = limite }, prog);
            br.Info = "Excel, leitor QSmart";
        }
        br.Truncado = limite.HasValue && br.Folhas.Any(f => f.N >= limite.Value);
        return br;
    }

    public static string DetectarModo(Bruto br)
    {
        foreach (var f in br.Folhas)
            if (Inspecao.NecessariasEvo.IsSubsetOf(f.Colunas.Select(Inspecao.NormCabecalho))) return "inspecao";
        foreach (var f in br.Folhas)
        {
            var c = f.Colunas.Select(x => x.Trim().ToUpperInvariant()).ToHashSet();
            if (c.Contains("USUARIO") && c.Contains("PROCEDIMENTO")) return "volume";
        }
        var p = br.Folhas.FirstOrDefault();
        if (p != null)
        {
            var c = p.Colunas.Select(x => x.Trim().ToUpperInvariant()).ToHashSet();
            if (c.Contains("USUARIO") && c.Contains("JUSTIFICATIVA")) return "pareceres";
        }
        return "volume";
    }

    static string Parecida(string falta, IEnumerable<string> colunas)
    {
        string Chave(string x) => Regex.Replace(Texto.SemAcento(x), @"[\s_]+", "").ToUpperInvariant();
        var lista = colunas.ToList();
        foreach (var c in lista) if (Chave(c) == Chave(falta)) return c;
        var norm = new Dictionary<string, string>();
        foreach (var c in lista) norm[Texto.SemAcento(c).Trim().ToUpperInvariant()] = c;
        var perto = Difflib.CloseMatches(Texto.SemAcento(falta).ToUpperInvariant(), norm.Keys, 1, 0.8);
        return perto.Count > 0 ? norm[perto[0]] : "";
    }

    static string TextoFalta(IEnumerable<string> faltas, IEnumerable<string> colunas)
        => string.Join(", ", faltas.Select(f => { var p = Parecida(f, colunas); return p != "" ? $"{f} (existe “{p}”, mas o nome não bate)" : f; }));

    static ColDep Col(string original, string norm, string status, string papel, string?[]? serie = null)
    {
        var c = new ColDep { Original = original, Normalizada = norm, Status = status, Papel = papel };
        if (serie != null)
        {
            c.Preenchidas = serie.Count(v => v != null);
            c.Vazias = serie.Length - c.Preenchidas;
            var ex = serie.FirstOrDefault(v => v != null);
            c.Exemplo = ex == null ? "" : (ex.Length > 80 ? ex[..80] : ex);
        }
        return c;
    }

    static AbaDep NovaAba(Folha f)
    {
        var n = f.N;
        return new AbaDep
        {
            Nome = f.Nome, Dados = f, N = n, Linhas = Enumerable.Range(2, n).ToArray(),
            Estado = Enumerable.Repeat("lida", n).ToArray(), MotivoLinha = Enumerable.Repeat("", n).ToArray(),
        };
    }

    static readonly string[] SobreporPadrao = { "lida", "fora", "alerta" };

    /// <summary>_marcar: marca as linhas da máscara (status, motivo e célula com problema).</summary>
    static void Marcar(AbaDep a, bool[] mascara, string status, Func<int, string> texto, string? coluna = null, string[]? sobrepor = null)
    {
        sobrepor ??= SobreporPadrao;
        if (!mascara.Any(x => x)) return;
        string?[]? cel = null;
        if (coluna != null && !a.Celulas.TryGetValue(coluna, out cel)) a.Celulas[coluna] = cel = new string?[a.N];
        for (int i = 0; i < a.N; i++)
        {
            if (!mascara[i]) continue;
            if (sobrepor.Contains(a.Estado[i])) a.Estado[i] = status;
            var t = texto(i);
            a.MotivoLinha[i] = a.MotivoLinha[i] == "" ? t : a.MotivoLinha[i] + "  ·  " + t;
            if (cel != null) cel[i] = cel[i] == null ? t : cel[i] + "  ·  " + t;
        }
    }

    static void Marcar(AbaDep a, bool[] m, string status, string texto, string? coluna = null, string[]? sobrepor = null)
        => Marcar(a, m, status, _ => texto, coluna, sobrepor);

    static void AnotarCelula(AbaDep a, bool[] m, string coluna, Func<int, string> texto)
    {
        if (!a.Celulas.TryGetValue(coluna, out var cel)) a.Celulas[coluna] = cel = new string?[a.N];
        for (int i = 0; i < a.N; i++)
            if (m[i]) { var t = texto(i); cel[i] = cel[i] == null ? t : cel[i] + "  ·  " + t; }
    }

    static void IgnorarAba(AbaDep a, string motivo)
    {
        a.Status = "ignorada";
        a.Motivo = motivo;
        Array.Fill(a.Estado, "descartada");
        Array.Fill(a.MotivoLinha, "Aba ignorada: " + motivo);
    }

    static Dictionary<string, int> Contagem(AbaDep a) => StatusDep.ToDictionary(s => s.chave, s => a.Estado.Count(e => e == s.chave));

    static TabelaValores Tab(string titulo, IEnumerable<(string, int, string)> cols, IEnumerable<object[]> linhas, string nota = "")
        => new() { Titulo = titulo, Colunas = cols.Select(c => new ColunaValor(c.Item1, c.Item2, c.Item3)).ToList(), Linhas = linhas.ToList(), Nota = nota };

    /// <summary>Alerta datas não reconhecidas e com dia e mês trocados. lidaDma = data lida em dd/mm/aaaa ("" = falhou).</summary>
    static int ChecarDatas(AbaDep a, string?[] bruto, string?[] lidaDma, string? coluna)
    {
        int n = a.N;
        var tem = new bool[n];
        for (int i = 0; i < n; i++) tem[i] = bruto[i] != null && bruto[i]!.Trim() != "";
        Marcar(a, Enumerable.Range(0, n).Select(i => tem[i] && lidaDma[i] == "").ToArray(), "alerta", "data não reconhecida (fica sem data)", coluna);
        Marcar(a, tem.Select(x => !x).ToArray(), "alerta", "data vazia", coluna);
        var esperado = new string?[n];
        for (int i = 0; i < n; i++)
        {
            var b = bruto[i] ?? "";
            var m = ReIso.Match(b);
            if (m.Success) { esperado[i] = $"{m.Groups[3].Value}/{m.Groups[2].Value}/{m.Groups[1].Value}"; continue; }
            m = ReDma.Match(b);
            if (m.Success) esperado[i] = $"{m.Groups[1].Value.PadLeft(2, '0')}/{m.Groups[2].Value.PadLeft(2, '0')}/{m.Groups[3].Value}";
        }
        var troca = Enumerable.Range(0, n).Select(i => esperado[i] != null && lidaDma[i] != "" && lidaDma[i] != null && lidaDma[i] != esperado[i]).ToArray();
        if (troca.Any(x => x))
            Marcar(a, troca, "alerta", i => $"data lida como {lidaDma[i]} (esperado {esperado[i]}): dia e mês trocados", coluna);
        return troca.Count(x => x);
    }

    static string?[] ColunaTexto(Folha f, string? nomeOriginal)
    {
        if (nomeOriginal == null) return new string?[f.N];
        return f.ColunaTexto(f.Colunas.IndexOf(nomeOriginal));
    }

    // ---------------- modo Volume ----------------
    static (List<AbaDep>, List<TabelaValores>) DepVolume(Bruto br, CtxDep ctx, List<(string, string, string)> alertas)
    {
        var abas = new List<AbaDep>();
        var validas = new List<AbaDep>();
        foreach (var f in br.Folhas)
        {
            var aba = NovaAba(f);
            var presentes = new Dictionary<string, string>();
            foreach (var c in f.Colunas) presentes.TryAdd(c.Trim().ToUpperInvariant(), c);
            var dataUsada = Volume.ColunaData(presentes.Keys);
            foreach (var c in f.Colunas)
            {
                var n = c.Trim().ToUpperInvariant();
                string st, papel;
                if (Volume.ColunasUsadas.Contains(n))
                {
                    st = n is "USUARIO" or "PROCEDIMENTO" ? "obrigatoria" : "usada";
                    papel = PapelVolume[n];
                    if (n is "DATAHORAFIMATIVIDADE" or "DATAHORAINICIOATIVIDADE" && n != dataUsada)
                        (st, papel) = ("sem_uso", "Lida, mas a data vem de DATAHORAFIMATIVIDADE");
                }
                else (st, papel) = ("ignorada", "Não é lida pelo volume");
                aba.Colunas.Add(Col(c, n, st, papel, f.ColunaTexto(f.Colunas.IndexOf(c))));
            }
            var falta = new[] { "USUARIO", "PROCEDIMENTO" }.Where(c => !presentes.ContainsKey(c)).ToList();
            foreach (var x in falta)
            {
                var p = Parecida(x, f.Colunas);
                aba.Colunas.Add(Col("—", x, "faltando", "Obrigatória" + (p != "" ? $": existe “{p}”, mas o nome não bate" : "")));
            }
            foreach (var (x, efeito) in new[] { ("JUSTIFICATIVA", "todas as linhas ficam “SEM JUSTIFICATIVA”"), ("IDREGISTROCONTROLADO", "todas as linhas ficam “SEM SEGMENTO”") })
                if (!presentes.ContainsKey(x)) aba.Colunas.Add(Col("—", x, "faltando", "Opcional: " + efeito));
            if (dataUsada == null)
                aba.Colunas.Add(Col("—", "DATAHORAFIMATIVIDADE", "faltando", "Opcional: sem coluna de data, o filtro por dia fica indisponível"));
            if (falta.Count > 0) IgnorarAba(aba, "falta " + TextoFalta(falta, f.Colunas));
            else validas.Add(aba);
            abas.Add(aba);
        }
        if (validas.Count == 0)
        {
            alertas.Add(("erro", "", "Nenhuma aba tem as colunas USUARIO e PROCEDIMENTO: o Resumo mostraria erro ao abrir esta planilha."));
            return (abas, new());
        }
        // mesma montagem do leitor: só colunas usadas, nomes em maiúsculas, abas válidas empilhadas
        var partes = validas.Select(a => new Folha
        {
            Nome = a.Nome,
            Colunas = a.Dados.Colunas.Where(Volume.UsarColuna).ToList(),
            Linhas = a.Dados.Linhas.Select(l => a.Dados.Colunas.Select((c, j) => (c, j)).Where(x => Volume.UsarColuna(x.c)).Select(x => l[x.j]).ToArray()).ToList(),
        }).ToList();
        var df = Volume.Empilhar(partes);
        var d = Volume.Derivar(df);
        var colData = Volume.ColunaData(df.Colunas);
        if (colData != null)
        {
            var vals = df.Coluna(colData);
            int falhas = vals.Count(v => Datas.Strptime(v ?? "nan", "%d/%m/%Y %H:%M:%S") == null);
            if (vals.Length > 0 && falhas / (double)vals.Length > 0.5)
                alertas.Add(("aviso", "", $"Mais da metade das datas ({colData}) não está no formato dd/mm/aaaa hh:mm:ss; " +
                                          "o programa passou para a leitura flexível (dia primeiro). Confira a coluna DATA."));
        }
        var classe = d.Proc.Select(p => Classes.De(p, ctx.Classes)).ToArray();
        int ini = 0;
        foreach (var a in validas)
        {
            int n = a.N;
            int Off(int i) => ini + i;
            var orig = new Dictionary<string, string>();
            foreach (var c in a.Dados.Colunas) orig[c.Trim().ToUpperInvariant()] = c;
            string?[] B(string nome) => Enumerable.Range(0, n).Select(i => { var c = df.Col(nome); return c < 0 ? null : df.Linhas[Off(i)][c]; }).ToArray();
            a.Derivadas = new()
            {
                ("Usuário (lido)", Enumerable.Range(0, n).Select(i => (string?)d.Usuario[Off(i)]).ToArray()),
                ("Procedimento (lido)", Enumerable.Range(0, n).Select(i => (string?)d.Proc[Off(i)]).ToArray()),
                ("Classe", Enumerable.Range(0, n).Select(i => (string?)Classes.Rotulo[classe[Off(i)]]).ToArray()),
                ("Segmento", Enumerable.Range(0, n).Select(i => (string?)d.Seg[Off(i)]).ToArray()),
                ("Data (lida)", Enumerable.Range(0, n).Select(i => (string?)d.Data[Off(i)]).ToArray()),
                ("Motivo (lido)", Enumerable.Range(0, n).Select(i => (string?)d.Tipo[Off(i)]).ToArray()),
            };
            var vazia = Enumerable.Range(0, n).Select(i => df.Linhas[Off(i)].All(v => v == null)).ToArray();
            Marcar(a, vazia, "alerta", "linha vazia: mesmo assim conta como 1 registro");
            Marcar(a, Enumerable.Range(0, n).Select(i => classe[Off(i)] == Classes.Fora).ToArray(), "fora",
                i => $"procedimento fora da comparação ({d.Proc[Off(i)]})", null, new[] { "lida" });
            var bu = B("USUARIO");
            Marcar(a, Enumerable.Range(0, n).Select(i => bu[i] == null && !vazia[i]).ToArray(), "alerta", "usuário vazio: conta como “(SEM USUARIO)”", orig.GetValueOrDefault("USUARIO"));
            var bp = B("PROCEDIMENTO");
            Marcar(a, Enumerable.Range(0, n).Select(i => bp[i] == null && !vazia[i]).ToArray(), "alerta",
                i => $"procedimento vazio: lido como “{d.Proc[Off(i)]}” e fica fora da comparação", orig.GetValueOrDefault("PROCEDIMENTO"));
            if (colData != null && df.Colunas.Contains(colData))
            {
                var bd = B(colData);
                var lida = Enumerable.Range(0, n).Select(i => vazia[i] ? "x" : d.Data[Off(i)]).ToArray();
                var bruto = Enumerable.Range(0, n).Select(i => vazia[i] ? "x" : bd[i]).ToArray();
                var nt = ChecarDatas(a, bruto, lida, orig.GetValueOrDefault(colData));
                if (nt > 0) alertas.Add(("aviso", a.Nome, $"{Texto.Fmt(nt)} data(s) com dia e mês trocados na leitura."));
            }
            if (df.Colunas.Contains("JUSTIFICATIVA"))
            {
                var colJ = orig.GetValueOrDefault("JUSTIFICATIVA");
                Marcar(a, Enumerable.Range(0, n).Select(i => classe[Off(i)] == Classes.Reprov && d.Tipo[Off(i)] == "OUTROS").ToArray(), "alerta",
                    "reprovação sem título antes de “:” na justificativa: motivo vira OUTROS", colJ);
                Marcar(a, Enumerable.Range(0, n).Select(i => classe[Off(i)] == Classes.Reprov && d.Tipo[Off(i)] == "SEM JUSTIFICATIVA" && !vazia[i]).ToArray(), "alerta",
                    "reprovação sem justificativa", colJ);
            }
            if (df.Colunas.Contains("IDREGISTROCONTROLADO"))
            {
                var bi = B("IDREGISTROCONTROLADO");
                Marcar(a, Enumerable.Range(0, n).Select(i => bi[i] == null && !vazia[i]).ToArray(), "alerta",
                    i => $"IDREGISTROCONTROLADO vazio: segmento vira “{d.Seg[Off(i)]}”", orig.GetValueOrDefault("IDREGISTROCONTROLADO"));
            }
            ini += n;
        }
        var v = new List<TabelaValores>();
        var procBruto = df.Coluna("PROCEDIMENTO");
        var g = Enumerable.Range(0, df.Linhas.Count).GroupBy(i => (procBruto[i] ?? "(vazio)", d.Proc[i], classe[i]))
            .Select(x => (x.Key, n: x.Count())).OrderBy(x => x.Key.Item1, StringComparer.Ordinal).ThenBy(x => x.Key.Item2, StringComparer.Ordinal)
            .OrderByDescending(x => x.n).ToList();
        v.Add(Tab("Procedimentos", new[] { ("Na planilha", 0, "e"), ("Lido como", 220, "e"), ("Classe", 190, "e"), ("Linhas", 90, "d") },
            g.Select(x => new object[] { x.Key.Item1, x.Key.Item2, Classes.Rotulo.GetValueOrDefault(x.Key.Item3, x.Key.Item3), x.n }),
            "A classe vem de “Classificar procedimentos” no Resumo."));
        v.Add(Tab("Motivos das reprovações", new[] { ("Motivo lido (texto antes do “:”)", 0, "e"), ("Linhas", 90, "d") },
            Inspecao.ValueCounts(Enumerable.Range(0, df.Linhas.Count).Where(i => classe[i] == Classes.Reprov).Select(i => d.Tipo[i]))
                .Select(x => new object[] { x.valor, (int)x.n }), "OUTROS = justificativa sem título antes do “:”."));
        v.Add(Tab("Segmentos", new[] { ("Segmento lido", 0, "e"), ("Nome exibido", 240, "e"), ("Linhas", 90, "d") },
            Inspecao.ValueCounts(d.Seg).Select(x => new object[] { x.valor, Texto.NomeSegmento(x.valor), (int)x.n })));
        var vcData = Inspecao.ValueCounts(d.Data).ToDictionary(x => x.valor, x => x.n);
        var dias = Texto.OrdenarDias(vcData.Keys).Concat(vcData.ContainsKey("") ? new[] { "" } : Array.Empty<string>());
        v.Add(Tab("Datas", new[] { ("Dia lido", 0, "e"), ("Linhas", 90, "d") }, dias.Select(x => new object[] { x == "" ? "(sem data)" : x, (int)vcData[x] })));
        v.Add(Tab("Usuários", new[] { ("Usuário lido", 0, "e"), ("Linhas", 90, "d") }, Inspecao.ValueCounts(d.Usuario).Select(x => new object[] { x.valor, (int)x.n })));
        return (abas, v);
    }

    // ---------------- inconsistências (planilha de inspeção) ----------------
    static readonly HashSet<string> Sim = new() { "sim", "s", "yes", "y", "x", "1", "true", "verdadeiro", "com desvio", "desvio" };
    static readonly HashSet<string> Nao = new() { "nao", "n", "no", "0", "false", "falso", "sem desvio" };
    static readonly HashSet<string> SemErro = new() { "sem erro", "sem erros", "nenhum", "nenhum erro", "sem desvio", "sem gravidade", "n/a", "na", "nao se aplica", "-" };
    static readonly HashSet<string> Grav = new() { "leve", "baixa", "baixo", "media", "medio", "moderada", "moderado", "alta", "alto", "grave", "gravissimo",
        "gravissima", "critico", "critica", "muito grave" };
    static readonly HashSet<string> NaoCandidatas = new(Inspecao.NecessariasEvo) { "COLABORADOR", "COOP", "DATA DA INSPECAO" };

    static string Nv(string? v) => v == null ? "" : Texto.ColapsarEspacos(Texto.SemAcento(v)).Trim().ToLowerInvariant();

    static string? AcharColunaInc(FolhaN f, string[] preferidos, HashSet<string> aceita, double minimo, string? escolha, HashSet<string>? aceitaConteudo = null)
    {
        if (escolha == "") return null;
        if (escolha != null) return f.Colunas.Contains(escolha) ? escolha : null;
        var cands = f.Colunas.Where(c => !NaoCandidatas.Contains(c) && !c.StartsWith("UNNAMED")).ToList();
        var atual = aceita;
        double Taxa(string c)
        {
            int k = f.Col(c);
            var vals = f.Linhas.Select(l => Nv(l[k])).Where(x => x != "").ToList();
            return vals.Count > 0 ? vals.Count(atual.Contains) / (double)vals.Count : 0.0;
        }
        foreach (var c in cands)
            if (preferidos.Any(p => c.Contains(p)) && Taxa(c) >= 0.5) return c;
        if (aceitaConteudo != null) atual = aceitaConteudo;
        string? melhor = null;
        double mt = -1;
        foreach (var c in cands) { var t = Taxa(c); if (t > mt) { mt = t; melhor = c; } }
        return melhor != null && Taxa(melhor) >= minimo ? melhor : null;
    }

    public static List<string> Candidatas(Bruto br)
    {
        var res = new List<string>();
        foreach (var f in br.Folhas)
            foreach (var c in f.Colunas)
            {
                var n = Inspecao.NormCabecalho(c);
                if (!NaoCandidatas.Contains(n) && !n.StartsWith("UNNAMED") && !res.Contains(n)) res.Add(n);
            }
        return res;
    }

    /// <summary>Confere TIPO DE ERRO × coluna de desvio (Sim/Não) × gravidade, linha a linha.</summary>
    static List<Inconsistencia> Inconsistencias(AbaDep a, FolhaN f, Dictionary<int, Inspecao.LinhaBase> d, CtxDep ctx)
    {
        var t = a.Dados;
        var orig = new Dictionary<string, string>();
        foreach (var c in t.Colunas) orig[Inspecao.NormCabecalho(c)] = c;
        var pos = t.Colunas.Select((c, i) => (c, i)).GroupBy(x => x.c).ToDictionary(g => g.Key, g => g.First().i);
        var colD = AcharColunaInc(f, new[] { "DESVIO" }, Sim.Union(Nao).ToHashSet(), 0.8, ctx.IncDesvio, new HashSet<string> { "sim", "nao", "s", "n", "yes", "no" });
        var colG = AcharColunaInc(f, new[] { "GRAVIDADE", "CRITICIDADE", "SEVERIDADE", "IMPACTO", "CLASSIFICA", "NIVEL" }, Grav.Union(SemErro).ToHashSet(), 0.8, ctx.IncGrav);
        if (colG == colD) colG = null;
        a.IncCols = (colD != null ? orig[colD] : null, colG != null ? orig[colG] : null);
        foreach (var (papel, cn) in new[] { ("marcação de desvio (Sim/Não)", colD), ("gravidade", colG) })
            if (cn != null)
                foreach (var c in a.Colunas.Where(c => c.Original == orig[cn]))
                    (c.Status, c.Papel) = ("conferencia", $"Não entra na conta: o depurador usa como {papel} para conferir o preenchimento");

        // valores por linha da aba (posição na aba original)
        int nA = a.N;
        var linhaDe = new int?[nA];   // posição em f
        for (int k = 0; k < f.Idx.Count; k++) linhaDe[f.Idx[k]] = k;
        string? Bruto(int i, string col) { var k = linhaDe[i]; return k == null ? null : f.V(k.Value, col); }
        bool Em(int i) => linhaDe[i] != null;

        var regras = new List<(bool[] mask, int cat, Func<int, string> texto, string?[] cols)>();
        var avaliada = new bool[nA]; var tSem = new bool[nA]; var tErr = new bool[nA];
        var sim = new bool[nA]; var nao = new bool[nA]; var dVazio = new bool[nA]; var dOutro = new bool[nA];
        var gSem = new bool[nA]; var gErr = new bool[nA]; var gVazio = new bool[nA];
        var vt = new string[nA]; var vd = new string[nA]; var vg = new string[nA];
        for (int i = 0; i < nA; i++)
        {
            if (!Em(i)) { vt[i] = vd[i] = vg[i] = ""; continue; }
            var raw = Bruto(i, "TIPO DE ERRO");
            avaliada[i] = raw != null;
            var tp = Nv(raw);
            tSem[i] = avaliada[i] && SemErro.Contains(tp);
            tErr[i] = avaliada[i] && tp != "" && !tSem[i];
            vt[i] = (raw ?? "").Trim();
            if (colD != null)
            {
                var dv = Nv(Bruto(i, colD));
                sim[i] = Sim.Contains(dv); nao[i] = Nao.Contains(dv); dVazio[i] = dv == "";
                dOutro[i] = !(sim[i] || nao[i] || dVazio[i]);
                vd[i] = (Bruto(i, colD) ?? "").Trim();
            }
            else vd[i] = "";
            if (colG != null)
            {
                var gv = Nv(Bruto(i, colG));
                gSem[i] = SemErro.Contains(gv); gVazio[i] = gv == ""; gErr[i] = !gSem[i] && !gVazio[i];
                vg[i] = (Bruto(i, colG) ?? "").Trim();
            }
            else vg[i] = "";
        }
        string nd = colD != null ? orig[colD] : "", ng = colG != null ? orig[colG] : "";
        var marcas = new string[nA];
        for (int i = 0; i < nA; i++)
        {
            var m = "";
            if (colD != null && sim[i]) m = $"{nd} = “{vd[i]}”";
            if (colG != null && gErr[i]) m = m != "" ? $"{m}; {ng} = “{vg[i]}”" : $"{ng} = “{vg[i]}”";
            marcas[i] = m;
        }
        bool[] M(Func<int, bool> f2) => Enumerable.Range(0, nA).Select(i => Em(i) && f2(i)).ToArray();
        regras.Add((M(i => tSem[i] && sim[i]), 0, i => $"TIPO DE ERRO “{vt[i]}”, mas {nd} = “{vd[i]}”", new[] { "TIPO DE ERRO", colD }));
        regras.Add((M(i => tErr[i] && nao[i]), 1, i => $"TIPO DE ERRO “{vt[i]}”, mas {nd} = “{vd[i]}”", new[] { "TIPO DE ERRO", colD }));
        regras.Add((M(i => tSem[i] && gErr[i]), 2, i => $"TIPO DE ERRO “{vt[i]}”, mas {ng} = “{vg[i]}”", new[] { "TIPO DE ERRO", colG }));
        regras.Add((M(i => tErr[i] && gSem[i]), 3, i => $"TIPO DE ERRO “{vt[i]}”, mas {ng} = “{vg[i]}”", new[] { "TIPO DE ERRO", colG }));
        regras.Add((M(i => (sim[i] && gSem[i]) || (nao[i] && gErr[i])), 4, i => $"{nd} = “{vd[i]}”, mas {ng} = “{vg[i]}”", new[] { colD, colG }));
        regras.Add((M(i => !avaliada[i] && (sim[i] || gErr[i])), 5, i => $"marcado como desvio ({marcas[i]}), mas TIPO DE ERRO está vazio: o programa descarta a linha",
            new[] { "TIPO DE ERRO", colD, colG }));
        regras.Add((M(i => tErr[i] && dVazio[i] && colD != null), 6, i => $"TIPO DE ERRO “{vt[i]}”, mas {nd} está vazio", new[] { colD }));
        regras.Add((M(i => tErr[i] && gVazio[i] && colG != null), 6, i => $"TIPO DE ERRO “{vt[i]}”, mas {ng} está vazio", new[] { colG }));
        regras.Add((M(i => dOutro[i]), 7, i => $"{nd} = “{vd[i]}”: valor não reconhecido (esperado Sim ou Não)", new[] { colD }));

        var linhas = new SortedDictionary<int, (List<int> cats, List<string> txt, List<string> cols)>();
        foreach (var (mask, cat, texto, cols0) in regras)
        {
            if (!mask.Any(x => x)) continue;
            var cols = cols0.Where(c => c != null).Select(c => c!).ToList();
            Marcar(a, mask, "alerta", i => "preenchimento inconsistente: " + texto(i), orig[cols[0]]);
            foreach (var c in cols.Skip(1)) AnotarCelula(a, mask, orig[c], i => "preenchimento inconsistente: " + texto(i));
            for (int i = 0; i < nA; i++)
            {
                if (!mask[i]) continue;
                if (!linhas.TryGetValue(i, out var e)) linhas[i] = e = (new(), new(), new());
                e.cats.Add(cat);
                e.txt.Add(texto(i));
                foreach (var c in cols) if (!e.cols.Contains(orig[c])) e.cols.Add(orig[c]);
            }
        }
        var res = new List<Inconsistencia>();
        foreach (var (i, e) in linhas)
        {
            var raw = Bruto(i, "TIPO DE ERRO");
            var prog = raw == null ? "Não conta (descartada)" : d.TryGetValue(i, out var lb) && lb.Desvio ? "Desvio" : "Sem desvio";
            var linhaExcel = a.Linhas[i];
            res.Add(new Inconsistencia
            {
                Aba = a.Nome, Linha = linhaExcel,
                Celulas = string.Join(", ", e.cols.Select(c => $"{XlsxWriter.ColunaLetra(pos[c])}{linhaExcel}")),
                Colaborador = Bruto(i, "COLABORADOR") ?? "", Tipo = vt[i], Desvio = vd[i], Gravidade = vg[i], Programa = prog,
                Categoria = CategoriasInc[e.cats.Min()], Cats = e.cats.Distinct().OrderBy(x => x).ToList(), Problema = string.Join("  ·  ", e.txt),
            });
        }
        a.Inconsistencias = res;
        return res;
    }

    // ---------------- modo Inspeção ----------------
    static (List<AbaDep>, List<TabelaValores>) DepInspecao(Bruto br, CtxDep ctx, List<(string, string, string)> alertas)
    {
        var sep = Inspecao.SepararAbas(br.Folhas);
        var validos = sep.Validas.Select(f => f.Nome).ToHashSet();
        var abas = new List<AbaDep>();
        foreach (var t in br.Folhas)
        {
            var aba = NovaAba(t);
            var presentes = t.Colunas.Select(Inspecao.NormCabecalho).ToHashSet();
            foreach (var c in t.Colunas)
            {
                var n = Inspecao.NormCabecalho(c);
                string st, papel;
                if (Inspecao.NecessariasEvo.Contains(n)) (st, papel) = ("obrigatoria", PapelInspecao[n]);
                else if (PapelInspecao.ContainsKey(n)) (st, papel) = ("usada", PapelInspecao[n]);
                else (st, papel) = ("ignorada", "Não é usada pela Evolução");
                aba.Colunas.Add(Col(c, n, st, papel, t.ColunaTexto(t.Colunas.IndexOf(c))));
            }
            foreach (var x in Inspecao.NecessariasEvo.Where(x => !presentes.Contains(x)).OrderBy(x => x, StringComparer.Ordinal))
            {
                var p = Parecida(x, t.Colunas);
                aba.Colunas.Add(Col("—", x, "faltando", "Obrigatória" + (p != "" ? $": existe “{p}”, mas o nome não bate" : "")));
            }
            foreach (var x in new[] { "COLABORADOR", "COOP", "DATA DA INSPECAO" })
                if (!presentes.Contains(x)) aba.Colunas.Add(Col("—", x, "faltando", "Opcional: " + PapelInspecao[x]));
            if (!validos.Contains(t.Nome))
            {
                var m = sep.Motivos.GetValueOrDefault(t.Nome, "aba ignorada");
                if (m.StartsWith("falta ")) m = "falta " + TextoFalta(Inspecao.NecessariasEvo.Where(x => !presentes.Contains(x)).OrderBy(x => x, StringComparer.Ordinal), t.Colunas);
                IgnorarAba(aba, m);
            }
            abas.Add(aba);
        }
        if (sep.Validas.Count == 0)
        {
            alertas.Add(("erro", "", "Nenhuma aba tem INSPETOR, INTERNO OU EXTERNO, TIPO DE AUTORIZACAO e TIPO DE ERRO: a Evolução mostraria erro ao abrir esta planilha."));
            return (abas, new());
        }
        foreach (var (nome, o) in sep.Copias) alertas.Add(("info", nome, $"Aba ignorada por ser cópia idêntica de “{o}”."));

        var porNome = abas.ToDictionary(a => a.Nome);
        DateTime?[]? datas = null;
        var offs = new Dictionary<string, int>();
        if (sep.Validas.Any(f => f.Tem("DATA DA INSPECAO")))
        {
            var vals = new List<string?>();
            foreach (var f in sep.Validas) { offs[f.Nome] = vals.Count; for (int i = 0; i < f.Linhas.Count; i++) vals.Add(f.V(i, "DATA DA INSPECAO")); }
            datas = Datas.ToDatetime(vals);
        }
        var (der, temCoop, temColab) = Inspecao.Derivar(sep.Validas);
        foreach (var f in sep.Validas)
        {
            var a = porNome[f.Nome];
            int nA = a.N;
            var orig = new Dictionary<string, string>();
            foreach (var c in a.Dados.Colunas) orig[Inspecao.NormCabecalho(c)] = c;
            var linhaDe = new int?[nA];
            for (int k = 0; k < f.Idx.Count; k++) linhaDe[f.Idx[k]] = k;
            Marcar(a, linhaDe.Select(x => x == null).ToArray(), "descartada", "linha totalmente vazia");
            string? B(int i, string col) => linhaDe[i] is int k ? f.V(k, col) : null;
            bool Em(int i) => linhaDe[i] != null;
            Marcar(a, Enumerable.Range(0, nA).Select(i => Em(i) && B(i, "TIPO DE ERRO") == null).ToArray(), "descartada",
                "TIPO DE ERRO vazio: linha não avaliada (não conta)", orig["TIPO DE ERRO"]);
            var dAba = der.Where(x => x.Aba == f.Nome).ToDictionary(x => f.Idx[x.Pos]);
            string?[] Der(Func<Inspecao.LinhaBase, string?> sel) => Enumerable.Range(0, nA).Select(i => dAba.TryGetValue(i, out var x) ? sel(x) : null).ToArray();
            a.Derivadas = new()
            {
                ("Desvio?", Der(x => x.Desvio ? "Sim" : "Não")), ("Erro (lido)", Der(x => x.Erro)),
                ("Origem (lida)", Der(x => x.Origem)), ("Autorização (lida)", Der(x => x.Aut)),
            };
            if (temColab) a.Derivadas.Add(("Colaborador (lido)", Der(x => x.Colab)));
            if (temCoop) a.Derivadas.Add(("Cooperativa (lida)", Der(x => x.Coop)));
            var avaliada = Enumerable.Range(0, nA).Select(i => Em(i) && B(i, "TIPO DE ERRO") != null).ToArray();
            Marcar(a, Enumerable.Range(0, nA).Select(i => avaliada[i] && B(i, "TIPO DE ERRO")!.Trim() == "").ToArray(), "alerta",
                "TIPO DE ERRO só com espaços: conta como avaliada SEM desvio", orig["TIPO DE ERRO"]);
            Marcar(a, Enumerable.Range(0, nA).Select(i => dAba.TryGetValue(i, out var x) && x.Desvio &&
                Texto.ColapsarEspacos(Texto.SemAcento(x.TipoErroBruto!).Trim()).ToLowerInvariant() == "sem erro").ToArray(), "alerta",
                "parece “Sem erro”, mas tem espaço ou grafia diferente: está contando como DESVIO", orig["TIPO DE ERRO"]);
            Marcar(a, Enumerable.Range(0, nA).Select(i => avaliada[i] && B(i, "INTERNO OU EXTERNO") == null).ToArray(), "alerta",
                "origem vazia: vira “Nao Informado”", orig["INTERNO OU EXTERNO"]);
            Marcar(a, Enumerable.Range(0, nA).Select(i => dAba.TryGetValue(i, out var x) && !Inspecao.OrigensPadrao.Contains(x.Origem) && B(i, "INTERNO OU EXTERNO") != null).ToArray(),
                "alerta", i => $"origem fora de Interno/Externo/Chamados: vira a categoria “{dAba[i].Origem}”", orig["INTERNO OU EXTERNO"]);
            Marcar(a, Enumerable.Range(0, nA).Select(i => avaliada[i] && B(i, "TIPO DE AUTORIZACAO") == null).ToArray(), "alerta",
                "autorização vazia: vira “NAO INFORMADO”", orig["TIPO DE AUTORIZACAO"]);
            if (f.Tem("COLABORADOR"))
                Marcar(a, Enumerable.Range(0, nA).Select(i => avaliada[i] && B(i, "COLABORADOR") == null).ToArray(), "alerta",
                    "colaborador vazio: vira “(não informado)”", orig["COLABORADOR"]);
            if (datas != null && orig.ContainsKey("DATA DA INSPECAO"))
            {
                var lida = Enumerable.Range(0, nA).Select(i => linhaDe[i] is int k && datas[offs[f.Nome] + k] is DateTime dt
                    ? dt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "").ToArray();
                a.Derivadas.Add(("Data (lida)", lida.Select((x, i) => linhaDe[i] == null ? null : (string?)x).ToArray()));
                var bruto = Enumerable.Range(0, nA).Select(i => B(i, "DATA DA INSPECAO")).ToArray();
                // linhas totalmente vazias não fazem parte de b no original
                var nt = ChecarDatasSub(a, bruto, lida, orig["DATA DA INSPECAO"], linhaDe.Select(x => x != null).ToArray());
                if (nt > 0) alertas.Add(("aviso", f.Nome, $"{Texto.Fmt(nt)} data(s) da inspeção com dia e mês trocados na leitura (afeta o período e o mês sugerido)."));
            }
            var inc = Inconsistencias(a, f, dAba, ctx);
            if (inc.Count > 0) alertas.Add(("aviso", f.Nome, $"{Texto.Fmt(inc.Count)} linha(s) com preenchimento inconsistente (veja a aba Inconsistências)."));
            else if (a.IncCols is { } ic && ic.desvio == null && ic.gravidade == null)
                alertas.Add(("info", f.Nome, "Não achei coluna de marcação de desvio (Sim/Não) nem de gravidade para conferir o preenchimento; escolha na aba Inconsistências."));
        }
        var v = new List<TabelaValores>();
        var g = der.GroupBy(x => (B: x.TipoErroBruto!.Trim() != "" ? x.TipoErroBruto! : "(só espaços)", x.Erro, x.Desvio))
            .Select(x => (x.Key, n: x.Count())).OrderBy(x => x.Key.B, StringComparer.Ordinal).ThenBy(x => x.Key.Erro, StringComparer.Ordinal)
            .ThenBy(x => x.Key.Desvio).OrderByDescending(x => x.n).ToList();
        v.Add(Tab("Tipos de erro", new[] { ("Na planilha", 0, "e"), ("Lido como", 240, "e"), ("Desvio?", 90, "c"), ("Linhas", 90, "d") },
            g.Select(x => new object[] { x.Key.B, x.Key.Erro, x.Key.Desvio ? "Sim" : "Não", x.n }),
            "Desvio = TIPO DE ERRO diferente de “Sem erro” (sem acento, sem caixa)."));
        var folhaDe = sep.Validas.ToDictionary(f => f.Nome);
        foreach (var (campo, tit, sel) in new (string, string, Func<Inspecao.LinhaBase, string>)[]
                 { ("INTERNO OU EXTERNO", "Origens", x => x.Origem), ("TIPO DE AUTORIZACAO", "Autorizações", x => x.Aut) })
        {
            var gg = der.GroupBy(x => (B: folhaDe[x.Aba].V(x.Pos, campo) ?? "(vazio)", L: sel(x))).Select(x => (x.Key, n: x.Count()))
                .OrderBy(x => x.Key.B, StringComparer.Ordinal).ThenBy(x => x.Key.L, StringComparer.Ordinal).OrderByDescending(x => x.n).ToList();
            v.Add(Tab(tit, new[] { ("Na planilha", 0, "e"), ("Lido como", 260, "e"), ("Linhas", 90, "d") }, gg.Select(x => new object[] { x.Key.B, x.Key.L, x.n })));
        }
        v.Add(Tab("Inspetores (abas)", new[] { ("Aba", 0, "e"), ("Avaliações", 110, "d"), ("Desvios", 110, "d") },
            der.GroupBy(x => x.Aba).OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new object[] { x.Key, x.Count(), x.Count(y => y.Desvio) })));
        if (temColab)
            v.Add(Tab("Colaboradores", new[] { ("Colaborador lido", 0, "e"), ("Avaliações", 110, "d"), ("Desvios", 110, "d") },
                der.GroupBy(x => x.Colab!).OrderBy(x => x.Key, StringComparer.Ordinal).OrderByDescending(x => x.Count())
                    .Select(x => new object[] { x.Key, x.Count(), x.Count(y => y.Desvio) })));
        return (abas, v);
    }

    /// <summary>ChecarDatas aplicado só às linhas que existem no quadro de dados (sem as totalmente vazias).</summary>
    static int ChecarDatasSub(AbaDep a, string?[] bruto, string[] lida, string coluna, bool[] existe)
    {
        // linhas que não existem: valor "x"/"x" (não geram alerta), como no original (fora do índice)
        var b2 = bruto.Select((v, i) => existe[i] ? v : "x").ToArray();
        var l2 = lida.Select((v, i) => existe[i] ? v : "x").Select(x => (string?)x).ToArray();
        return ChecarDatas(a, b2, l2, coluna);
    }

    // ---------------- modo Pareceres ----------------
    static (List<AbaDep>, List<TabelaValores>) DepPareceres(Bruto br, CtxDep ctx, List<(string, string, string)> alertas)
    {
        var abas = new List<AbaDep>();
        var excl = ctx.ProcExcluidos.Select(Pareceres.NormProc).ToHashSet();
        var pctx = Pareceres.Contexto.Criar(ctx.Padroes, ctx.Ignorar, ctx.Acentos);
        var v = new List<TabelaValores>();
        for (int idx = 0; idx < br.Folhas.Count; idx++)
        {
            var t = br.Folhas[idx];
            var aba = NovaAba(t);
            abas.Add(aba);
            var presentes = t.Colunas.Select(c => c.Trim().ToUpperInvariant()).ToHashSet();
            var dataUsada = Volume.ColunaData(presentes);
            foreach (var c in t.Colunas)
            {
                var n = c.Trim().ToUpperInvariant();
                string st, papel;
                if (Pareceres.ColunasObrigatorias.Contains(n)) (st, papel) = ("obrigatoria", PapelPareceres[n]);
                else if (PapelPareceres.ContainsKey(n))
                {
                    (st, papel) = ("usada", PapelPareceres[n]);
                    if (n is "DATAHORAFIMATIVIDADE" or "DATAHORAINICIOATIVIDADE" && n != dataUsada) (st, papel) = ("sem_uso", "Lida, mas a data vem de DATAHORAFIMATIVIDADE");
                }
                else (st, papel) = ("ignorada", "Não é usada pelos Pareceres");
                aba.Colunas.Add(Col(c, n, st, papel, t.ColunaTexto(t.Colunas.IndexOf(c))));
            }
            if (idx > 0) { IgnorarAba(aba, "a aba Pareceres lê só a primeira aba do arquivo"); continue; }
            var falta = Pareceres.ColunasObrigatorias.Where(c => !presentes.Contains(c)).ToList();
            foreach (var x in falta)
            {
                var p = Parecida(x, t.Colunas);
                aba.Colunas.Add(Col("—", x, "faltando", "Obrigatória" + (p != "" ? $": existe “{p}”, mas o nome não bate" : "")));
            }
            if (falta.Count > 0)
            {
                IgnorarAba(aba, "falta " + TextoFalta(falta, t.Colunas));
                alertas.Add(("erro", t.Nome, "A planilha não tem " + string.Join(", ", falta) + ": a aba Pareceres mostraria erro."));
                continue;
            }
            var orig = new Dictionary<string, string>();
            foreach (var c in t.Colunas) orig[c.Trim().ToUpperInvariant()] = c;
            var df = Pareceres.Derivar(t);
            int nA = aba.N;
            var res = Enumerable.Range(0, nA).Select(i => Pareceres.Classificar(df.Justificativa[i], pctx)).ToArray();
            aba.Derivadas = new()
            {
                ("Usuário (lido)", df.Usuario.Select(x => (string?)x).ToArray()), ("Data (lida)", df.Data.Select(x => (string?)x).ToArray()),
                ("Tipo de parecer", res.Select(r => (string?)r.canon).ToArray()), ("Resultado", res.Select(r => (string?)r.status).ToArray()),
                ("Sugestão", res.Select(r => (string?)r.sug).ToArray()),
            };
            if (orig.ContainsKey("PROCEDIMENTO"))
                Marcar(aba, Enumerable.Range(0, nA).Select(i => excl.Contains(Pareceres.NormProc(df.Procedimento[i]))).ToArray(), "descartada",
                    "procedimento fora da análise (Pareceres › Procedimentos)", orig["PROCEDIMENTO"]);
            Marcar(aba, res.Select(r => r.tipo == "vazio").ToArray(), "descartada", "justificativa vazia ou só com CPF/CNPJ", orig["JUSTIFICATIVA"]);
            var ign = res.Select(r => r.tipo == "ignorado").ToArray();
            if (ign.Any(x => x))
                Marcar(aba, ign, "descartada", i =>
                {
                    var k = Texto.SemAcento(res[i].limpo).ToUpperInvariant();
                    return $"ignorada pela regra “{pctx.RegexIgnorar.FirstOrDefault(x => x.IsMatch(k))?.ToString() ?? ""}”";
                }, orig["JUSTIFICATIVA"]);
            Marcar(aba, res.Select(r => r.status == "Divergente").ToArray(), "alerta",
                i => "parecer fora do padrão: " + res[i].motivo + (res[i].sug == "" ? "" : $" (sugestão: {res[i].sug})"), orig["JUSTIFICATIVA"]);
            if (dataUsada != null)
                ChecarDatas(aba, t.ColunaTexto(t.Colunas.IndexOf(orig[dataUsada])), df.Data.Select(x => (string?)x).ToArray(), orig[dataUsada]);
            if (ctx.Padroes.Count == 0)
                alertas.Add(("aviso", t.Nome, "Nenhum parecer padrão cadastrado: sem o documento de pareceres, nada é comparado (anexe na aba Pareceres)."));
            var gg = Enumerable.Range(0, nA).Where(i => aba.Estado[i] != "descartada" && res[i].tipo == "parecer")
                .GroupBy(i => (res[i].canon, res[i].status)).Select(x => (x.Key, n: x.Count()))
                .OrderBy(x => x.Key.canon, StringComparer.Ordinal).ThenBy(x => x.Key.status, StringComparer.Ordinal).OrderByDescending(x => x.n).ToList();
            v.Add(Tab("Tipos de parecer", new[] { ("Tipo lido", 0, "e"), ("Resultado", 140, "c"), ("Linhas", 90, "d") },
                gg.Select(x => new object[] { x.Key.canon, x.Key.status == "" ? "—" : x.Key.status, x.n })));
            if (orig.ContainsKey("PROCEDIMENTO"))
                v.Add(Tab("Procedimentos", new[] { ("Procedimento", 0, "e"), ("Na análise?", 120, "c"), ("Linhas", 90, "d") },
                    Inspecao.ValueCounts(Enumerable.Range(0, nA).Select(i => df.ProcVazio[i] ? "(vazio)" : df.Procedimento[i]))
                        .Select(x => new object[] { x.valor, excl.Contains(Pareceres.NormProc(x.valor)) ? "Não" : "Sim", (int)x.n })));
            if (br.Folhas.Count > 1)
                alertas.Add(("info", "", $"O arquivo tem {br.Folhas.Count} abas; a aba Pareceres lê só a primeira (“{t.Nome}”)."));
        }
        return (abas, v);
    }

    /// <summary>depurar: diagnóstico completo da leitura (por aba, por coluna, por linha e por valor).</summary>
    public static ResultadoDep Depurar(Bruto br, string modo, CtxDep ctx)
    {
        var detectado = DetectarModo(br);
        var usado = modo == "auto" ? detectado : modo;
        var alertas = new List<(string, string, string)>();
        var (abas, valores) = usado switch
        {
            "inspecao" => DepInspecao(br, ctx, alertas),
            "pareceres" => DepPareceres(br, ctx, alertas),
            _ => DepVolume(br, ctx, alertas),
        };
        foreach (var a in abas)
        {
            a.Contagem = Contagem(a);
            if (a.Status != "ignorada" && a.Contagem["descartada"] > 0) a.Status = "parcial";
            if (a.Status == "ignorada") alertas.Add((a.N > 0 ? "aviso" : "info", a.Nome, "Aba ignorada: " + a.Motivo + "."));
            foreach (var c in a.Colunas)
                if (c.Status is "obrigatoria" or "usada" && c.Preenchidas == 0 && a.N > 0 && a.Status != "ignorada")
                    alertas.Add(("aviso", a.Nome, $"A coluna {c.Original} existe mas está totalmente vazia."));
            var unnamed = a.Colunas.Where(c => c.Original.ToUpperInvariant().StartsWith("UNNAMED")).ToList();
            if (a.N > 0 && unnamed.Count > 0 && unnamed.Count >= Math.Max(2, a.Colunas.Count / 2))
                alertas.Add(("aviso", a.Nome, "Muitas colunas sem título: o cabeçalho talvez não esteja na linha 1."));
        }
        if (br.Truncado)
            alertas.Insert(0, ("aviso", "", $"Leitura limitada às primeiras {Texto.Fmt(br.Limite ?? 0)} linhas de cada aba: as contagens são parciais."));
        if (modo == "auto")
            alertas.Insert(0, ("info", "", $"Modo detectado: {NomeModo(usado)} (é como a aba {OndeModo[usado]} lê este arquivo)."));
        else if (modo != detectado)
            alertas.Insert(0, ("info", "", $"Pelo formato, esta planilha seria lida como {NomeModo(detectado)} (aba {OndeModo[detectado]}); você escolheu {NomeModo(usado)}."));
        return new ResultadoDep
        {
            Arquivo = br.Arquivo, Info = br.Info, Modo = usado, ModoPedido = modo, Detectado = detectado, Abas = abas, Valores = valores,
            Alertas = alertas, Total = StatusDep.ToDictionary(s => s.chave, s => abas.Sum(a => a.Contagem[s.chave])),
            Linhas = abas.Sum(a => a.N),
            Inconsistencias = abas.SelectMany(a => a.Inconsistencias).ToList(),
            IncCols = abas.Where(a => a.IncCols != null).ToDictionary(a => a.Nome, a => a.IncCols!.Value),
            Candidatas = usado == "inspecao" ? Candidatas(br) : new(),
            AbasLidas = abas.Count(a => a.Status != "ignorada"),
            ColunasUsadas = abas.Where(a => a.Status != "ignorada").Sum(a => a.Colunas.Count(c => c.Status is "obrigatoria" or "usada")),
            ColunasTotal = abas.Sum(a => a.Colunas.Count(c => c.Status != "faltando")),
        };
    }

    public static byte[] Exportar(ResultadoDep r)
    {
        var cores = new Dictionary<string, string> { ["lida"] = "DFF2E7", ["fora"] = "ECEBF0", ["alerta"] = "FBEBC8", ["descartada"] = "F8D9D7" };
        var wb = new XlsxWriter();
        var s = wb.Planilha("Resumo");
        s.Cabecalho(new object?[] { "Aba", "Situação", "Motivo", "Linhas" }.Concat(StatusDep.Select(x => (object?)x.nome)).ToArray());
        foreach (var a in r.Abas)
            s.Linha(new object?[] { a.Nome, a.Status switch { "lida" => "Lida", "parcial" => "Lida em parte", _ => "Ignorada" }, a.Motivo, a.N }
                .Concat(StatusDep.Select(x => (object?)a.Contagem[x.chave])).ToArray());
        var al = wb.Planilha("Alertas");
        al.Cabecalho("Nível", "Aba", "Alerta");
        foreach (var (n, a, t) in r.Alertas) al.Linha(n, a, t);
        var co = wb.Planilha("Colunas");
        co.Cabecalho("Aba", "Coluna na planilha", "Nome reconhecido", "Situação", "Para que serve", "Preenchidas", "Vazias", "Exemplo");
        foreach (var a in r.Abas) foreach (var c in a.Colunas)
                co.Linha(a.Nome, c.Original, c.Normalizada, StatusCol[c.Status], c.Papel, c.Preenchidas, c.Vazias, c.Exemplo);
        foreach (var s0 in wb.Planilhas) s0.NegritoLinha(0);
        foreach (var a in r.Abas)
        {
            var w = wb.Planilha(a.Nome.Length > 28 ? a.Nome[..28] : a.Nome);
            w.Cabecalho(new object?[] { "Linha", "Status", "Motivo" }.Concat(a.Derivadas.Select(d => (object?)("→ " + d.nome)))
                .Concat(a.Dados.Colunas.Select(c => (object?)c)).ToArray());
            for (int i = 0; i < a.N; i++)
            {
                w.Linha(new object?[] { a.Linhas[i], NomeStatus(a.Estado[i]), a.MotivoLinha[i] }.Concat(a.Derivadas.Select(d => (object?)d.valores[i]))
                    .Concat(a.Dados.Linhas[i].Select(c => (object?)(c.Tem ? c.Texto : null))).ToArray());
                w.Preencher(i + 1, 1, cores[a.Estado[i]]);
            }
            w.NegritoLinha(0);
            w.Congelar(1, 3);
        }
        if (r.Inconsistencias.Count > 0)
        {
            var w = wb.Planilha("Inconsistências");
            w.Cabecalho("Aba", "Linha", "Células", "Colaborador", "Tipo de erro", "Desvio marcado", "Gravidade", "Programa conta como", "Tipo de inconsistência", "Detalhe");
            foreach (var x in r.Inconsistencias) w.Linha(x.Aba, x.Linha, x.Celulas, x.Colaborador, x.Tipo, x.Desvio, x.Gravidade, x.Programa, x.Categoria, x.Problema);
            w.NegritoLinha(0);
        }
        foreach (var v in r.Valores)
        {
            var w = wb.Planilha("Valores " + v.Titulo);
            w.Cabecalho(v.Colunas.Select(c => (object?)c.Titulo).ToArray());
            foreach (var l in v.Linhas) w.Linha(l);
            w.NegritoLinha(0);
        }
        return wb.Salvar();
    }
}
