using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;

namespace QSmart.Core.Pdf;

public enum Fonte { Normal, Negrito, Italico }

public readonly record struct Cor(double R, double G, double B)
{
    public static Cor Hex(string h)
    {
        h = h.TrimStart('#');
        return new Cor(Convert.ToInt32(h[..2], 16) / 255.0, Convert.ToInt32(h.Substring(2, 2), 16) / 255.0, Convert.ToInt32(h.Substring(4, 2), 16) / 255.0);
    }
    public static readonly Cor Branco = new(1, 1, 1);
    public string Op(bool contorno) => string.Format(CultureInfo.InvariantCulture, "{0:0.###} {1:0.###} {2:0.###} {3}", R, G, B, contorno ? "RG" : "rg");
}

public static class Fontes
{
    static readonly Encoding Win = CriarWin();
    static Encoding CriarWin()
    {
        // Tabela WinAnsi (cp1252) manual: o navegador não traz o provedor de codificações
        return Encoding.Latin1;
    }

    static readonly Dictionary<char, byte> Extra = new()
    {
        ['€'] = 0x80, ['‚'] = 0x82, ['ƒ'] = 0x83, ['„'] = 0x84, ['…'] = 0x85, ['†'] = 0x86, ['‡'] = 0x87, ['ˆ'] = 0x88, ['‰'] = 0x89,
        ['Š'] = 0x8A, ['‹'] = 0x8B, ['Œ'] = 0x8C, ['Ž'] = 0x8E, ['‘'] = 0x91, ['’'] = 0x92, ['“'] = 0x93, ['”'] = 0x94, ['•'] = 0x95,
        ['–'] = 0x96, ['—'] = 0x97, ['˜'] = 0x98, ['™'] = 0x99, ['š'] = 0x9A, ['›'] = 0x9B, ['œ'] = 0x9C, ['ž'] = 0x9E, ['Ÿ'] = 0x9F,
    };

    public static byte Byte(char c)
    {
        if (Extra.TryGetValue(c, out var b)) return b;
        if (c == ' ') return 0xA0;
        if (c < 0x80 || (c >= 0xA0 && c <= 0xFF)) return (byte)c;
        if (c == '→') return (byte)'>';
        if (c == '×') return 0xD7;
        return (byte)'?';
    }

    static short[] Tab(Fonte f) => f switch { Fonte.Negrito => Metricas.HelveticaBold, Fonte.Italico => Metricas.HelveticaOblique, _ => Metricas.Helvetica };

    public static double Largura(string s, Fonte f, double tam)
    {
        var t = Tab(f);
        double w = 0;
        foreach (var c in s) w += t[Byte(c)];
        return w * tam / 1000.0;
    }

    public static string Cortar(string txt, double largura, Fonte f = Fonte.Normal, double tam = 8)
    {
        if (Largura(txt, f, tam) <= largura) return txt;
        while (txt.Length > 0 && Largura(txt + "…", f, tam) > largura) txt = txt[..^1];
        return txt + "…";
    }

    public static string Nome(Fonte f) => f switch { Fonte.Negrito => "F2", Fonte.Italico => "F3", _ => "F1" };

    public static string Literal(string s)
    {
        var sb = new StringBuilder("(");
        foreach (var c in s)
        {
            var b = Byte(c);
            if (b == '(' || b == ')' || b == '\\') sb.Append('\\').Append((char)b);
            else if (b < 32 || b > 126) sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
            else sb.Append((char)b);
        }
        return sb.Append(')').ToString();
    }
}

/// <summary>Página: operações de desenho (coordenadas do PDF, origem embaixo à esquerda).</summary>
public sealed class Tela
{
    public readonly StringBuilder Ops = new();
    static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public void Retangulo(double x, double y, double w, double h, Cor? preenchimento, Cor? contorno = null, double espessura = 1)
    {
        if (preenchimento == null && contorno == null) return;
        Ops.Append("q ");
        if (preenchimento != null) Ops.Append(preenchimento.Value.Op(false)).Append(' ');
        if (contorno != null) Ops.Append(contorno.Value.Op(true)).Append(' ').Append(N(espessura)).Append(" w ");
        Ops.Append($"{N(x)} {N(y)} {N(w)} {N(h)} re ");
        Ops.Append(preenchimento != null && contorno != null ? "B" : preenchimento != null ? "f" : "S").Append(" Q\n");
    }

    public void Linha(double x1, double y1, double x2, double y2, Cor cor, double espessura = 1)
        => Ops.Append($"q {cor.Op(true)} {N(espessura)} w {N(x1)} {N(y1)} m {N(x2)} {N(y2)} l S Q\n");

    /// <summary>Texto na linha de base y. alinhamento: e (esquerda), c (centro), d (direita).</summary>
    public void Texto(double x, double y, string s, Fonte f, double tam, Cor cor, char alinhamento = 'e')
    {
        if (string.IsNullOrEmpty(s)) return;
        var w = Fontes.Largura(s, f, tam);
        if (alinhamento == 'c') x -= w / 2;
        else if (alinhamento == 'd') x -= w;
        Ops.Append($"BT {cor.Op(false)} /{Fontes.Nome(f)} {N(tam)} Tf {N(x)} {N(y)} Td {Fontes.Literal(s)} Tj ET\n");
    }
}

// ================= elementos de layout (estilo platypus) =================
public abstract class Elemento
{
    public double EspacoAntes, EspacoDepois;
    public abstract double Altura(double largura);
    public abstract void Desenhar(Tela t, double x, double yTopo, double largura);
    /// <summary>Divide para caber em <paramref name="disponivel"/>; null = não divide.</summary>
    public virtual (Elemento cabe, Elemento resto)? Dividir(double largura, double disponivel) => null;
}

public sealed class Espaco : Elemento
{
    readonly double _h;
    public Espaco(double h) { _h = h; }
    public override double Altura(double largura) => _h;
    public override void Desenhar(Tela t, double x, double yTopo, double largura) { }
}

public sealed class QuebraPagina : Elemento
{
    public override double Altura(double largura) => 0;
    public override void Desenhar(Tela t, double x, double yTopo, double largura) { }
}

public sealed class LinhaH : Elemento
{
    public double Espessura = 0.8;
    public Cor Cor;
    public override double Altura(double largura) => Espessura;
    public override void Desenhar(Tela t, double x, double yTopo, double largura) => t.Linha(x, yTopo - Espessura / 2, x + largura, yTopo - Espessura / 2, Cor, Espessura);
}

public sealed class Estilo
{
    public double Tamanho = 10, Entrelinha = 12;
    public Fonte Fonte = Fonte.Normal;
    public Cor Cor = new(0, 0, 0);
    public double Antes, Depois;
    public Estilo Com(Action<Estilo> a) { var e = (Estilo)MemberwiseClone(); a(e); return e; }
}

/// <summary>Parágrafo com quebra de linha e marcação simples (&lt;b&gt;, &amp;nbsp;).</summary>
public sealed class Paragrafo : Elemento
{
    readonly List<(string txt, bool negrito)> _trechos = new();
    public readonly Estilo Est;
    List<List<(string txt, bool negrito)>>? _linhas;
    double _larguraCache = -1;

    public Paragrafo(string marcacao, Estilo est)
    {
        Est = est;
        EspacoAntes = est.Antes;
        EspacoDepois = est.Depois;
        bool neg = false;
        var sb = new StringBuilder();
        int i = 0;
        void Fecha()
        {
            if (sb.Length == 0) return;
            var bruto = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"[ \t\r\n]+", " ");
            _trechos.Add((WebUtility.HtmlDecode(bruto.Replace("&nbsp;", "\u00a0")), neg));
            sb.Clear();
        }
        while (i < marcacao.Length)
        {
            if (marcacao[i] == '<')
            {
                int f = marcacao.IndexOf('>', i);
                if (f > i)
                {
                    var tag = marcacao.Substring(i + 1, f - i - 1).Trim().ToLowerInvariant();
                    if (tag is "b" or "/b") { Fecha(); neg = tag == "b"; i = f + 1; continue; }
                    if (tag is "br" or "br/") { Fecha(); _trechos.Add(("\n", neg)); i = f + 1; continue; }
                }
            }
            sb.Append(marcacao[i]);
            i++;
        }
        Fecha();
    }

    Fonte F(bool neg) => neg ? Fonte.Negrito : Est.Fonte;

    List<List<(string, bool)>> Quebrar(double largura)
    {
        if (_linhas != null && Math.Abs(_larguraCache - largura) < 0.01) return _linhas;
        var linhas = new List<List<(string, bool)>>();
        var atual = new List<(string, bool)>();
        double w = 0;
        foreach (var (txt, neg) in _trechos)
        {
            if (txt == "\n") { linhas.Add(atual); atual = new(); w = 0; continue; }
            var palavras = txt.Split(' ');
            for (int k = 0; k < palavras.Length; k++)
            {
                var p = palavras[k] + (k < palavras.Length - 1 ? " " : "");
                if (p.Length == 0) continue;
                var pw = Fontes.Largura(p.TrimEnd(' '), F(neg), Est.Tamanho);
                if (w > 0 && w + pw > largura)
                {
                    linhas.Add(atual);
                    atual = new();
                    w = 0;
                }
                atual.Add((p, neg));
                w += Fontes.Largura(p, F(neg), Est.Tamanho);
            }
        }
        if (atual.Count > 0 || linhas.Count == 0) linhas.Add(atual);
        _linhas = linhas;
        _larguraCache = largura;
        return linhas;
    }

    public double LarguraNatural() => _trechos.Sum(t => Fontes.Largura(t.txt, F(t.negrito), Est.Tamanho));

    public override double Altura(double largura) => Quebrar(largura).Count * Est.Entrelinha;

    public override void Desenhar(Tela t, double x, double yTopo, double largura)
    {
        var linhas = Quebrar(largura);
        double y = yTopo - Est.Tamanho;   // primeira linha de base
        foreach (var l in linhas)
        {
            double xx = x;
            foreach (var (txt, neg) in l)
            {
                var s = txt;
                t.Texto(xx, y, s, F(neg), Est.Tamanho, Est.Cor);
                xx += Fontes.Largura(s, F(neg), Est.Tamanho);
            }
            y -= Est.Entrelinha;
        }
    }
}

/// <summary>Desenho vetorial com altura fixa (coordenadas locais: origem embaixo à esquerda).</summary>
public sealed class Desenho : Elemento
{
    public readonly double Largura, AlturaFixa;
    readonly List<Action<Tela, double, double>> _ops = new();
    public Desenho(double largura, double altura) { Largura = largura; AlturaFixa = altura; }
    public void Ret(double x, double y, double w, double h, Cor c) => _ops.Add((t, ox, oy) => t.Retangulo(ox + x, oy + y, w, h, c));
    public void Lin(double x1, double y1, double x2, double y2, Cor c, double e = 1) => _ops.Add((t, ox, oy) => t.Linha(ox + x1, oy + y1, ox + x2, oy + y2, c, e));
    public void Txt(double x, double y, string s, Fonte f, double tam, Cor c, char al = 'e') => _ops.Add((t, ox, oy) => t.Texto(ox + x, oy + y, s, f, tam, c, al));
    public override double Altura(double largura) => AlturaFixa;
    public override void Desenhar(Tela t, double x, double yTopo, double largura)
    {
        foreach (var op in _ops) op(t, x, yTopo - AlturaFixa);
    }
}

public sealed class Junto : Elemento
{
    public readonly List<Elemento> Itens;
    public Junto(List<Elemento> itens) { Itens = itens; }
    public override double Altura(double largura)
    {
        double h = 0;
        for (int i = 0; i < Itens.Count; i++)
            h += (i > 0 ? Itens[i].EspacoAntes : 0) + Itens[i].Altura(largura) + Itens[i].EspacoDepois;
        return h;
    }
    public override void Desenhar(Tela t, double x, double yTopo, double largura) => throw new InvalidOperationException();
}

/// <summary>Montagem de páginas A4 com rodapé numerado ("Página X de N").</summary>
public sealed class Documento
{
    public const double A4L = 595.2756, A4A = 841.8898, Mm = 72 / 25.4;
    public double MargemEsq = 18 * Mm, MargemDir = 18 * Mm, MargemTopo = 16 * Mm, MargemBase = 18 * Mm;
    public string Titulo = "", Autor = "QSmart · Insi", Rodape = "";
    public Cor CorRodape = Cor.Hex("#64748B");
    readonly List<Tela> _paginas = new();
    Tela _tela = null!;
    double _y;
    bool _topo;

    public double LarguraUtil => A4L - MargemEsq - MargemDir;

    void NovaPagina()
    {
        _tela = new Tela();
        _paginas.Add(_tela);
        _y = A4A - MargemTopo;
        _topo = true;
    }

    double Disponivel => _y - MargemBase;

    public byte[] Gerar(List<Elemento> historia)
    {
        NovaPagina();
        var fila = new Queue<Elemento>(historia);
        while (fila.Count > 0)
        {
            var e = fila.Dequeue();
            if (e is QuebraPagina) { if (!_topo) NovaPagina(); continue; }
            if (e is Junto j)
            {
                var h = j.Altura(LarguraUtil) + (_topo ? 0 : j.Itens.FirstOrDefault()?.EspacoAntes ?? 0);
                if (h > Disponivel && !_topo && j.Altura(LarguraUtil) <= A4A - MargemTopo - MargemBase) NovaPagina();
                foreach (var x in j.Itens) Colocar(x);
                continue;
            }
            Colocar(e);
        }
        return Serializar();
    }

    void Colocar(Elemento e)
    {
        if (e is QuebraPagina) { if (!_topo) NovaPagina(); return; }
        if (e is Junto j) { foreach (var x in j.Itens) Colocar(x); return; }
        var antes = _topo ? 0 : e.EspacoAntes;
        var h = e.Altura(LarguraUtil);
        if (antes + h > Disponivel)
        {
            var div = e.Dividir(LarguraUtil, Disponivel - antes);
            if (div != null)
            {
                _y -= antes;
                Desenhar(div.Value.cabe);
                NovaPagina();
                Colocar(div.Value.resto);
                return;
            }
            if (!_topo) { NovaPagina(); antes = 0; }
        }
        _y -= antes;
        Desenhar(e);
        _y -= e.EspacoDepois;
    }

    void Desenhar(Elemento e)
    {
        var h = e.Altura(LarguraUtil);
        e.Desenhar(_tela, MargemEsq, _y, LarguraUtil);
        _y -= h;
        _topo = false;
    }

    byte[] Serializar()
    {
        int total = _paginas.Count;
        for (int i = 0; i < total; i++)
        {
            var t = _paginas[i];
            t.Texto(MargemEsq, 10 * Mm, Rodape, Fonte.Normal, 7.5, CorRodape);
            t.Texto(A4L - MargemDir, 10 * Mm, $"Página {i + 1} de {total}", Fonte.Normal, 7.5, CorRodape, 'd');
        }
        var objs = new List<byte[]>();
        int Add(string s) { objs.Add(Encoding.Latin1.GetBytes(s)); return objs.Count; }
        int AddBytes(byte[] b) { objs.Add(b); return objs.Count; }
        // 1 catálogo, 2 páginas, 3-5 fontes, 6 info
        objs.Add(Array.Empty<byte>()); objs.Add(Array.Empty<byte>());
        Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Oblique /Encoding /WinAnsiEncoding >>");
        string Lit(string s) => Fontes.Literal(s);
        Add($"<< /Title {Lit(Titulo)} /Author {Lit(Autor)} /Producer (QSmart) /Creator (QSmart \\267 Insi) /CreationDate (D:{DateTime.Now:yyyyMMddHHmmss}) >>");
        var kids = new List<int>();
        foreach (var t in _paginas)
        {
            var conteudo = Encoding.Latin1.GetBytes(t.Ops.ToString());
            byte[] comp;
            using (var ms = new MemoryStream())
            {
                using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(conteudo);
                comp = ms.ToArray();
            }
            var head = Encoding.Latin1.GetBytes($"<< /Length {comp.Length} /Filter /FlateDecode >>\nstream\n");
            var tail = Encoding.Latin1.GetBytes("\nendstream");
            int c = AddBytes(head.Concat(comp).Concat(tail).ToArray());
            int p = Add(string.Format(CultureInfo.InvariantCulture,
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {0:0.####} {1:0.####}] /Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R >> >> /Contents {2} 0 R >>", A4L, A4A, c));
            kids.Add(p);
        }
        objs[0] = Encoding.Latin1.GetBytes("<< /Type /Catalog /Pages 2 0 R >>");
        objs[1] = Encoding.Latin1.GetBytes($"<< /Type /Pages /Kids [{string.Join(" ", kids.Select(k => k + " 0 R"))}] /Count {kids.Count} >>");
        using var saida = new MemoryStream();
        void W(string s) { var b = Encoding.Latin1.GetBytes(s); saida.Write(b); }
        W("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");
        var offsets = new List<long>();
        for (int i = 0; i < objs.Count; i++)
        {
            offsets.Add(saida.Position);
            W($"{i + 1} 0 obj\n");
            saida.Write(objs[i]);
            W("\nendobj\n");
        }
        long xref = saida.Position;
        W($"xref\n0 {objs.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) W($"{o:0000000000} 00000 n \n");
        W($"trailer\n<< /Size {objs.Count + 1} /Root 1 0 R /Info 6 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return saida.ToArray();
    }
}
