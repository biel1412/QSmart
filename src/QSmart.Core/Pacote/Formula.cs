using System.Globalization;
using System.Text;

namespace QSmart.Core.Pacote;

/// <summary>Fórmulas do Smart Math: leitura (em português, como o Excel brasileiro), cálculo, tradução para o .xlsx e ajuste de referências.</summary>
public static class Formula
{
    // =================================================================== tokens
    public enum T { Num, Txt, Bool, Erro, Ref, Func, Op, Abre, Fecha, Sep, Espaco, Outro }

    public sealed class Tok
    {
        public T Tipo; public string Texto = "";
        // referência: folha opcional e 1 ou 2 pontas (faixa); colunas inteiras (A:A) têm Linha = -1
        public string? Folha; public bool FolhaAspas;
        public Ponta A, B; public bool Faixa;
    }

    public struct Ponta { public int L, C; public bool AbsL, AbsC; public bool SoColuna, SoLinha; }

    static readonly string[] Erros = { "#DIV/0!", "#VALOR!", "#VALUE!", "#REF!", "#NOME?", "#NAME?", "#N/D", "#N/A", "#NÚM!", "#NUM!", "#NULO!", "#NULL!", "#CIRC!" };

    public static List<Tok> Tokens(string f, bool ingles = false)
    {
        var l = new List<Tok>();
        int i = 0, n = f.Length;
        char sep = ingles ? ',' : ';';
        while (i < n)
        {
            char ch = f[i];
            if (char.IsWhiteSpace(ch)) { int j = i; while (j < n && char.IsWhiteSpace(f[j])) j++; l.Add(new Tok { Tipo = T.Espaco, Texto = f[i..j] }); i = j; continue; }
            if (ch == '"')
            {
                int j = i + 1; var sb = new StringBuilder("\"");
                while (j < n) { if (f[j] == '"') { if (j + 1 < n && f[j + 1] == '"') { sb.Append("\"\""); j += 2; continue; } j++; break; } sb.Append(f[j]); j++; }
                l.Add(new Tok { Tipo = T.Txt, Texto = sb.Append('"').ToString() }); i = j; continue;
            }
            if (ch == '#')
            {
                var e = Erros.FirstOrDefault(x => string.Compare(f, i, x, 0, x.Length, StringComparison.OrdinalIgnoreCase) == 0);
                if (e != null) { l.Add(new Tok { Tipo = T.Erro, Texto = f.Substring(i, e.Length) }); i += e.Length; continue; }
            }
            if (ch == '(') { l.Add(new Tok { Tipo = T.Abre, Texto = "(" }); i++; continue; }
            if (ch == ')') { l.Add(new Tok { Tipo = T.Fecha, Texto = ")" }); i++; continue; }
            if (ch == sep || (!ingles && ch == ',' && !(i > 0 && char.IsDigit(f[i - 1]) && i + 1 < n && char.IsDigit(f[i + 1]))))
            { l.Add(new Tok { Tipo = T.Sep, Texto = ch.ToString() }); i++; continue; }
            if ("+-*/^&%".Contains(ch)) { l.Add(new Tok { Tipo = T.Op, Texto = ch.ToString() }); i++; continue; }
            if (ch is '<' or '>' or '=')
            {
                var op = i + 1 < n && (f[i + 1] == '=' || (ch == '<' && f[i + 1] == '>')) ? f.Substring(i, 2) : ch.ToString();
                l.Add(new Tok { Tipo = T.Op, Texto = op }); i += op.Length; continue;
            }
            if (char.IsDigit(ch) || ((ch == '.' || ch == ',') && i + 1 < n && char.IsDigit(f[i + 1])))
            {
                // número (ou linha inteira "1:3")
                int j = i; bool dec = false;
                while (j < n && (char.IsDigit(f[j]) || (!dec && (ingles ? f[j] == '.' : (f[j] == ',' || f[j] == '.')) && j + 1 < n && char.IsDigit(f[j + 1])))) { if (!char.IsDigit(f[j])) dec = true; j++; }
                if (j < n && (f[j] == 'e' || f[j] == 'E') && j + 1 < n && (char.IsDigit(f[j + 1]) || ((f[j + 1] == '+' || f[j + 1] == '-') && j + 2 < n && char.IsDigit(f[j + 2]))))
                { j += 2; while (j < n && char.IsDigit(f[j])) j++; }
                if (!dec && j < n && f[j] == ':' && TentarRef(f, i, ingles, out var tl, out var fim)) { l.Add(tl); i = fim; continue; }
                l.Add(new Tok { Tipo = T.Num, Texto = f[i..j] }); i = j; continue;
            }
            if (char.IsLetter(ch) || ch == '$' || ch == '_' || ch == '\'')
            {
                if (TentarRef(f, i, ingles, out var tr, out var fimR) && !(fimR < n && f[fimR] == '(')) { l.Add(tr); i = fimR; continue; }
                int j = i;
                while (j < n && (char.IsLetterOrDigit(f[j]) || f[j] == '.' || f[j] == '_')) j++;
                if (j == i) { l.Add(new Tok { Tipo = T.Outro, Texto = ch.ToString() }); i++; continue; }
                var nome = f[i..j];
                int k = j; while (k < n && f[k] == ' ') k++;
                if (k < n && f[k] == '(') l.Add(new Tok { Tipo = T.Func, Texto = nome });
                else if (nome.ToUpperInvariant() is "VERDADEIRO" or "FALSO" or "TRUE" or "FALSE") l.Add(new Tok { Tipo = T.Bool, Texto = nome });
                else l.Add(new Tok { Tipo = T.Outro, Texto = nome });
                i = j; continue;
            }
            l.Add(new Tok { Tipo = T.Outro, Texto = ch.ToString() }); i++;
        }
        return l;
    }

    /// <summary>Tenta ler uma referência a partir de i: [Folha!]A1[:B2], A:B, 1:3.</summary>
    static bool TentarRef(string f, int i, bool ingles, out Tok t, out int fim)
    {
        t = new Tok { Tipo = T.Ref }; fim = i;
        int j = i;
        string? folha = null; bool aspas = false;
        // folha
        if (f[j] == '\'')
        {
            int k = j + 1; var sb = new StringBuilder();
            while (k < f.Length) { if (f[k] == '\'') { if (k + 1 < f.Length && f[k + 1] == '\'') { sb.Append('\''); k += 2; continue; } break; } sb.Append(f[k]); k++; }
            if (k + 1 >= f.Length || f[k] != '\'' || f[k + 1] != '!') return false;
            folha = sb.ToString(); aspas = true; j = k + 2;
        }
        else
        {
            int k = j; while (k < f.Length && (char.IsLetterOrDigit(f[k]) || f[k] == '_' || f[k] == '.')) k++;
            if (k < f.Length && f[k] == '!' && k > j) { folha = f[j..k]; j = k + 1; }
        }
        if (!LerPonta(f, ref j, out var a)) return false;
        var b = a; bool faixa = false;
        if (j < f.Length && f[j] == ':')
        {
            int k = j + 1;
            if (LerPonta(f, ref k, out var b2)) { b = b2; faixa = true; j = k; }
            else if (a.SoColuna || a.SoLinha) return false;
        }
        if ((a.SoColuna || a.SoLinha) && !faixa) return false;
        if (faixa && (a.SoColuna != b.SoColuna || a.SoLinha != b.SoLinha)) return false;
        // não pode emendar em letra/dígito (ex.: "A1B")
        if (j < f.Length && (char.IsLetterOrDigit(f[j]) || f[j] == '_' || f[j] == '.')) return false;
        t.Folha = folha; t.FolhaAspas = aspas; t.A = a; t.B = b; t.Faixa = faixa; t.Texto = f[i..j];
        fim = j;
        return true;
    }

    static bool LerPonta(string f, ref int j, out Ponta p)
    {
        p = new Ponta();
        int k = j;
        bool absC = false, absL = false;
        if (k < f.Length && f[k] == '$') { absC = true; k++; }
        int ini = k;
        while (k < f.Length && k - ini < 3 && char.IsAsciiLetter(f[k])) k++;
        var col = f[ini..k];
        if (col.Length > 0 && k < f.Length && char.IsAsciiLetter(f[k])) return false;
        if (k < f.Length && f[k] == '$') { absL = true; k++; }
        int ini2 = k;
        while (k < f.Length && char.IsDigit(f[k]) && k - ini2 < 7) k++;
        var lin = f[ini2..k];
        if (col.Length == 0 && lin.Length == 0) return false;
        if (col.Length == 0) { if (absC) { absL = true; } if (!int.TryParse(lin, out var nl) || nl < 1) return false; p = new Ponta { L = nl - 1, C = -1, AbsL = absL || absC, SoLinha = true }; j = k; return true; }
        int c = Ender.ColDe(col);
        if (c > 16383) return false;
        if (lin.Length == 0) { if (absL) return false; p = new Ponta { L = -1, C = c, AbsC = absC, SoColuna = true }; j = k; return true; }
        if (!int.TryParse(lin, out var n) || n < 1 || n > 1048576) return false;
        p = new Ponta { L = n - 1, C = c, AbsL = absL, AbsC = absC };
        j = k;
        return true;
    }

    static string PontaTxt(Ponta p) =>
        p.SoLinha ? (p.AbsL ? "$" : "") + (p.L + 1) :
        p.SoColuna ? (p.AbsC ? "$" : "") + Ender.Col(p.C) :
        (p.AbsC ? "$" : "") + Ender.Col(p.C) + (p.AbsL ? "$" : "") + (p.L + 1);

    static string RefTxt(Tok t)
    {
        var folha = t.Folha == null ? "" : (t.FolhaAspas || t.Folha.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_') ? "'" + t.Folha.Replace("'", "''") + "'" : t.Folha) + "!";
        return folha + PontaTxt(t.A) + (t.Faixa ? ":" + PontaTxt(t.B) : "");
    }

    // =================================================================== nomes das funções (pt ↔ en)
    static readonly (string pt, string en)[] Nomes =
    {
        ("SOMA", "SUM"), ("MÉDIA", "AVERAGE"), ("MÍNIMO", "MIN"), ("MÁXIMO", "MAX"), ("CONT.NÚM", "COUNT"), ("CONT.VALORES", "COUNTA"), ("CONTAR.VAZIO", "COUNTBLANK"),
        ("CONT.SE", "COUNTIF"), ("CONT.SES", "COUNTIFS"), ("SOMASE", "SUMIF"), ("SOMASES", "SUMIFS"), ("MÉDIASE", "AVERAGEIF"), ("SE", "IF"), ("SEERRO", "IFERROR"),
        ("E", "AND"), ("OU", "OR"), ("NÃO", "NOT"), ("PROCV", "VLOOKUP"), ("PROCH", "HLOOKUP"), ("ÍNDICE", "INDEX"), ("CORRESP", "MATCH"), ("PROCX", "_xlfn.XLOOKUP"),
        ("ARRED", "ROUND"), ("ARREDONDAR.PARA.CIMA", "ROUNDUP"), ("ARREDONDAR.PARA.BAIXO", "ROUNDDOWN"), ("ABS", "ABS"), ("RAIZ", "SQRT"), ("POTÊNCIA", "POWER"),
        ("MOD", "MOD"), ("INT", "INT"), ("TRUNCAR", "TRUNC"), ("CONCATENAR", "CONCATENATE"), ("CONCAT", "_xlfn.CONCAT"), ("ESQUERDA", "LEFT"), ("DIREITA", "RIGHT"),
        ("EXT.TEXTO", "MID"), ("NÚM.CARACT", "LEN"), ("MAIÚSCULA", "UPPER"), ("MINÚSCULA", "LOWER"), ("ARRUMAR", "TRIM"), ("SUBSTITUIR", "SUBSTITUTE"), ("TEXTO", "TEXT"),
        ("VALOR", "VALUE"), ("HOJE", "TODAY"), ("AGORA", "NOW"), ("DATA", "DATE"), ("ANO", "YEAR"), ("MÊS", "MONTH"), ("DIA", "DAY"), ("DIA.DA.SEMANA", "WEEKDAY"),
        ("DIAS", "_xlfn.DAYS"), ("MED", "MEDIAN"), ("MAIOR", "LARGE"), ("MENOR", "SMALL"), ("PI", "PI"), ("SOMARPRODUTO", "SUMPRODUCT"), ("ÉCÉL.VAZIA", "ISBLANK"),
        ("ÉNÚM", "ISNUMBER"), ("ÉTEXTO", "ISTEXT"), ("ÉERRO", "ISERROR"), ("PROCURAR", "SEARCH"), ("ARREDMULTB", "FLOOR"), ("SINAL", "SIGN"), ("LN", "LN"), ("LOG10", "LOG10"),
        ("FATORIAL", "FACT"), ("DESVPAD", "STDEV"), ("VAR", "VAR"), ("FIMMÊS", "EOMONTH"), ("DATAM", "EDATE"), ("HORA", "HOUR"), ("MINUTO", "MINUTE"), ("SES", "_xlfn.IFS"),
    };

    static string SemAcento(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in d) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().ToUpperInvariant();
    }

    static readonly Dictionary<string, string> Canon = CriarCanon();
    static Dictionary<string, string> CriarCanon()
    {
        var d = new Dictionary<string, string>();
        foreach (var (pt, en) in Nomes)
        {
            d[SemAcento(pt)] = pt;
            var e = en.Replace("_xlfn.", "");
            d.TryAdd(e, pt);
        }
        return d;
    }

    /// <summary>Nome canônico (em português) de uma função digitada em pt ou en, com ou sem acento.</summary>
    public static string? Canonico(string nome) => Canon.GetValueOrDefault(SemAcento(nome.Replace("_xlfn.", "").Replace("_xlws.", "")));

    public static IEnumerable<string> FuncoesPt => Nomes.Select(n => n.pt).OrderBy(x => x);

    static readonly Dictionary<string, string> ErroPtEn = new() { ["#VALOR!"] = "#VALUE!", ["#NOME?"] = "#NAME?", ["#N/D"] = "#N/A", ["#NÚM!"] = "#NUM!", ["#NULO!"] = "#NULL!" };

    /// <summary>Fórmula (sem o "=") do português do QSmart para o formato do arquivo .xlsx (inglês, vírgulas).</summary>
    public static string ParaIngles(string f)
    {
        var sb = new StringBuilder();
        foreach (var t in Tokens(f, false))
            sb.Append(t.Tipo switch
            {
                T.Func => Canonico(t.Texto) is { } c ? Nomes.First(x => x.pt == c).en : t.Texto,
                T.Sep => ",",
                T.Num => t.Texto.Replace(',', '.'),
                T.Bool => t.Texto.ToUpperInvariant() is "VERDADEIRO" or "TRUE" ? "TRUE" : "FALSE",
                T.Erro => ErroPtEn.GetValueOrDefault(t.Texto.ToUpperInvariant(), t.Texto),
                _ => t.Texto,
            });
        return sb.ToString();
    }

    /// <summary>Fórmula (sem o "=") do arquivo .xlsx para o português do QSmart.</summary>
    public static string ParaPortugues(string f)
    {
        var sb = new StringBuilder();
        foreach (var t in Tokens(f, true))
            sb.Append(t.Tipo switch
            {
                T.Func => Canonico(t.Texto) ?? t.Texto,
                T.Sep => ";",
                T.Num => t.Texto.Replace('.', ','),
                T.Bool => t.Texto.ToUpperInvariant() == "TRUE" ? "VERDADEIRO" : "FALSO",
                T.Erro => ErroPtEn.FirstOrDefault(x => x.Value == t.Texto.ToUpperInvariant()).Key ?? t.Texto,
                _ => t.Texto,
            });
        return sb.ToString();
    }

    // =================================================================== ajuste de referências
    /// <summary>Desloca as partes relativas (sem $) das referências — para copiar/colar e preencher.</summary>
    public static string Deslocar(string f, int dl, int dc)
    {
        if (dl == 0 && dc == 0) return f;
        var sb = new StringBuilder();
        foreach (var t in Tokens(f))
        {
            if (t.Tipo != T.Ref) { sb.Append(t.Texto); continue; }
            Ponta Mv(Ponta p)
            {
                if (!p.SoColuna && !p.AbsL) p.L += dl;
                if (!p.SoLinha && !p.AbsC) p.C += dc;
                return p;
            }
            t.A = Mv(t.A); t.B = Mv(t.B);
            bool ruim = (!t.A.SoColuna && t.A.L < 0) || (!t.A.SoLinha && t.A.C < 0) || (!t.B.SoColuna && t.B.L < 0) || (!t.B.SoLinha && t.B.C < 0);
            sb.Append(ruim ? "#REF!" : RefTxt(t));
        }
        return sb.ToString();
    }

    /// <summary>Inserir (n &gt; 0) ou excluir (n &lt; 0) linhas/colunas a partir de "pos" numa folha.</summary>
    public static string Ajustar(string f, string folhaDaFormula, string folhaAlvo, bool linhas, int pos, int n)
    {
        var sb = new StringBuilder();
        foreach (var t in Tokens(f))
        {
            if (t.Tipo != T.Ref) { sb.Append(t.Texto); continue; }
            var folha = t.Folha ?? folhaDaFormula;
            if (!string.Equals(folha, folhaAlvo, StringComparison.OrdinalIgnoreCase)) { sb.Append(t.Texto); continue; }
            int Get(Ponta p) => linhas ? p.L : p.C;
            bool Inteira(Ponta p) => linhas ? p.SoColuna : p.SoLinha;
            Ponta Set(Ponta p, int v) { if (linhas) p.L = v; else p.C = v; return p; }
            if (Inteira(t.A)) { sb.Append(t.Texto); continue; }
            if (n > 0)
            {
                if (Get(t.A) >= pos) t.A = Set(t.A, Get(t.A) + n);
                if (Get(t.B) >= pos) t.B = Set(t.B, Get(t.B) + n);
                sb.Append(RefTxt(t)); continue;
            }
            int q = -n, fim = pos + q - 1;
            if (!t.Faixa)
            {
                int v = Get(t.A);
                if (v >= pos && v <= fim) { sb.Append("#REF!"); continue; }
                if (v > fim) { t.A = Set(t.A, v - q); t.B = t.A; }
                sb.Append(RefTxt(t)); continue;
            }
            int a = Get(t.A), b = Get(t.B);
            int a2 = a > fim ? a - q : a >= pos ? pos : a;
            int b2 = b > fim ? b - q : b >= pos ? pos - 1 : b;
            if (b2 < a2) { sb.Append("#REF!"); continue; }
            t.A = Set(t.A, a2); t.B = Set(t.B, b2);
            sb.Append(RefTxt(t));
        }
        return sb.ToString();
    }

    /// <summary>Troca o nome de uma folha nas referências (quando ela é renomeada).</summary>
    public static string RenomearFolha(string f, string antigo, string novo)
    {
        var sb = new StringBuilder();
        foreach (var t in Tokens(f))
        {
            if (t.Tipo == T.Ref && t.Folha != null && string.Equals(t.Folha, antigo, StringComparison.OrdinalIgnoreCase)) { t.Folha = novo; sb.Append(RefTxt(t)); }
            else sb.Append(t.Texto);
        }
        return sb.ToString();
    }

    /// <summary>Faixas citadas numa fórmula (para destacar as células enquanto se edita).</summary>
    public static List<(string? folha, int r1, int c1, int r2, int c2)> Referencias(string f)
    {
        var l = new List<(string?, int, int, int, int)>();
        foreach (var t in Tokens(f))
            if (t.Tipo == T.Ref && !t.A.SoColuna && !t.A.SoLinha)
                l.Add((t.Folha, Math.Min(t.A.L, t.B.L), Math.Min(t.A.C, t.B.C), Math.Max(t.A.L, t.B.L), Math.Max(t.A.C, t.B.C)));
        return l;
    }

    // =================================================================== árvore
    public abstract class No { }
    sealed class NumN : No { public double V; }
    sealed class TxtN : No { public string V = ""; }
    sealed class BoolN : No { public bool V; }
    sealed class ErrN : No { public ErroF V = ErroF.Valor; }
    sealed class RefN : No { public Tok T = null!; }
    sealed class FuncN : No { public string Nome = ""; public List<No> Args = new(); }
    sealed class BinN : No { public string Op = ""; public No A = null!, B = null!; }
    sealed class UnN : No { public char Op; public No A = null!; }
    sealed class PctN : No { public No A = null!; }

    sealed class Leitor
    {
        readonly List<Tok> t; int i;
        public Leitor(List<Tok> tk) { t = tk.Where(x => x.Tipo != T.Espaco).ToList(); }
        Tok? Ver => i < t.Count ? t[i] : null;
        bool Op(params string[] ops) => Ver is { Tipo: T.Op } x && ops.Contains(x.Texto);
        public No Tudo() { var n = Comparacao(); if (i < t.Count) throw new FormatException("sobra"); return n; }
        No Comparacao() { var a = Concat(); while (Op("=", "<>", "<", ">", "<=", ">=")) { var op = t[i++].Texto; a = new BinN { Op = op, A = a, B = Concat() }; } return a; }
        No Concat() { var a = Soma(); while (Op("&")) { i++; a = new BinN { Op = "&", A = a, B = Soma() }; } return a; }
        No Soma() { var a = Mult(); while (Op("+", "-")) { var op = t[i++].Texto; a = new BinN { Op = op, A = a, B = Mult() }; } return a; }
        No Mult() { var a = Pot(); while (Op("*", "/")) { var op = t[i++].Texto; a = new BinN { Op = op, A = a, B = Pot() }; } return a; }
        No Pot() { var a = Unario(); while (Op("^")) { i++; a = new BinN { Op = "^", A = a, B = Unario() }; } return a; }
        No Unario()
        {
            if (Op("-")) { i++; return new UnN { Op = '-', A = Unario() }; }
            if (Op("+")) { i++; return Unario(); }
            var a = Primario();
            while (Op("%")) { i++; a = new PctN { A = a }; }
            return a;
        }
        No Primario()
        {
            var x = Ver ?? throw new FormatException("fim");
            i++;
            switch (x.Tipo)
            {
                case T.Num: return new NumN { V = double.Parse(x.Texto.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture) };
                case T.Txt: return new TxtN { V = x.Texto[1..^1].Replace("\"\"", "\"") };
                case T.Bool: return new BoolN { V = x.Texto.ToUpperInvariant() is "VERDADEIRO" or "TRUE" };
                case T.Erro: return new ErrN { V = x.Texto.ToUpperInvariant() switch { "#DIV/0!" => ErroF.Div0, "#REF!" => ErroF.Ref, "#NOME?" or "#NAME?" => ErroF.Nome, "#N/D" or "#N/A" => ErroF.ND, "#NÚM!" or "#NUM!" => ErroF.Num, _ => ErroF.Valor } };
                case T.Ref: return new RefN { T = x };
                case T.Abre: { var n = Comparacao(); if (Ver?.Tipo != T.Fecha) throw new FormatException(")"); i++; return n; }
                case T.Func:
                {
                    var f = new FuncN { Nome = x.Texto };
                    if (Ver?.Tipo != T.Abre) throw new FormatException("(");
                    i++;
                    if (Ver?.Tipo == T.Fecha) { i++; return f; }
                    while (true)
                    {
                        if (Ver?.Tipo is T.Sep or T.Fecha) f.Args.Add(new TxtN { V = "" });   // argumento vazio
                        else f.Args.Add(Comparacao());
                        if (Ver?.Tipo == T.Sep) { i++; continue; }
                        if (Ver?.Tipo == T.Fecha) { i++; break; }
                        throw new FormatException("args");
                    }
                    return f;
                }
                default: throw new FormatException("token");
            }
        }
    }

    static readonly Dictionary<string, No?> Arvores = new();
    public static No? Arvore(string formula)
    {
        if (Arvores.TryGetValue(formula, out var n)) return n;
        try { n = new Leitor(Tokens(formula)).Tudo(); } catch { n = null; }
        if (Arvores.Count > 20000) Arvores.Clear();
        Arvores[formula] = n;
        return n;
    }

    public static bool Valida(string formula) => Arvore(formula) != null;

    // =================================================================== cálculo
    public sealed class Calc
    {
        readonly Pasta P;
        readonly Dictionary<(Folha, long), object?> memo = new();
        readonly HashSet<(Folha, long)> visitando = new();
        public Calc(Pasta p) { P = p; }
        public void Limpar() => memo.Clear();

        public object? Valor(Folha f, int r, int c)
        {
            var k = (f, Folha.K(r, c));
            if (memo.TryGetValue(k, out var v)) return v;
            var cel = f.Get(r, c);
            if (cel == null || cel.Entrada.Length == 0) return null;
            if (!cel.Entrada.StartsWith("=") || cel.Entrada.Length == 1)
            {
                v = cel.E.Formato == "texto" ? cel.Entrada : FormatoPt.Valor(cel.Entrada, out _);
                memo[k] = v; return v;
            }
            if (!visitando.Add(k)) return ErroF.Circ;
            try
            {
                var n = Arvore(cel.Entrada[1..]);
                v = n == null ? ErroF.Nome : Escalar(Avaliar(n, f));
            }
            catch (StackOverflowException) { v = ErroF.Circ; }
            catch (Exception) { v = ErroF.Valor; }
            finally { visitando.Remove(k); }
            memo[k] = v;
            return v;
        }

        object? Escalar(object? v) => v is FaixaV fx ? (fx.Linhas == 1 && fx.Colunas == 1 ? Valor(fx.F, fx.R1, fx.C1) : ErroF.Valor) : v;

        Folha? FolhaDe(Tok t, Folha atual) => t.Folha == null ? atual : P.Folha(t.Folha);

        object? Avaliar(No n, Folha f)
        {
            switch (n)
            {
                case NumN x: return x.V;
                case TxtN x: return x.V;
                case BoolN x: return x.V;
                case ErrN x: return x.V;
                case RefN x:
                {
                    var fo = FolhaDe(x.T, f);
                    if (fo == null) return ErroF.Ref;
                    var a = x.T.A; var b = x.T.B;
                    int r1 = a.SoColuna ? 0 : Math.Min(a.L, b.L), r2 = a.SoColuna ? Math.Max(0, fo.UltLinha) : Math.Max(a.L, b.L);
                    int c1 = a.SoLinha ? 0 : Math.Min(a.C, b.C), c2 = a.SoLinha ? Math.Max(0, fo.UltColuna) : Math.Max(a.C, b.C);
                    if (!x.T.Faixa) return Valor(fo, a.L, a.C);
                    return new FaixaV(fo, r1, c1, r2, c2);
                }
                case UnN x: { var v = Num(Escalar(Avaliar(x.A, f))); return v is double d ? -d : v; }
                case PctN x: { var v = Num(Escalar(Avaliar(x.A, f))); return v is double d ? d / 100 : v; }
                case BinN x: return Bin(x, f);
                case FuncN x: return Funcoes.Chamar(this, x.Nome, x.Args, f);
            }
            return ErroF.Valor;
        }

        internal object? Arg(No n, Folha f) => Avaliar(n, f);
        internal object? ArgEsc(No n, Folha f) => Escalar(Avaliar(n, f));

        object? Bin(BinN x, Folha f)
        {
            var a = Escalar(Avaliar(x.A, f)); var b = Escalar(Avaliar(x.B, f));
            if (a is ErroF ea) return ea;
            if (b is ErroF eb) return eb;
            switch (x.Op)
            {
                case "&": return Txt(a) + Txt(b);
                case "=": case "<>": case "<": case ">": case "<=": case ">=":
                {
                    int c = Comparar(a, b);
                    return x.Op switch { "=" => c == 0, "<>" => c != 0, "<" => c < 0, ">" => c > 0, "<=" => c <= 0, _ => c >= 0 };
                }
            }
            var na = Num(a); var nb = Num(b);
            if (na is ErroF e1) return e1;
            if (nb is ErroF e2) return e2;
            double p = (double)na!, q = (double)nb!;
            return x.Op switch
            {
                "+" => p + q, "-" => p - q, "*" => p * q,
                "/" => q == 0 ? ErroF.Div0 : p / q,
                "^" => Math.Pow(p, q) is var r && (double.IsNaN(r) || double.IsInfinity(r)) ? ErroF.Num : Math.Pow(p, q),
                _ => ErroF.Valor,
            };
        }

        public IEnumerable<object?> Celulas(FaixaV fx)
        {
            for (int r = fx.R1; r <= fx.R2; r++)
                for (int c = fx.C1; c <= fx.C2; c++)
                    yield return Valor(fx.F, r, c);
        }
    }

    // =================================================================== conversões
    public static object? Num(object? v) => v switch
    {
        null => 0.0,
        double d => d,
        bool b => b ? 1.0 : 0.0,
        string s when s.Trim().Length == 0 => 0.0,
        string s => FormatoPt.Numero(s, out var d) ? d : FormatoPt.Data(s, out var dt) ? dt : ErroF.Valor,
        ErroF e => e,
        _ => ErroF.Valor,
    };

    public static string Txt(object? v) => v switch
    {
        null => "", double d => FormatoPt.Geral(d), bool b => b ? "VERDADEIRO" : "FALSO", ErroF e => e.Cod, _ => v.ToString() ?? "",
    };

    public static int Comparar(object? a, object? b)
    {
        // Excel: números < textos < lógicos; vazio vale 0 ou ""
        int Ordem(object? x) => x switch { double => 0, null => 0, string => 1, bool => 2, _ => 3 };
        if (a == null && b is string) a = "";
        if (b == null && a is string) b = "";
        if (a == null) a = 0.0; if (b == null) b = 0.0;
        int oa = Ordem(a), ob = Ordem(b);
        if (oa != ob) return oa.CompareTo(ob);
        return a switch
        {
            double da => da.CompareTo((double)b!),
            string sa => string.Compare(sa, (string)b!, StringComparison.CurrentCultureIgnoreCase),
            bool ba => ba.CompareTo((bool)b!),
            _ => 0,
        };
    }
}
