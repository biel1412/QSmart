using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using QSmart.Core.Planilhas;

namespace QSmart.Core.Coordenacao;

/// <summary>Um pedido de férias lido da planilha de coleta.</summary>
public sealed class PeriodoFerias
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Colaborador { get; set; } = "";
    public string Equipe { get; set; } = "";
    public DateOnly Inicio { get; set; }
    public DateOnly Fim { get; set; }
    /// <summary>Quer vender dias (abono pecuniário).</summary>
    public bool Vender { get; set; }
    /// <summary>Linha da planilha de onde veio (para achar o pedido no Excel).</summary>
    public int Linha { get; set; }

    [JsonIgnore] public int Dias => Fim.DayNumber - Inicio.DayNumber + 1;
    /// <summary>Primeiro dia útil depois das férias.</summary>
    [JsonIgnore] public DateOnly Retorno => Ferias.ProximoDiaUtil(Fim.AddDays(1));
}

/// <summary>Última planilha de férias anexada (fica só neste navegador) e as regras da comparação.</summary>
public sealed class DadosFerias
{
    public int Versao { get; set; } = 2;
    public List<PeriodoFerias> Periodos { get; set; } = new();
    /// <summary>Linhas da planilha que não puderam ser lidas.</summary>
    public List<string> Problemas { get; set; } = new();
    public string Arquivo { get; set; } = "";
    public string CarregadoEm { get; set; } = "";
    /// <summary>Intervalo (dias livres) abaixo do qual as datas são “próximas”.</summary>
    public int DistanciaMinima { get; set; } = 7;
    /// <summary>Intervalo abaixo do qual as datas são “muito próximas”.</summary>
    public int MuitoProxima { get; set; } = 3;
    /// <summary>Quantos dias à frente contam como “sai em breve”.</summary>
    public int EmBreve { get; set; } = 30;
    /// <summary>Compara só colaboradores do mesmo time.</summary>
    public bool PorEquipe { get; set; } = true;
}

public enum NivelFerias { Ok = 0, Proximo = 1, MuitoProximo = 2, Choque = 3 }

/// <summary>Alerta entre dois pedidos: choque (dias em comum) ou datas próximas/muito próximas.</summary>
public sealed record AlertaFerias(NivelFerias Nivel, PeriodoFerias A, PeriodoFerias B, int Dias, DateOnly? De, DateOnly? Ate)
{
    public bool MesmaPessoa => Ferias.Norm(A.Colaborador) == Ferias.Norm(B.Colaborador);
    public string Titulo => Nivel switch
    {
        NivelFerias.Choque => MesmaPessoa ? "Pedidos sobrepostos" : "Choque de datas",
        NivelFerias.MuitoProximo => "Datas muito próximas",
        _ => "Datas próximas",
    };
    public string Texto => Nivel == NivelFerias.Choque
        ? (MesmaPessoa
            ? $"{A.Colaborador} tem dois pedidos que se sobrepõem em {Ferias.Plural(Dias, "dia")} ({Core.Texto.Dm(De!.Value)} a {Core.Texto.Dm(Ate!.Value)})."
            : $"{A.Colaborador} e {B.Colaborador} ficam fora ao mesmo tempo por {Ferias.Plural(Dias, "dia")} ({Core.Texto.Dm(De!.Value)} a {Core.Texto.Dm(Ate!.Value)}).")
        : Dias == 0
            ? $"{B.Colaborador} sai no dia seguinte à volta de {A.Colaborador} ({Core.Texto.Dm(A.Fim)} → {Core.Texto.Dm(B.Inicio)}), sem nenhum dia de intervalo."
            : $"Só {Ferias.Plural(Dias, "dia")} entre a volta de {A.Colaborador} ({Core.Texto.Dm(A.Fim)}) e a saída de {B.Colaborador} ({Core.Texto.Dm(B.Inicio)}).";
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DadosFerias))]
internal partial class JsonFerias : JsonSerializerContext { }

public static class Ferias
{
    public const int MinimoClt = 5, MaiorParteClt = 14, DiasPorAno = 30, MaxVenda = 10, LinhasModelo = 300;
    public static readonly string[] ColunasModelo = { "Usuário", "Time", "Data de início", "Total de dias", "Deseja vender?" };

    public static string Norm(string? s) => Core.Texto.NormalizarBusca(s ?? "");
    public static string Plural(int n, string palavra) => $"{n} {palavra}{(n == 1 ? "" : "s")}";

    public static DateOnly ProximoDiaUtil(DateOnly d)
    {
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1);
        return d;
    }

    public static string NomeNivel(NivelFerias n) => n switch
    {
        NivelFerias.Choque => "Choque", NivelFerias.MuitoProximo => "Muito próximas", NivelFerias.Proximo => "Próximas", _ => "OK",
    };

    static bool MesmoGrupo(PeriodoFerias a, PeriodoFerias b, bool porEquipe)
        => !porEquipe || Norm(a.Equipe) == Norm(b.Equipe);

    /// <summary>Todos os choques e proximidades entre os pedidos (cada par uma vez; A começa antes).</summary>
    public static List<AlertaFerias> Alertas(IEnumerable<PeriodoFerias> periodos, int distanciaMinima, int muitoProxima, bool porEquipe)
    {
        var ps = periodos.Where(p => p.Fim >= p.Inicio).OrderBy(p => p.Inicio).ThenBy(p => p.Fim).ThenBy(p => p.Colaborador, StringComparer.Ordinal).ToList();
        var r = new List<AlertaFerias>();
        for (int i = 0; i < ps.Count; i++)
            for (int j = i + 1; j < ps.Count; j++)
            {
                var a = ps[i]; var b = ps[j];
                if (b.Inicio.DayNumber - a.Fim.DayNumber - 1 >= Math.Max(distanciaMinima, muitoProxima) && b.Inicio > a.Fim) continue;
                bool mesma = Norm(a.Colaborador) == Norm(b.Colaborador);
                if (!mesma && !MesmoGrupo(a, b, porEquipe)) continue;
                var de = a.Inicio > b.Inicio ? a.Inicio : b.Inicio;
                var ate = a.Fim < b.Fim ? a.Fim : b.Fim;
                if (ate >= de)
                {
                    r.Add(new AlertaFerias(NivelFerias.Choque, a, b, ate.DayNumber - de.DayNumber + 1, de, ate));
                    continue;
                }
                if (mesma) continue;                                   // a mesma pessoa pode emendar períodos
                var antes = a.Fim < b.Inicio ? a : b;
                var depois = antes == a ? b : a;
                int intervalo = depois.Inicio.DayNumber - antes.Fim.DayNumber - 1;
                if (intervalo < muitoProxima) r.Add(new AlertaFerias(NivelFerias.MuitoProximo, antes, depois, intervalo, null, null));
                else if (intervalo < distanciaMinima) r.Add(new AlertaFerias(NivelFerias.Proximo, antes, depois, intervalo, null, null));
            }
        return r.OrderByDescending(x => x.Nivel).ThenBy(x => x.A.Inicio).ToList();
    }

    public static List<AlertaFerias> Alertas(DadosFerias d) => Alertas(d.Periodos, d.DistanciaMinima, d.MuitoProxima, d.PorEquipe);

    public static NivelFerias Nivel(PeriodoFerias p, IEnumerable<AlertaFerias> alertas)
    {
        var n = NivelFerias.Ok;
        foreach (var a in alertas)
            if ((a.A.Id == p.Id || a.B.Id == p.Id) && a.Nivel > n) n = a.Nivel;
        return n;
    }

    /// <summary>Lembretes da CLT por colaborador (não impedem nada, só avisam).</summary>
    public static List<(PeriodoFerias p, string texto)> AvisosClt(IEnumerable<PeriodoFerias> todos)
    {
        var r = new List<(PeriodoFerias, string)>();
        foreach (var g in todos.GroupBy(p => (Norm(p.Colaborador), p.Inicio.Year)))
        {
            var ps = g.OrderBy(p => p.Inicio).ToList();
            var p0 = ps[0];
            foreach (var p in ps)
            {
                if (p.Dias < MinimoClt) r.Add((p, $"{p.Colaborador}: pedido de {Plural(p.Dias, "dia")} em {Core.Texto.Dm(p.Inicio)}. Pela CLT, cada período deve ter pelo menos {MinimoClt} dias."));
                if (p.Inicio.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday)
                    r.Add((p, $"{p.Colaborador}: começa numa {Core.Texto.DiaSemana(p.Inicio).ToLowerInvariant()} ({Core.Texto.Dm(p.Inicio)}). As férias não podem começar nos 2 dias antes do descanso semanal ou de feriado."));
            }
            int dias = ps.Sum(p => p.Dias);
            bool vende = ps.Any(p => p.Vender);
            if (dias > DiasPorAno) r.Add((p0, $"{p0.Colaborador} pediu {dias} dias em {g.Key.Year} (o normal é até {DiasPorAno})."));
            else if (vende && dias > DiasPorAno - MaxVenda) r.Add((p0, $"{p0.Colaborador} quer vender e pediu {dias} dias em {g.Key.Year}. Quem vende {MaxVenda} dias tira no máximo {DiasPorAno - MaxVenda}."));
            if (ps.Count > 3) r.Add((p0, $"{p0.Colaborador} tem {ps.Count} pedidos em {g.Key.Year}. A CLT permite dividir as férias em até 3 períodos."));
            if (ps.Count > 1 && ps.All(p => p.Dias < MaiorParteClt)) r.Add((p0, $"{p0.Colaborador} dividiu as férias de {g.Key.Year} e nenhum período tem {MaiorParteClt} dias ou mais (exigência da CLT)."));
        }
        return r;
    }

    public static string Gravar(DadosFerias d) => JsonSerializer.Serialize(d, JsonFerias.Default.DadosFerias);

    public static DadosFerias Ler(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new DadosFerias();
        var d = JsonSerializer.Deserialize(json, JsonFerias.Default.DadosFerias) ?? new DadosFerias();
        if (d.Versao < 2) d = new DadosFerias { DistanciaMinima = d.DistanciaMinima, PorEquipe = d.PorEquipe };   // cadastro manual antigo: começa do zero
        d.Periodos ??= new();
        d.Problemas ??= new();
        d.DistanciaMinima = Math.Clamp(d.DistanciaMinima, 0, 365);
        d.MuitoProxima = Math.Clamp(d.MuitoProxima, 0, d.DistanciaMinima);
        d.EmBreve = Math.Clamp(d.EmBreve, 1, 365);
        return d;
    }

    // ================= modelo de coleta =================

    /// <summary>Planilha em branco para a equipe preencher: Usuário, Time, Data de início, Total de dias, Deseja vender?</summary>
    public static byte[] Modelo()
    {
        var wb = new XlsxWriter();
        var s = wb.Planilha("Férias");
        s.Cabecalho(ColunasModelo.Cast<object?>().ToArray());
        for (int c = 0; c < ColunasModelo.Length; c++) s.Preencher(0, c, "CCEFEA", true);
        int fim = LinhasModelo + 1;
        for (int i = 0; i < LinhasModelo; i++) s.Linha(new object?[ColunasModelo.Length]);
        s.FormatoData(2, 1, LinhasModelo);
        s.ValidacaoData($"C2:C{fim}", new DateOnly(2000, 1, 1), new DateOnly(2099, 12, 31), "Data de início", "Primeiro dia das férias, no formato dd/mm/aaaa.");
        s.ValidacaoInteiro($"D2:D{fim}", 1, DiasPorAno, "Total de dias", $"Quantos dias de férias (de 1 a {DiasPorAno}). Ex.: 30, 20, 15, 10.");
        s.ValidacaoLista($"E2:E{fim}", new[] { "Sim", "Não" }, "Deseja vender?", "Sim, se quer vender dias (abono). Não, se não quer.");
        s.Congelar(1, 0);
        s.Largura(0, 30); s.Largura(1, 22); s.Largura(2, 16); s.Largura(3, 14); s.Largura(4, 16);

        var a = wb.Planilha("Como preencher");
        a.Cabecalho("Coluna", "O que colocar", "Exemplo");
        a.NegritoLinha(0);
        a.Linha("Usuário", "Seu usuário ou nome, igual em todos os pedidos.", "MARIA.SILVA");
        a.Linha("Time", "Nome do time. Todos do mesmo time devem escrever igual.", "Inspeção");
        a.Linha("Data de início", "Primeiro dia das férias (dd/mm/aaaa). Evite sexta, sábado e domingo.", "06/04/2027");
        a.Linha("Total de dias", $"Quantos dias de férias, de {MinimoClt} a {DiasPorAno}. Quem vende {MaxVenda} dias tira até {DiasPorAno - MaxVenda}.", "20");
        a.Linha("Deseja vender?", "Sim ou Não (escolha na lista).", "Sim");
        a.Linha("", "", "");
        a.Linha("Dividir as férias", "Use uma linha para cada período (até 3). Um deles precisa ter 14 dias ou mais.", "");
        a.Linha("Depois de preencher", "Salve o arquivo e devolva para a coordenação, que anexa no QSmart (Coordenação › Férias).", "");
        a.Largura(0, 22); a.Largura(1, 90); a.Largura(2, 16);
        return wb.Salvar();
    }

    // ================= leitura da planilha preenchida =================

    static int Coluna(List<string> nomes, params string[] chaves)
    {
        for (int i = 0; i < nomes.Count; i++)
            if (chaves.Any(k => nomes[i].Contains(k))) return i;
        return -1;
    }

    static DateOnly? Data(Celula c)
    {
        switch (c.Tipo)
        {
            case TipoCelula.Data: return DateOnly.FromDateTime(c.Data);
            case TipoCelula.Inteiro or TipoCelula.Numero:
                return c.Numero is > 20000 and < 80000 && LeitorXlsx.DeSerial(c.Numero) is DateTime dt ? DateOnly.FromDateTime(dt) : null;
            case TipoCelula.Texto:
                var t = c.Texto!.Trim();
                string[] fmts = { "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy", "yyyy-MM-dd HH:mm:ss" };
                return DateOnly.TryParseExact(t.Length > 10 && t[4] == '-' ? t[..10] : t, fmts, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
            default: return null;
        }
    }

    static int? Inteiro(Celula c)
    {
        if (c.Tipo is TipoCelula.Inteiro or TipoCelula.Numero) return Math.Abs(c.Numero % 1) < 1e-9 ? (int)c.Numero : null;
        if (c.Tipo == TipoCelula.Texto && int.TryParse(c.Texto!.Trim().Split(' ')[0], NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return n;
        return null;
    }

    static bool? SimNao(Celula c)
    {
        if (c.EhVazia) return false;
        if (c.Tipo == TipoCelula.Bool) return c.Numero != 0;
        var t = Norm(c.Texto);
        if (t is "sim" or "s" or "x" or "1" or "yes" or "verdadeiro" or "true" || t.StartsWith("sim")) return true;
        if (t is "nao" or "n" or "0" or "no" or "falso" or "false" or "-" || t.StartsWith("nao")) return false;
        return null;
    }

    /// <summary>Lê a planilha de coleta preenchida. Linhas com problema ficam de fora e entram na lista de problemas.</summary>
    public static async Task<(List<PeriodoFerias> periodos, List<string> problemas)> LerPlanilha(byte[] dados)
    {
        var folhas = await LeitorXlsx.Ler(dados);
        Folha? f = null;
        int cu = -1, ct = -1, cd = -1, cn = -1, cv = -1;
        foreach (var fo in folhas)
        {
            var nomes = fo.Colunas.Select(Norm).ToList();
            cu = Coluna(nomes, "usuario", "colaborador", "nome", "login");
            cd = Coluna(nomes, "inicio", "data", "saida");
            cn = Coluna(nomes, "total", "dias", "quantidade", "qtd");
            if (cu >= 0 && cd >= 0 && cn >= 0) { f = fo; break; }
        }
        if (f == null)
            throw new InvalidDataException("Não achei as colunas do modelo (Usuário, Time, Data de início, Total de dias, Deseja vender?). Use a planilha baixada em “Baixar modelo”.");
        var ns = f.Colunas.Select(Norm).ToList();
        ct = Coluna(ns, "time", "equipe", "setor", "squad");
        cv = Coluna(ns, "vender", "venda", "abono");

        var periodos = new List<PeriodoFerias>();
        var problemas = new List<string>();
        for (int i = 0; i < f.N; i++)
        {
            int lin = i + 2;
            var u = f.Valor(i, cu); var d = f.Valor(i, cd); var n = f.Valor(i, cn);
            var t = ct >= 0 ? f.Valor(i, ct) : Celula.Vazia;
            var v = cv >= 0 ? f.Valor(i, cv) : Celula.Vazia;
            if (u.EhVazia && d.EhVazia && n.EhVazia && t.EhVazia && v.EhVazia) continue;
            var nome = (u.Texto ?? "").Trim();
            if (nome == "") { problemas.Add($"Linha {lin}: sem usuário."); continue; }
            var ini = Data(d);
            if (ini == null) { problemas.Add($"Linha {lin} ({nome}): data de início {(d.EhVazia ? "vazia" : $"inválida (“{d.Texto}”)")}."); continue; }
            var dias = Inteiro(n);
            if (dias is null or < 1 or > 60) { problemas.Add($"Linha {lin} ({nome}): total de dias {(n.EhVazia ? "vazio" : $"inválido (“{n.Texto}”)")}."); continue; }
            var vende = SimNao(v);
            if (vende == null) problemas.Add($"Linha {lin} ({nome}): “Deseja vender?” com “{v.Texto}”; considerei Não.");
            periodos.Add(new PeriodoFerias
            {
                Colaborador = nome, Equipe = (t.Texto ?? "").Trim(), Inicio = ini.Value, Fim = ini.Value.AddDays(dias.Value - 1),
                Vender = vende ?? false, Linha = lin,
            });
        }
        if (periodos.Count == 0 && problemas.Count == 0)
            throw new InvalidDataException("A planilha está vazia: nenhum pedido de férias preenchido.");
        return (periodos, problemas);
    }

    // ================= relatório =================

    /// <summary>Planilha com a análise: pedidos e situação, alertas, quem vende, quem sai em breve e pontos de atenção.</summary>
    public static byte[] Exportar(DadosFerias d, DateOnly hoje)
    {
        var al = Alertas(d);
        string Cor(NivelFerias n) => n switch { NivelFerias.Choque => "FDECEC", NivelFerias.MuitoProximo => "FDE3D0", _ => "FEF3E2" };
        var wb = new XlsxWriter();
        var s = wb.Planilha("Pedidos");
        s.Cabecalho("Usuário", "Time", "Início", "Fim", "Dias", "Volta em", "Deseja vender?", "Sai em (dias)", "Situação", "Linha na planilha");
        foreach (var p in d.Periodos.OrderBy(x => x.Inicio).ThenBy(x => x.Colaborador, StringComparer.Ordinal))
        {
            var n = Nivel(p, al);
            int falta = p.Inicio.DayNumber - hoje.DayNumber;
            s.Linha(p.Colaborador, p.Equipe, p.Inicio, p.Fim, p.Dias, p.Retorno, p.Vender ? "Sim" : "Não",
                falta >= 0 ? falta : (p.Fim >= hoje ? "em férias" : "já passou"), NomeNivel(n), p.Linha);
            if (n != NivelFerias.Ok) s.Preencher(s.N - 1, 8, Cor(n), true);
            if (p.Vender) s.Preencher(s.N - 1, 6, "E3F5E8", true);
        }
        s.Congelar(1, 1);
        s.AutoLargura(10, 40);

        var a = wb.Planilha("Alertas");
        a.Cabecalho("Tipo", "Time", "Usuário 1", "Usuário 2", "Dias", "Detalhe");
        foreach (var x in al)
        {
            a.Linha(x.Titulo, x.A.Equipe == x.B.Equipe ? x.A.Equipe : $"{x.A.Equipe} / {x.B.Equipe}", x.A.Colaborador, x.B.Colaborador, x.Dias, x.Texto);
            a.Preencher(a.N - 1, 0, Cor(x.Nivel), true);
        }
        a.AutoLargura(10, 90);

        var v = wb.Planilha("Querem vender");
        v.Cabecalho("Usuário", "Time", "Início", "Dias");
        foreach (var p in d.Periodos.Where(p => p.Vender).OrderBy(p => p.Inicio)) v.Linha(p.Colaborador, p.Equipe, p.Inicio, p.Dias);
        v.AutoLargura(10, 40);

        var b = wb.Planilha("Saem em breve");
        b.Cabecalho("Usuário", "Time", "Início", "Faltam (dias)", "Dias de férias");
        foreach (var p in d.Periodos.Where(p => p.Inicio >= hoje && p.Inicio.DayNumber - hoje.DayNumber <= d.EmBreve).OrderBy(p => p.Inicio))
            b.Linha(p.Colaborador, p.Equipe, p.Inicio, p.Inicio.DayNumber - hoje.DayNumber, p.Dias);
        b.AutoLargura(10, 40);

        var at = wb.Planilha("Pontos de atenção");
        at.Cabecalho("Tipo", "Detalhe");
        foreach (var x in d.Problemas) at.Linha("Linha ignorada / ajustada", x);
        foreach (var (_, x) in AvisosClt(d.Periodos)) at.Linha("Regra da CLT", x);
        at.AutoLargura(10, 110);

        var so = wb.Planilha("Sobre");
        so.Cabecalho("Item", "Valor");
        so.Linha("Planilha analisada", d.Arquivo);
        so.Linha("Carregada em", d.CarregadoEm);
        so.Linha("Pedidos lidos", d.Periodos.Count);
        so.Linha("Datas muito próximas: intervalo menor que (dias)", d.MuitoProxima);
        so.Linha("Datas próximas: intervalo menor que (dias)", d.DistanciaMinima);
        so.Linha("Comparar só dentro do mesmo time", d.PorEquipe ? "Sim" : "Não");
        so.Linha("“Em breve” = próximos (dias)", d.EmBreve);
        so.Linha("Gerado em", Core.Texto.Agora());
        so.AutoLargura(10, 60);
        return wb.Salvar();
    }
}
