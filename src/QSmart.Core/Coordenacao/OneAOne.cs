using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using QSmart.Core.Planilhas;

namespace QSmart.Core.Coordenacao;

// =====================================================================================
//  One a One da coordenação: cada colaborador anda numa "esteira" conforme o tempo passa
//  (Em dia → Chegando → Atrasado) e volta para o começo quando o 1:1 é feito.
//  Lideranças e pessoas não são fixas: tudo é montado a partir dos dados.
// =====================================================================================

/// <summary>Um 1:1 feito: data, o que foi conversado e a ação combinada.</summary>
public sealed class RegistroOO
{
    public DateOnly Data { get; set; }
    public string Obs { get; set; } = "";
    public string Acao { get; set; } = "";
    public DateOnly? AcaoPrazo { get; set; }
    public bool AcaoFeita { get; set; }
    /// <summary>Como a pessoa estava no 1:1 (1 = preocupante … 4 = ótima). Opcional.</summary>
    public int? Clima { get; set; }
}

/// <summary>Passagem por uma liderança (histórico de times).</summary>
public sealed class VinculoOO
{
    public string Lideranca { get; set; } = "";
    public DateOnly? Desde { get; set; }
    public DateOnly? Ate { get; set; }
}

public sealed class PessoaOO
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Nome { get; set; } = "";
    public string Lideranca { get; set; } = "";
    /// <summary>"Novo" (1:1 a cada 30 dias) ou "Antigo" (a cada 90).</summary>
    public string Tipo { get; set; } = "Antigo";
    public bool Arquivado { get; set; }
    public DateOnly? ArquivadoEm { get; set; }
    /// <summary>Quando entrou na carteira (pessoas novas).</summary>
    public DateOnly? Entrada { get; set; }
    /// <summary>Quando mudou de liderança pela última vez (pede um 1:1 em até 30 dias).</summary>
    public DateOnly? MudouEm { get; set; }
    /// <summary>Data de admissão na empresa (da planilha base da coordenação).</summary>
    public DateOnly? Admissao { get; set; }
    /// <summary>Coordenação a que a pessoa pertence (da planilha base).</summary>
    public string Coordenacao { get; set; } = "";
    public List<VinculoOO> Vinculos { get; set; } = new();
    public List<RegistroOO> Registros { get; set; } = new();

    public DateOnly? Ultimo => Registros.Count == 0 ? null : Registros.Max(r => r.Data);
    public RegistroOO? UltimoRegistro => Registros.OrderByDescending(r => r.Data).FirstOrDefault();
    public string Chave => OneAOne.ChaveNome(Nome);
}

public sealed class FotoLider
{
    public int Total { get; set; }
    public int EmDia { get; set; }
}

/// <summary>A carteira da coordenação.</summary>
public sealed class BaseOO
{
    public List<PessoaOO> Pessoas { get; set; } = new();
    /// <summary>Lideranças criadas (mesmo sem ninguém ainda).</summary>
    public List<string> Liderancas { get; set; } = new();
    /// <summary>Foto de cada mês (yyyy-MM → liderança → total/em dia), para comparar meses.</summary>
    public Dictionary<string, Dictionary<string, FotoLider>> Fotos { get; set; } = new();
    public string Arquivo { get; set; } = "";
    public string AtualizadoEm { get; set; } = "";

    public IEnumerable<PessoaOO> Ativos => Pessoas.Where(p => !p.Arquivado);
    public List<string> TodasLiderancas()
        => Liderancas.Concat(Ativos.Select(p => p.Lideranca)).Where(x => x != "")
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
}

public enum FaixaOO { EmDia, Chegando, Atrasado }

/// <summary>O que muda quando uma planilha nova é anexada sobre a carteira que já existe.</summary>
public sealed class MudancasOO
{
    public BaseOO Lida { get; set; } = new();
    public List<PessoaOO> Entraram { get; } = new();
    public List<PessoaOO> Sairam { get; } = new();
    public List<(PessoaOO atual, string de, string para)> MudaramLideranca { get; } = new();
    public List<(PessoaOO atual, PessoaOO nova)> Parecidos { get; } = new();
    public List<(PessoaOO atual, PessoaOO nova)> Iguais { get; } = new();
    public int NovosRegistros { get; set; }
    public bool Vazia => Entraram.Count + Sairam.Count + MudaramLideranca.Count + Parecidos.Count + NovosRegistros == 0;
}

public static class OneAOne
{
    public const int CicloNovo = 30, CicloAntigo = 90, Chegando = 14, AposMudanca = 30, NovoViraAntigo = 90;

    public static string ChaveNome(string nome) => string.Join(' ', Ferias.Norm(nome).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    // ======================== regras ========================

    /// <summary>Novo vira Antigo 90 dias depois de entrar (ou do primeiro 1:1 registrado).</summary>
    public static bool EhNovo(PessoaOO p, DateOnly hoje)
    {
        if (!Ferias.Norm(p.Tipo).StartsWith("novo")) return false;
        var ini = p.Entrada ?? (p.Registros.Count > 0 ? p.Registros.Min(r => r.Data) : (DateOnly?)null);
        return ini == null || hoje < ini.Value.AddDays(NovoViraAntigo);
    }

    public static DateOnly? ViraAntigoEm(PessoaOO p)
    {
        if (!Ferias.Norm(p.Tipo).StartsWith("novo")) return null;
        var ini = p.Entrada ?? (p.Registros.Count > 0 ? p.Registros.Min(r => r.Data) : (DateOnly?)null);
        return ini?.AddDays(NovoViraAntigo);
    }

    public static int Ciclo(PessoaOO p, DateOnly hoje) => EhNovo(p, hoje) ? CicloNovo : CicloAntigo;

    /// <summary>Data do próximo 1:1. Quem mudou de liderança ganha um 1:1 em até 30 dias.</summary>
    public static DateOnly? Proximo(PessoaOO p, DateOnly hoje)
    {
        var u = p.Ultimo;
        DateOnly? d = u?.AddDays(Ciclo(p, hoje)) ?? p.Entrada?.AddDays(CicloNovo);
        if (p.MudouEm is DateOnly m && (u == null || u < m))
        {
            var t = m.AddDays(AposMudanca);
            if (d == null || t < d) d = t;
        }
        return d;
    }

    /// <summary>Dias até o próximo 1:1 (negativo = atrasado). Null = nunca teve 1:1 registrado.</summary>
    public static int? Faltam(PessoaOO p, DateOnly hoje) => Proximo(p, hoje) is DateOnly d ? d.DayNumber - hoje.DayNumber : null;

    public static FaixaOO Faixa(PessoaOO p, DateOnly hoje)
    {
        var f = Faltam(p, hoje);
        return f == null || f < 0 ? FaixaOO.Atrasado : f <= Chegando ? FaixaOO.Chegando : FaixaOO.EmDia;
    }

    /// <summary>Quantos 1:1 por semana mantêm todo mundo em dia.</summary>
    public static double RitmoSemanal(BaseOO b, DateOnly hoje) => b.Ativos.Sum(p => 7.0 / Ciclo(p, hoje));

    public static IEnumerable<(PessoaOO p, RegistroOO r)> AcoesAbertas(BaseOO b)
        => b.Ativos.SelectMany(p => p.Registros.Where(r => r.Acao.Trim() != "" && !r.AcaoFeita).Select(r => (p, r)));

    // ======================== mudanças na carteira ========================

    public static void TrocarLideranca(PessoaOO p, string nova, DateOnly hoje)
    {
        nova = nova.Trim();
        if (nova == "" || string.Equals(p.Lideranca, nova, StringComparison.OrdinalIgnoreCase)) return;
        var v = p.Vinculos.LastOrDefault(x => x.Ate == null);
        if (v != null) v.Ate = hoje;
        else if (p.Lideranca != "") p.Vinculos.Add(new VinculoOO { Lideranca = p.Lideranca, Ate = hoje });
        p.Vinculos.Add(new VinculoOO { Lideranca = nova, Desde = hoje });
        p.Lideranca = nova;
        p.MudouEm = hoje;
    }

    public static void Registrar(PessoaOO p, RegistroOO r, bool substituir = false)
    {
        var mesmo = p.Registros.FirstOrDefault(x => x.Data == r.Data);
        if (mesmo != null && substituir) { p.Registros.Remove(mesmo); mesmo = null; }
        if (mesmo != null)
        {
            if (r.Obs != "") mesmo.Obs = mesmo.Obs == "" ? r.Obs : mesmo.Obs + " | " + r.Obs;
            if (r.Acao != "") { mesmo.Acao = r.Acao; mesmo.AcaoPrazo = r.AcaoPrazo; mesmo.AcaoFeita = r.AcaoFeita; }
        }
        else p.Registros.Add(r);
        p.Registros.Sort((a, c) => a.Data.CompareTo(c.Data));
    }

    /// <summary>Guarda a foto do mês atual (por liderança: total e quantos em dia).</summary>
    public static void Fotografar(BaseOO b, DateOnly hoje)
    {
        var f = new Dictionary<string, FotoLider>();
        foreach (var g in b.Ativos.GroupBy(p => p.Lideranca))
            f[g.Key] = new FotoLider { Total = g.Count(), EmDia = g.Count(p => Faixa(p, hoje) == FaixaOO.EmDia) };
        b.Fotos[$"{hoje.Year:0000}-{hoje.Month:00}"] = f;
    }

    static int Distancia(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    /// <summary>Nomes que parecem ser a mesma pessoa (erro de digitação, nome do meio, sobrenome a mais).</summary>
    public static bool Parecido(string a, string b)
    {
        a = ChaveNome(a); b = ChaveNome(b);
        if (a == b) return true;
        if (a.Length > 6 && b.Length > 6 && (a.Contains(b) || b.Contains(a))) return true;
        var pa = a.Split(' '); var pb = b.Split(' ');
        if (pa.Length > 1 && pb.Length > 1 && pa[0] == pb[0] && pa[^1] == pb[^1]) return true;
        return Math.Max(a.Length, b.Length) >= 8 && Distancia(a, b) <= 2;
    }

    public static MudancasOO Comparar(BaseOO atual, BaseOO lida)
    {
        var m = new MudancasOO { Lida = lida };
        var porChave = atual.Pessoas.GroupBy(p => p.Chave).ToDictionary(g => g.Key, g => g.First());
        var usados = new HashSet<PessoaOO>();
        var soltas = new List<PessoaOO>();
        foreach (var n in lida.Pessoas)
        {
            if (porChave.TryGetValue(n.Chave, out var a) && usados.Add(a)) m.Iguais.Add((a, n));
            else soltas.Add(n);
        }
        foreach (var n in soltas)
        {
            var a = atual.Pessoas.FirstOrDefault(x => !usados.Contains(x) && Parecido(x.Nome, n.Nome));
            if (a != null) { usados.Add(a); m.Parecidos.Add((a, n)); }
            else m.Entraram.Add(n);
        }
        m.Sairam.AddRange(atual.Pessoas.Where(p => !p.Arquivado && !usados.Contains(p)));
        foreach (var (a, n) in m.Iguais.Concat(m.Parecidos))
        {
            if (n.Lideranca != "" && !string.Equals(a.Lideranca, n.Lideranca, StringComparison.OrdinalIgnoreCase)) m.MudaramLideranca.Add((a, a.Lideranca, n.Lideranca));
            m.NovosRegistros += n.Registros.Count(r => !a.Registros.Any(x => x.Data == r.Data));
        }
        return m;
    }

    /// <summary>Aplica a planilha nova. Parecidos não confirmados entram como pessoas novas.</summary>
    public static void Aplicar(BaseOO b, MudancasOO m, ICollection<string> parecidosConfirmados, bool arquivarSaidas, DateOnly hoje)
    {
        void Juntar(PessoaOO a, PessoaOO n)
        {
            if (a.Arquivado) { a.Arquivado = false; a.ArquivadoEm = null; }
            if (n.Lideranca != "") TrocarLideranca(a, n.Lideranca, hoje);
            if (n.Tipo != "") a.Tipo = n.Tipo;
            foreach (var r in n.Registros.Where(r => !a.Registros.Any(x => x.Data == r.Data))) Registrar(a, r);
        }
        foreach (var (a, n) in m.Iguais) Juntar(a, n);
        foreach (var (a, n) in m.Parecidos)
        {
            if (parecidosConfirmados.Contains(a.Id)) Juntar(a, n);
            else Entrar(n);
        }
        foreach (var n in m.Entraram) Entrar(n);
        if (arquivarSaidas)
            foreach (var s in m.Sairam) { s.Arquivado = true; s.ArquivadoEm = hoje; }
        foreach (var l in m.Lida.Liderancas)
            if (!b.Liderancas.Contains(l, StringComparer.OrdinalIgnoreCase)) b.Liderancas.Add(l);
        b.Arquivo = m.Lida.Arquivo;
        Fotografar(b, hoje);

        void Entrar(PessoaOO n)
        {
            if (n.Registros.Count == 0) n.Entrada ??= hoje;
            if (n.Vinculos.Count == 0 && n.Lideranca != "") n.Vinculos.Add(new VinculoOO { Lideranca = n.Lideranca, Desde = b.Pessoas.Count == 0 ? null : hoje });
            b.Pessoas.Add(n);
        }
    }

    // ======================== planilha ========================

    /// <summary>Lê a planilha de One a One (a do time ou o modelo do QSmart).</summary>
    public static BaseOO Ler(byte[] dados, string nomeArquivo)
    {
        var b = new BaseOO { Arquivo = nomeArquivo };
        using var ms = new MemoryStream(dados);
        ZipArchive zip;
        try { zip = new ZipArchive(ms, ZipArchiveMode.Read); }
        catch { throw new InvalidDataException("O arquivo não é uma planilha .xlsx válida."); }
        var historico = new List<(string nome, RegistroOO r)>();
        using (zip)
        {
            var shared = FeriasAuto.LerShared(zip.GetEntry("xl/sharedStrings.xml"));
            foreach (var (nome, caminho) in FeriasAuto.LerAbas(zip))
            {
                var e = zip.GetEntry(caminho);
                if (e == null) continue;
                var cel = FeriasAuto.LerCelulas(e, shared);
                if (cel.Count == 0) continue;
                var nn = Ferias.Norm(nome);
                if (nn.StartsWith("como preencher") || nn.StartsWith("arquivados")) continue;
                if (nn.StartsWith("historico")) LerHistorico(cel, historico);
                else LerAba(b, cel);
            }
        }
        if (b.Pessoas.Count == 0)
            throw new InvalidDataException("Não achei o cabeçalho do One a One (Time, Nome, Tipo, Último 1:1). Use o modelo em branco desta aba.");
        var porChave = b.Pessoas.GroupBy(p => p.Chave).ToDictionary(g => g.Key, g => g.First());
        foreach (var (n, r) in historico)
            if (porChave.TryGetValue(ChaveNome(n), out var p)) Registrar(p, r, substituir: true);
        foreach (var p in b.Pessoas)
            if (p.Lideranca != "") p.Vinculos.Add(new VinculoOO { Lideranca = p.Lideranca });
        b.Liderancas = b.Pessoas.Select(p => p.Lideranca).Where(x => x != "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return b;
    }

    static void LerAba(BaseOO b, Dictionary<(int, int), (string? txt, double? num)> cel)
    {
        int cab = -1;
        foreach (var lin in cel.Keys.Select(k => k.Item1).Distinct().OrderBy(x => x).Take(30))
        {
            var t = cel.Where(k => k.Key.Item1 == lin).Select(k => Ferias.Norm(k.Value.txt)).ToList();
            if (t.Any(x => x.StartsWith("nome")) && t.Any(x => x.Contains("ultimo"))) { cab = lin; break; }
        }
        if (cab < 0) return;
        var cols = cel.Where(k => k.Key.Item1 == cab).ToDictionary(k => k.Key.Item2, k => Ferias.Norm(k.Value.txt));
        int Col(Func<string, bool> f) => cols.Where(k => f(k.Value)).Select(k => k.Key).DefaultIfEmpty(-1).Min();
        int cNome = Col(t => t.StartsWith("nome")), cTime = Col(t => t.StartsWith("time") || t.StartsWith("lider")),
            cTipo = Col(t => t.StartsWith("tipo")), cUlt = Col(t => t.Contains("ultimo"));
        if (cNome < 0 || cUlt < 0) return;

        // sub-cabeçalho (Observação / Ação, Data) pode estar na linha de baixo
        int ini = cab + 1;
        var sub = cel.Where(k => k.Key.Item1 == cab + 1).ToDictionary(k => k.Key.Item2, k => Ferias.Norm(k.Value.txt));
        if (sub.Values.Any(t => t.StartsWith("observ")) && Txt(cel, cab + 1, cNome) == "") ini = cab + 2;
        else sub = new();
        int maxCol = cols.Keys.Concat(sub.Keys).DefaultIfEmpty(0).Max();
        var extras = new List<(int col, bool obs)>();
        for (int c = cUlt + 1; c <= maxCol; c++)
        {
            var t = sub.TryGetValue(c, out var s) && s != "" ? s : cols.TryGetValue(c, out var h) ? h : "";
            if (t.StartsWith("observ") || t.Contains("acao") || (t.Contains("observ") && !t.Contains("data"))) extras.Add((c, true));
            else if (t.StartsWith("data")) extras.Add((c, false));
        }
        // pares (data, observação). A primeira observação sem data é do último 1:1.
        var pares = new List<(int data, int obs)>();
        foreach (var (c, obs) in extras.Where(x => x.obs))
        {
            int d = extras.Any(x => !x.obs && x.col == c - 1) ? c - 1 : -1;
            pares.Add((d, c));
        }

        int ultima = cel.Keys.Max(k => k.Item1);
        var vistos = new HashSet<string>();
        for (int l = ini; l <= ultima; l++)
        {
            var nome = Txt(cel, l, cNome);
            if (nome == "") continue;
            if (!vistos.Add(ChaveNome(nome))) continue;
            var tipo = Txt(cel, l, cTipo);
            var p = new PessoaOO
            {
                Nome = string.Join(' ', nome.Split(' ', StringSplitOptions.RemoveEmptyEntries)),
                Lideranca = Txt(cel, l, cTime),
                Tipo = Ferias.Norm(tipo).StartsWith("novo") ? "Novo" : "Antigo",
            };
            var ult = Dt(cel, l, cUlt);
            foreach (var (cd, co) in pares)
            {
                var o = Txt(cel, l, co);
                var d = cd >= 0 ? Dt(cel, l, cd) : ult;
                if (d == null) continue;
                Registrar(p, new RegistroOO { Data = d.Value, Obs = o });
            }
            if (ult != null && !p.Registros.Any(r => r.Data == ult)) Registrar(p, new RegistroOO { Data = ult.Value });
            b.Pessoas.Add(p);
        }
    }

    static void LerHistorico(Dictionary<(int, int), (string? txt, double? num)> cel, List<(string, RegistroOO)> saida)
    {
        int cab = -1;
        foreach (var lin in cel.Keys.Select(k => k.Item1).Distinct().OrderBy(x => x).Take(10))
        {
            var t = cel.Where(k => k.Key.Item1 == lin).Select(k => Ferias.Norm(k.Value.txt)).ToList();
            if (t.Any(x => x.StartsWith("nome")) && t.Any(x => x.StartsWith("data"))) { cab = lin; break; }
        }
        if (cab < 0) return;
        var cols = cel.Where(k => k.Key.Item1 == cab).ToDictionary(k => k.Key.Item2, k => Ferias.Norm(k.Value.txt));
        int Col(Func<string, bool> f) => cols.Where(k => f(k.Value)).Select(k => k.Key).DefaultIfEmpty(-1).Min();
        int cNome = Col(t => t.StartsWith("nome")), cData = Col(t => t.StartsWith("data do")), cObs = Col(t => t.StartsWith("observ")),
            cAcao = Col(t => t.StartsWith("acao")), cPrazo = Col(t => t.StartsWith("prazo")), cFeita = Col(t => t.Contains("feita"));
        if (cData < 0) cData = Col(t => t.StartsWith("data"));
        int ultima = cel.Keys.Max(k => k.Item1);
        for (int l = cab + 1; l <= ultima; l++)
        {
            var n = Txt(cel, l, cNome); var d = Dt(cel, l, cData);
            if (n == "" || d == null) continue;
            saida.Add((n, new RegistroOO
            {
                Data = d.Value, Obs = Txt(cel, l, cObs), Acao = Txt(cel, l, cAcao), AcaoPrazo = Dt(cel, l, cPrazo),
                AcaoFeita = Ferias.Norm(Txt(cel, l, cFeita)).StartsWith("s"),
            }));
        }
    }

    static string Txt(Dictionary<(int, int), (string? txt, double? num)> cel, int l, int c)
        => c >= 0 && cel.TryGetValue((l, c), out var v) ? FeriasAuto.Txt(v) : "";
    static DateOnly? Dt(Dictionary<(int, int), (string? txt, double? num)> cel, int l, int c)
        => c >= 0 && cel.TryGetValue((l, c), out var v) ? FeriasAuto.Data(v) : null;

    public static readonly string[] Cabecalho =
    {
        "Time (liderança)", "Nome", "Tipo", "Último 1:1", "Próximo 1:1", "Status",
        "Observação / Ação", "Data (1:1 anterior)", "Observação / Ação (1:1 anterior)", "Data (1:1 antes desse)", "Observação / Ação (1:1 antes desse)",
    };

    static void Formatar(XlsxWriter.Aba s, int fim)
    {
        for (int c = 0; c < Cabecalho.Length; c++) s.Preencher(0, c, "1F3864", true);
        foreach (var c in new[] { 3, 4, 7, 9 }) s.FormatoData(c, 1, fim - 1);
        s.ValidacaoLista($"C2:C{fim}", new[] { "Novo", "Antigo" }, "Tipo", "Novo: 1:1 a cada 30 dias. Antigo: a cada 90 dias.");
        s.ValidacaoData($"D2:D{fim}", new DateOnly(2000, 1, 1), new DateOnly(2099, 12, 31), "Último 1:1", "Data do último 1:1 (dd/mm/aaaa). Em branco se nunca teve.");
        s.ValidacaoData($"H2:H{fim}", new DateOnly(2000, 1, 1), new DateOnly(2099, 12, 31), "Data", "Data do 1:1 anterior (dd/mm/aaaa).");
        s.ValidacaoData($"J2:J{fim}", new DateOnly(2000, 1, 1), new DateOnly(2099, 12, 31), "Data", "Data do 1:1 antes desse (dd/mm/aaaa).");
        s.CondicionalCor($"F2:F{fim}", "ISNUMBER(SEARCH(\"Atrasado\",$F2))", "F8CBAD", true);
        s.CondicionalCor($"F2:F{fim}", "ISNUMBER(SEARCH(\"Próximo\",$F2))", "FFEB9C");
        s.CondicionalCor($"F2:F{fim}", "ISNUMBER(SEARCH(\"OK\",$F2))", "C6EFCE");
        s.Congelar(1, 2);
        var larg = new double[] { 18, 36, 10, 13, 13, 15, 40, 13, 40, 13, 40 };
        for (int c = 0; c < larg.Length; c++) s.Largura(c, larg[c]);
    }

    static object?[] LinhaFormulas(int r, object? time, object? nome, object? tipo, object? ult, object? o1, object? d2, object? o2, object? d3, object? o3)
        => new object?[]
        {
            time, nome, tipo, ult,
            new Formula($"IF(D{r}=\"\",\"\",IF(C{r}=\"Novo\",D{r}+30,D{r}+90))"),
            new Formula($"IF(E{r}=\"\",\"—\",IF(TODAY()>E{r},\"🔴 Atrasado\",IF(E{r}-TODAY()<=14,\"⚠️ Próximo\",\"✅ OK\")))"),
            o1, d2, o2, d3, o3,
        };

    /// <summary>Planilha em branco no mesmo formato, com o passo a passo de como preencher.</summary>
    public static byte[] Modelo(int linhas = 150)
    {
        var wb = new XlsxWriter();
        var s = wb.Planilha("One a One");
        s.Cabecalho(Cabecalho.Cast<object?>().ToArray());
        for (int i = 0; i < linhas; i++)
        {
            int r = i + 2;
            s.Linha(LinhaFormulas(r, null, null, null, null, null, null, null, null, null));
            foreach (var c in new[] { 0, 1, 2, 3, 6, 7, 8, 9, 10 }) s.Preencher(r - 1, c, "DDEBF7");
        }
        Formatar(s, linhas + 1);
        ComoPreencher(wb);
        return wb.Salvar();
    }

    static void ComoPreencher(XlsxWriter wb)
    {
        var g = wb.Planilha("Como preencher");
        g.Linha("COMO PREENCHER A PLANILHA DE ONE A ONE", "");
        g.Preencher(0, 0, "1F3864", true); g.Preencher(0, 1, "1F3864", true);
        g.Linha("", "");
        g.Linha("Passo", "O que fazer");
        g.NegritoLinha(2);
        var passos = new (string, string)[]
        {
            ("1. Uma linha por pessoa", "Na aba “One a One”, preencha uma linha para cada colaborador da coordenação. Não deixe linhas em branco no meio."),
            ("2. Time (liderança) — coluna A", "Nome da liderança da pessoa (ex.: Mauricio). Escreva sempre do mesmo jeito: o QSmart monta uma esteira para cada liderança."),
            ("3. Nome — coluna B", "Nome completo, igual ao das outras planilhas (férias, compilado). Ajuda o QSmart a reconhecer a mesma pessoa."),
            ("4. Tipo — coluna C", "Escolha na lista: Novo (1:1 a cada 30 dias) ou Antigo (1:1 a cada 90 dias). No QSmart, o Novo vira Antigo sozinho depois de 90 dias."),
            ("5. Último 1:1 — coluna D", "Data do último 1:1 com a pessoa (dd/mm/aaaa). Deixe em branco se ainda não teve: ela aparece como atrasada para entrar na fila."),
            ("6. Próximo 1:1 e Status — colunas E e F", "Não preencha: são automáticas. Próximo = último + 30 ou 90 dias. Status: 🔴 Atrasado, ⚠️ Próximo (até 14 dias) ou ✅ OK."),
            ("7. Observação / Ação — coluna G", "O que foi conversado e a ação combinada no último 1:1 (opcional)."),
            ("8. 1:1 anteriores — colunas H a K", "Se quiser levar o histórico: data e observação do 1:1 anterior (H e I) e do 1:1 antes desse (J e K). Opcional."),
            ("9. Não mude as colunas", "Não troque a ordem nem o nome das colunas do cabeçalho."),
            ("10. Anexe no QSmart", "Coordenação → One a One → “Anexar planilha”. Na primeira vez a carteira é criada. Nas próximas, o QSmart mostra o que mudou (quem entrou, quem saiu, quem mudou de liderança, nomes parecidos) e só aplica quando você confirmar."),
            ("11. Depois, use o QSmart", "Registre cada 1:1 direto no QSmart (“1:1 feito”). Para levar os dados para outra planilha ou passar para outra coordenação, use “Exportar planilha”: ela sai neste mesmo formato, com uma aba Histórico."),
            ("Privacidade", "A planilha é lida no seu navegador e não vai para nenhum servidor. No QSmart os dados ficam guardados criptografados."),
        };
        foreach (var (a, b2) in passos) g.Linha(a, b2);
        g.Linha("", "");
        g.Linha("Exemplo (pessoas fictícias)", "");
        g.NegritoLinha(passos.Length + 4);
        int ex = passos.Length + 6;
        g.Linha(Cabecalho.Take(7).Cast<object?>().ToArray());
        g.NegritoLinha(ex - 1);
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        g.Linha(LinhaFormulas(ex + 1, "Liderança A", "Ana Exemplo Souza", "Antigo", hoje.AddDays(-40), "Combinado treino de atendimento", null, null, null, null).Take(7).ToArray());
        g.Linha(LinhaFormulas(ex + 2, "Liderança A", "Bruno Exemplo Lima", "Novo", hoje.AddDays(-35), "Primeiro mês: acompanhar metas", null, null, null, null).Take(7).ToArray());
        g.Linha(LinhaFormulas(ex + 3, "Liderança B", "Carla Exemplo Dias", "Antigo", null, "", null, null, null, null).Take(7).ToArray());
        foreach (var c in new[] { 3, 4 }) g.FormatoData(c, ex, ex + 2);
        g.Largura(0, 36); g.Largura(1, 110);
        for (int c = 2; c < 7; c++) g.Largura(c, 16);
    }

    /// <summary>A carteira em planilha: mesmo formato do modelo + aba Histórico com todos os 1:1 (e uma de Arquivados).</summary>
    public static byte[] Exportar(BaseOO b)
    {
        var wb = new XlsxWriter();
        var s = wb.Planilha("One a One");
        s.Cabecalho(Cabecalho.Cast<object?>().ToArray());
        int r = 2;
        foreach (var p in b.Ativos.OrderBy(p => p.Lideranca, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase))
        {
            var regs = p.Registros.OrderByDescending(x => x.Data).ToList();
            string Txt(RegistroOO? x) => x == null ? "" : x.Acao == "" ? x.Obs : x.Obs == "" ? "Ação: " + x.Acao : $"{x.Obs} | Ação: {x.Acao}";
            RegistroOO? R(int i) => i < regs.Count ? regs[i] : null;
            s.Linha(LinhaFormulas(r, p.Lideranca, p.Nome, Ferias.Norm(p.Tipo).StartsWith("novo") ? "Novo" : "Antigo", R(0)?.Data,
                Txt(R(0)), R(1)?.Data, Txt(R(1)), R(2)?.Data, Txt(R(2))));
            r++;
        }
        Formatar(s, Math.Max(r, 3));

        var h = wb.Planilha("Histórico");
        h.Cabecalho("Time (liderança)", "Nome", "Data do 1:1", "Observação", "Ação combinada", "Prazo da ação", "Ação feita?");
        h.NegritoLinha(0);
        int n = 1;
        foreach (var p in b.Ativos.OrderBy(p => p.Lideranca).ThenBy(p => p.Nome))
            foreach (var x in p.Registros.OrderByDescending(x => x.Data))
            {
                h.Linha(p.Lideranca, p.Nome, x.Data, x.Obs, x.Acao, x.AcaoPrazo, x.Acao == "" ? "" : x.AcaoFeita ? "Sim" : "Não");
                n++;
            }
        h.FormatoData(2, 1, Math.Max(1, n - 1)); h.FormatoData(5, 1, Math.Max(1, n - 1));
        h.Congelar(1, 2);
        foreach (var (c, w) in new[] { (0, 18.0), (1, 36.0), (2, 13.0), (3, 50.0), (4, 40.0), (5, 13.0), (6, 11.0) }) h.Largura(c, w);

        var arq = b.Pessoas.Where(p => p.Arquivado).ToList();
        if (arq.Count > 0)
        {
            var a = wb.Planilha("Arquivados");
            a.Cabecalho("Time (liderança)", "Nome", "Arquivado em", "Último 1:1");
            a.NegritoLinha(0);
            foreach (var p in arq) a.Linha(p.Lideranca, p.Nome, p.ArquivadoEm, p.Ultimo);
            a.FormatoData(2, 1, arq.Count); a.FormatoData(3, 1, arq.Count);
            a.Largura(0, 18); a.Largura(1, 36); a.Largura(2, 14); a.Largura(3, 14);
        }
        ComoPreencher(wb);
        return wb.Salvar();
    }

    // ======================== gravação ========================

    static readonly JsonSerializerOptions Js = new() { WriteIndented = false };
    public static byte[] ParaBytes(BaseOO b) => JsonSerializer.SerializeToUtf8Bytes(b, Js);
    public static BaseOO? DeBytes(byte[] d)
    {
        try { return JsonSerializer.Deserialize<BaseOO>(d, Js); } catch { return null; }
    }
}
