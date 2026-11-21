using System.Globalization;
using System.Text.RegularExpressions;
using static QSmart.Core.Pacote.Formula;

namespace QSmart.Core.Pacote;

/// <summary>As funções do Smart Math (nomes em português, como no Excel brasileiro).</summary>
static class Funcoes
{
    static bool Erro(object? v, out ErroF e) { e = (v as ErroF)!; return v is ErroF; }

    /// <summary>Todos os valores dos argumentos (faixas abertas célula a célula). "deFaixa" diz se o valor veio de uma faixa.</summary>
    static IEnumerable<(object? v, bool deFaixa)> Itens(Calc c, List<No> args, Folha f)
    {
        foreach (var a in args)
        {
            var v = c.Arg(a, f);
            if (v is FaixaV fx) foreach (var x in c.Celulas(fx)) yield return (x, true);
            else yield return (v, false);
        }
    }

    /// <summary>Números para SOMA/MÉDIA/MÁXIMO…: em faixas, só números; em argumentos diretos, converte.</summary>
    static object Numeros(Calc c, List<No> args, Folha f, out List<double> l)
    {
        l = new List<double>();
        foreach (var (v, deFaixa) in Itens(c, args, f))
        {
            if (v is ErroF e) return e;
            if (deFaixa) { if (v is double d) l.Add(d); continue; }
            var n = Num(v);
            if (n is ErroF en) return en;
            l.Add((double)n!);
        }
        return true;
    }

    static object? N1(Calc c, List<No> a, int i, Folha f, double padrao = double.NaN)
    {
        if (i >= a.Count) return double.IsNaN(padrao) ? ErroF.Valor : padrao;
        return Num(c.ArgEsc(a[i], f));
    }

    static string S1(Calc c, List<No> a, int i, Folha f) => i < a.Count ? Txt(c.ArgEsc(a[i], f)) : "";

    static object? B1(Calc c, List<No> a, int i, Folha f)
    {
        var v = c.ArgEsc(a[i], f);
        return v switch
        {
            bool b => b, double d => d != 0, null => false, ErroF e => e,
            string s when s.ToUpperInvariant() is "VERDADEIRO" or "TRUE" => true,
            string s when s.ToUpperInvariant() is "FALSO" or "FALSE" => false,
            _ => ErroF.Valor,
        };
    }

    /// <summary>Critério do CONT.SE/SOMASE: "&gt;5", "&lt;&gt;x", "a*", 10…</summary>
    static Func<object?, bool> Criterio(object? crit)
    {
        if (crit is double dn) return v => v is double d && d == dn || (v is string s && FormatoPt.Numero(s, out var x) && x == dn);
        if (crit is bool bb) return v => v is bool b && b == bb;
        var t = Txt(crit);
        string op = "=";
        foreach (var o in new[] { ">=", "<=", "<>", ">", "<", "=" }) if (t.StartsWith(o)) { op = o; t = t[o.Length..]; break; }
        bool ehNum = FormatoPt.Numero(t, out var alvo) || FormatoPt.Data(t, out alvo);
        if (ehNum && op != "=" && op != "<>")
            return v => v is double d && op switch { ">" => d > alvo, "<" => d < alvo, ">=" => d >= alvo, "<=" => d <= alvo, _ => false };
        if (ehNum) return v => (v is double d && d == alvo) == (op == "=");
        if (op is ">" or "<" or ">=" or "<=")
            return v => v is string s && (string.Compare(s, t, StringComparison.CurrentCultureIgnoreCase) is var cmp) && op switch { ">" => cmp > 0, "<" => cmp < 0, ">=" => cmp >= 0, _ => cmp <= 0 };
        Regex? re = t.Contains('*') || t.Contains('?') ? new Regex("^" + Regex.Escape(t).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase) : null;
        bool Igual(object? v)
        {
            if (t.Length == 0) return v == null || (v is string s0 && s0.Length == 0);
            var s = Txt(v);
            return re != null ? re.IsMatch(s) : string.Equals(s, t, StringComparison.CurrentCultureIgnoreCase);
        }
        return v => Igual(v) == (op == "=");
    }

    static List<object?> Lista(Calc c, object? v) => v is FaixaV fx ? c.Celulas(fx).ToList() : new List<object?> { v };

    static double Arred(double d, int casas)
    {
        var m = Math.Pow(10, casas);
        return Math.Round(d * m, MidpointRounding.AwayFromZero) / m;
    }

    public static object? Chamar(Calc c, string nomeDigitado, List<No> a, Folha f)
    {
        var nome = Canonico(nomeDigitado);
        if (nome == null) return ErroF.Nome;
        object? r;
        switch (nome)
        {
            // ---------------- matemática / estatística
            case "SOMA": { var e = Numeros(c, a, f, out var l); return e is ErroF ee ? ee : l.Sum(); }
            case "MÉDIA": { var e = Numeros(c, a, f, out var l); return e is ErroF ee ? ee : l.Count == 0 ? ErroF.Div0 : l.Average(); }
            case "MÍNIMO": { var e = Numeros(c, a, f, out var l); return e is ErroF ee ? ee : l.Count == 0 ? 0.0 : l.Min(); }
            case "MÁXIMO": { var e = Numeros(c, a, f, out var l); return e is ErroF ee ? ee : l.Count == 0 ? 0.0 : l.Max(); }
            case "MED":
            {
                var e = Numeros(c, a, f, out var l); if (e is ErroF ee) return ee; if (l.Count == 0) return ErroF.Num;
                l.Sort(); return l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2;
            }
            case "DESVPAD": case "VAR":
            {
                var e = Numeros(c, a, f, out var l); if (e is ErroF ee) return ee; if (l.Count < 2) return ErroF.Div0;
                var m = l.Average(); var v = l.Sum(x => (x - m) * (x - m)) / (l.Count - 1);
                return nome == "VAR" ? v : Math.Sqrt(v);
            }
            case "CONT.NÚM": return (double)Itens(c, a, f).Count(x => x.v is double || (!x.deFaixa && Num(x.v) is double && x.v is not null));
            case "CONT.VALORES": return (double)Itens(c, a, f).Count(x => x.v != null && !(x.v is string s && s.Length == 0 && x.deFaixa));
            case "CONTAR.VAZIO": return (double)Itens(c, a, f).Count(x => x.v == null || (x.v is string s && s.Length == 0));
            case "MAIOR": case "MENOR":
            {
                if (a.Count < 2) return ErroF.Valor;
                var l = Lista(c, c.Arg(a[0], f)).OfType<double>().ToList();
                if (N1(c, a, 1, f) is not double k) return ErroF.Valor;
                int ki = (int)k; if (ki < 1 || ki > l.Count) return ErroF.Num;
                l.Sort(); return nome == "MENOR" ? l[ki - 1] : l[^ki];
            }
            case "SOMARPRODUTO":
            {
                var listas = a.Select(x => Lista(c, c.Arg(x, f))).ToList();
                if (listas.Count == 0 || listas.Any(l => l.Count != listas[0].Count)) return ErroF.Valor;
                double s = 0;
                for (int i = 0; i < listas[0].Count; i++) { double p = 1; foreach (var l in listas) p *= l[i] is double d ? d : 0; s += p; }
                return s;
            }
            case "ARRED": case "ARREDONDAR.PARA.CIMA": case "ARREDONDAR.PARA.BAIXO": case "TRUNCAR":
            {
                if (N1(c, a, 0, f) is not double d) return N1(c, a, 0, f);
                if (N1(c, a, 1, f, 0) is not double cs) return ErroF.Valor;
                int casas = (int)cs; var m = Math.Pow(10, casas);
                return nome switch
                {
                    "ARRED" => Arred(d, casas),
                    "ARREDONDAR.PARA.CIMA" => Math.Sign(d) * Math.Ceiling(Math.Abs(d) * m - 1e-9) / m,
                    _ => Math.Sign(d) * Math.Floor(Math.Abs(d) * m + 1e-9) / m,
                };
            }
            case "ARREDMULTB": { if (N1(c, a, 0, f) is not double d || N1(c, a, 1, f, 1) is not double s || s == 0) return ErroF.Valor; return Math.Floor(d / s) * s; }
            case "ABS": r = N1(c, a, 0, f); return r is double d1 ? Math.Abs(d1) : r;
            case "SINAL": r = N1(c, a, 0, f); return r is double d2 ? (double)Math.Sign(d2) : r;
            case "INT": r = N1(c, a, 0, f); return r is double d3 ? Math.Floor(d3) : r;
            case "RAIZ": r = N1(c, a, 0, f); return r is double d4 ? (d4 < 0 ? ErroF.Num : Math.Sqrt(d4)) : r;
            case "LN": r = N1(c, a, 0, f); return r is double d5 ? (d5 <= 0 ? ErroF.Num : Math.Log(d5)) : r;
            case "LOG10": r = N1(c, a, 0, f); return r is double d6 ? (d6 <= 0 ? ErroF.Num : Math.Log10(d6)) : r;
            case "FATORIAL": { r = N1(c, a, 0, f); if (r is not double d7 || d7 < 0 || d7 > 170) return r is ErroF ? r : ErroF.Num; double p = 1; for (int i = 2; i <= (int)d7; i++) p *= i; return p; }
            case "POTÊNCIA": { var x = N1(c, a, 0, f); var y = N1(c, a, 1, f); if (x is not double dx) return x; if (y is not double dy) return y; return Math.Pow(dx, dy); }
            case "MOD": { var x = N1(c, a, 0, f); var y = N1(c, a, 1, f); if (x is not double dx) return x; if (y is not double dy) return y; if (dy == 0) return ErroF.Div0; return dx - dy * Math.Floor(dx / dy); }
            case "PI": return Math.PI;

            // ---------------- condicionais
            case "SE":
            {
                if (a.Count < 2) return ErroF.Valor;
                var t = B1(c, a, 0, f);
                if (t is ErroF et) return et;
                if ((bool)t!) return c.ArgEsc(a[1], f) ?? 0.0;
                return a.Count > 2 ? c.ArgEsc(a[2], f) ?? 0.0 : false;
            }
            case "SES":
                for (int i = 0; i + 1 < a.Count; i += 2) { var t = B1(c, a, i, f); if (t is ErroF et) return et; if ((bool)t!) return c.ArgEsc(a[i + 1], f); }
                return ErroF.ND;
            case "SEERRO": { var v = c.ArgEsc(a[0], f); return v is ErroF ? (a.Count > 1 ? c.ArgEsc(a[1], f) ?? 0.0 : 0.0) : v; }
            case "E": case "OU":
            {
                var l = new List<bool>();
                foreach (var (v, _) in Itens(c, a, f)) { if (v is ErroF e) return e; if (v is bool b) l.Add(b); else if (v is double d) l.Add(d != 0); }
                if (l.Count == 0) return ErroF.Valor;
                return nome == "E" ? l.All(x => x) : l.Any(x => x);
            }
            case "NÃO": { var t = B1(c, a, 0, f); return t is bool b ? !b : t; }
            case "CONT.SE": case "SOMASE": case "MÉDIASE":
            {
                if (a.Count < 2) return ErroF.Valor;
                var faixa = c.Arg(a[0], f) as FaixaV; if (faixa == null) return ErroF.Valor;
                var ok = Criterio(c.ArgEsc(a[1], f));
                var soma = a.Count > 2 ? c.Arg(a[2], f) as FaixaV : faixa;
                if (soma == null) return ErroF.Valor;
                double s = 0; int n = 0;
                for (int i = 0; i < faixa.Linhas; i++)
                    for (int j = 0; j < faixa.Colunas; j++)
                        if (ok(c.Valor(faixa.F, faixa.R1 + i, faixa.C1 + j)))
                        {
                            n++;
                            if (c.Valor(soma.F, soma.R1 + i, soma.C1 + j) is double d) s += d;
                        }
                return nome == "CONT.SE" ? n : nome == "SOMASE" ? s : n == 0 ? ErroF.Div0 : s / n;
            }
            case "CONT.SES": case "SOMASES":
            {
                int ini = nome == "SOMASES" ? 1 : 0;
                var soma = nome == "SOMASES" ? c.Arg(a[0], f) as FaixaV : null;
                var pares = new List<(FaixaV fx, Func<object?, bool> ok)>();
                for (int i = ini; i + 1 < a.Count; i += 2) { if (c.Arg(a[i], f) is not FaixaV fx) return ErroF.Valor; pares.Add((fx, Criterio(c.ArgEsc(a[i + 1], f)))); }
                if (pares.Count == 0) return ErroF.Valor;
                var b0 = pares[0].fx; double s = 0; int n = 0;
                for (int i = 0; i < b0.Linhas; i++)
                    for (int j = 0; j < b0.Colunas; j++)
                        if (pares.All(p => p.ok(c.Valor(p.fx.F, p.fx.R1 + i, p.fx.C1 + j))))
                        { n++; if (soma != null && c.Valor(soma.F, soma.R1 + i, soma.C1 + j) is double d) s += d; }
                return nome == "CONT.SES" ? n : s;
            }

            // ---------------- procura
            case "PROCV": case "PROCH":
            {
                if (a.Count < 3) return ErroF.Valor;
                var alvo = c.ArgEsc(a[0], f); if (alvo is ErroF ea) return ea;
                if (c.Arg(a[1], f) is not FaixaV t) return ErroF.Valor;
                if (N1(c, a, 2, f) is not double ci) return ErroF.Valor;
                bool aprox = a.Count > 3 && B1(c, a, 3, f) is true;
                bool v = nome == "PROCV";
                int idx = (int)ci - 1, n = v ? t.Linhas : t.Colunas;
                if (idx < 0 || idx >= (v ? t.Colunas : t.Linhas)) return ErroF.Ref;
                int achou = -1;
                for (int i = 0; i < n; i++)
                {
                    var x = v ? c.Valor(t.F, t.R1 + i, t.C1) : c.Valor(t.F, t.R1, t.C1 + i);
                    int cmp = Comparar(x, alvo);
                    if (!aprox) { if (cmp == 0 || (x is string sx && alvo is string sa && Criterio(sa)(sx))) { achou = i; break; } }
                    else { if (cmp <= 0) achou = i; else break; }
                }
                if (achou < 0) return ErroF.ND;
                return (v ? c.Valor(t.F, t.R1 + achou, t.C1 + idx) : c.Valor(t.F, t.R1 + idx, t.C1 + achou)) ?? 0.0;
            }
            case "CORRESP":
            {
                var alvo = c.ArgEsc(a[0], f);
                var l = Lista(c, c.Arg(a[1], f));
                int tipo = a.Count > 2 && N1(c, a, 2, f) is double tp ? (int)tp : 1;
                int achou = -1;
                for (int i = 0; i < l.Count; i++)
                {
                    int cmp = Comparar(l[i], alvo);
                    if (tipo == 0) { if (cmp == 0) { achou = i; break; } }
                    else if (tipo > 0) { if (cmp <= 0) achou = i; else break; }
                    else { if (cmp >= 0) achou = i; else break; }
                }
                return achou < 0 ? ErroF.ND : achou + 1.0;
            }
            case "ÍNDICE":
            {
                if (c.Arg(a[0], f) is not FaixaV t) return ErroF.Valor;
                if (N1(c, a, 1, f, 0) is not double li) return ErroF.Valor;
                double co = a.Count > 2 && N1(c, a, 2, f, 0) is double cc ? cc : 0;
                int L = (int)li, C = (int)co;
                if (t.Linhas == 1 && a.Count == 2) { C = L; L = 1; }
                if (C == 0) C = 1; if (L == 0) L = 1;
                if (L > t.Linhas || C > t.Colunas || L < 1 || C < 1) return ErroF.Ref;
                return c.Valor(t.F, t.R1 + L - 1, t.C1 + C - 1) ?? 0.0;
            }
            case "PROCX":
            {
                if (a.Count < 3) return ErroF.Valor;
                var alvo = c.ArgEsc(a[0], f);
                var busca = Lista(c, c.Arg(a[1], f)); var ret = Lista(c, c.Arg(a[2], f));
                for (int i = 0; i < busca.Count && i < ret.Count; i++) if (Comparar(busca[i], alvo) == 0) return ret[i] ?? 0.0;
                return a.Count > 3 ? c.ArgEsc(a[3], f) : ErroF.ND;
            }

            // ---------------- texto
            case "CONCATENAR": case "CONCAT": return string.Concat(Itens(c, a, f).Select(x => Txt(x.v)));
            case "ESQUERDA": { var s = S1(c, a, 0, f); int n = a.Count > 1 && N1(c, a, 1, f) is double d ? (int)d : 1; return n < 0 ? ErroF.Valor : s[..Math.Min(n, s.Length)]; }
            case "DIREITA": { var s = S1(c, a, 0, f); int n = a.Count > 1 && N1(c, a, 1, f) is double d ? (int)d : 1; return n < 0 ? ErroF.Valor : s[Math.Max(0, s.Length - n)..]; }
            case "EXT.TEXTO":
            {
                var s = S1(c, a, 0, f);
                if (N1(c, a, 1, f) is not double i0 || N1(c, a, 2, f) is not double n0 || i0 < 1 || n0 < 0) return ErroF.Valor;
                int i = (int)i0 - 1; if (i >= s.Length) return "";
                return s.Substring(i, Math.Min((int)n0, s.Length - i));
            }
            case "NÚM.CARACT": return (double)S1(c, a, 0, f).Length;
            case "MAIÚSCULA": return S1(c, a, 0, f).ToUpperInvariant();
            case "MINÚSCULA": return S1(c, a, 0, f).ToLowerInvariant();
            case "ARRUMAR": return Regex.Replace(S1(c, a, 0, f).Trim(), " {2,}", " ");
            case "SUBSTITUIR": { var s = S1(c, a, 0, f); var de = S1(c, a, 1, f); return de.Length == 0 ? s : s.Replace(de, S1(c, a, 2, f)); }
            case "PROCURAR":
            {
                var o = S1(c, a, 0, f); var s = S1(c, a, 1, f);
                int ini = a.Count > 2 && N1(c, a, 2, f) is double d ? (int)d - 1 : 0;
                if (ini < 0 || ini > s.Length) return ErroF.Valor;
                var i = s.IndexOf(o, ini, StringComparison.CurrentCultureIgnoreCase);
                return i < 0 ? ErroF.Valor : i + 1.0;
            }
            case "VALOR": { var v = c.ArgEsc(a[0], f); var n = Num(v); return n; }
            case "TEXTO":
            {
                var v = Num(c.ArgEsc(a[0], f)); if (v is not double d) return S1(c, a, 0, f);
                var fm = S1(c, a, 1, f);
                if (Regex.IsMatch(fm, "[dmya]", RegexOptions.IgnoreCase))
                {
                    var dt = DateTime.FromOADate(d);
                    var net = fm.Replace("aaaa", "yyyy").Replace("AAAA", "yyyy").Replace("aa", "yy").Replace("mmmm", "MMMM").Replace("mmm", "MMM").Replace("mm", "MM").Replace("dddd", "dddd");
                    return dt.ToString(net, CultureInfo.GetCultureInfo("pt-BR"));
                }
                int casas = fm.Contains(',') ? fm.Length - fm.LastIndexOf(',') - 1 : 0;
                casas = Math.Max(0, Math.Min(10, Regex.Replace(casas > 0 ? fm[(fm.LastIndexOf(',') + 1)..] : "", "[^0#]", "").Length));
                var txt = FormatoPt.Num(fm.EndsWith("%") ? d * 100 : d, casas, fm.Contains('.')) + (fm.EndsWith("%") ? "%" : "");
                return fm.Contains("R$") ? "R$ " + txt : txt;
            }

            // ---------------- datas
            case "HOJE": return Math.Floor(DateTime.Today.ToOADate());
            case "AGORA": return DateTime.Now.ToOADate();
            case "DATA":
            {
                if (N1(c, a, 0, f) is not double y || N1(c, a, 1, f) is not double m || N1(c, a, 2, f) is not double d) return ErroF.Valor;
                try { return new DateTime((int)y, 1, 1).AddMonths((int)m - 1).AddDays((int)d - 1).ToOADate(); } catch { return ErroF.Num; }
            }
            case "ANO": case "MÊS": case "DIA": case "DIA.DA.SEMANA": case "HORA": case "MINUTO":
            {
                if (N1(c, a, 0, f) is not double d) return N1(c, a, 0, f);
                DateTime dt; try { dt = DateTime.FromOADate(d); } catch { return ErroF.Num; }
                return nome switch
                {
                    "ANO" => dt.Year, "MÊS" => dt.Month, "DIA" => dt.Day, "HORA" => dt.Hour, "MINUTO" => dt.Minute,
                    _ => (a.Count > 1 && N1(c, a, 1, f) is double t && (int)t == 2) ? ((int)dt.DayOfWeek + 6) % 7 + 1.0 : (int)dt.DayOfWeek + 1.0,
                };
            }
            case "DIAS": { if (N1(c, a, 0, f) is not double fim || N1(c, a, 1, f) is not double ini) return ErroF.Valor; return Math.Floor(fim) - Math.Floor(ini); }
            case "FIMMÊS": case "DATAM":
            {
                if (N1(c, a, 0, f) is not double d || N1(c, a, 1, f) is not double m) return ErroF.Valor;
                var dt = DateTime.FromOADate(d).AddMonths((int)m);
                return nome == "DATAM" ? Math.Floor(dt.ToOADate()) : new DateTime(dt.Year, dt.Month, DateTime.DaysInMonth(dt.Year, dt.Month)).ToOADate();
            }

            // ---------------- informação
            case "ÉCÉL.VAZIA": return c.ArgEsc(a[0], f) == null;
            case "ÉNÚM": return c.ArgEsc(a[0], f) is double;
            case "ÉTEXTO": return c.ArgEsc(a[0], f) is string;
            case "ÉERRO": return c.ArgEsc(a[0], f) is ErroF;
        }
        return ErroF.Nome;
    }
}
