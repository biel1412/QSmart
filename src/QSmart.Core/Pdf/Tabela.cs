namespace QSmart.Core.Pdf;

/// <summary>Comando de estilo de tabela (equivalente ao TableStyle do reportlab). Índices negativos contam do fim.</summary>
public sealed record EstiloTab(string Cmd, (int c, int r) De, (int c, int r) Ate, object? A = null, object? B = null);

/// <summary>Tabela com cabeçalho repetido, zebra, cores por célula e divisão entre páginas.</summary>
public sealed class TabelaPdf : Elemento
{
    sealed class Cel
    {
        public Cor? Fundo;
        public Cor Texto = new(0, 0, 0);
        public Fonte Fonte = Fonte.Normal;
        public double Tam = 10;
        public char Alinh = 'e';
        public char VAlinh = 'b';
        public double PE = 6, PD = 6, PT = 3, PB = 3;
    }

    sealed record LinhaDec(bool Abaixo, int C0, int C1, double Esp, Cor Cor);

    readonly List<object?[]> _dados;
    readonly List<Cel[]> _cels;
    readonly List<List<LinhaDec>> _decs;
    readonly double[] _larguras;
    readonly int _repetir;
    readonly List<EstiloTab> _grade;   // BOX / INNERGRID / LINEBEFORE (só em tabelas que não se dividem)
    double[]? _alturas;

    public TabelaPdf(List<object?[]> dados, double[] larguras, List<EstiloTab> estilos, int repetirLinhas = 0)
    {
        _dados = dados;
        _larguras = larguras;
        _repetir = repetirLinhas;
        int nr = dados.Count, nc = larguras.Length;
        _cels = Enumerable.Range(0, nr).Select(_ => Enumerable.Range(0, nc).Select(_ => new Cel()).ToArray()).ToList();
        _decs = Enumerable.Range(0, nr).Select(_ => new List<LinhaDec>()).ToList();
        _grade = new();
        foreach (var e in estilos)
        {
            int Ix(int v, int n) => v < 0 ? n + v : v;
            int c0 = Math.Max(0, Ix(e.De.c, nc)), r0 = Math.Max(0, Ix(e.De.r, nr)), c1 = Math.Min(nc - 1, Ix(e.Ate.c, nc)), r1 = Math.Min(nr - 1, Ix(e.Ate.r, nr));
            if (e.Cmd is "BOX" or "INNERGRID" or "LINEBEFORE") { _grade.Add(e with { De = (c0, r0), Ate = (c1, r1) }); continue; }
            for (int r = r0; r <= r1; r++)
            {
                if (e.Cmd is "LINEBELOW" or "LINEABOVE")
                {
                    _decs[r].Add(new LinhaDec(e.Cmd == "LINEBELOW", c0, c1, Convert.ToDouble(e.A), (Cor)e.B!));
                    continue;
                }
                for (int c = c0; c <= c1; c++)
                {
                    var x = _cels[r][c];
                    switch (e.Cmd)
                    {
                        case "ROWBACKGROUNDS": { var cores = (Cor[])e.A!; x.Fundo = cores[(r - r0) % cores.Length]; break; }
                        case "BACKGROUND": x.Fundo = (Cor)e.A!; break;
                        case "TEXTCOLOR": x.Texto = (Cor)e.A!; break;
                        case "FONTNAME": x.Fonte = (Fonte)e.A!; break;
                        case "FONTSIZE": x.Tam = Convert.ToDouble(e.A); break;
                        case "ALIGN": x.Alinh = (char)e.A!; break;
                        case "VALIGN": x.VAlinh = (char)e.A!; break;
                        case "LEFTPADDING": x.PE = Convert.ToDouble(e.A); break;
                        case "RIGHTPADDING": x.PD = Convert.ToDouble(e.A); break;
                        case "TOPPADDING": x.PT = Convert.ToDouble(e.A); break;
                        case "BOTTOMPADDING": x.PB = Convert.ToDouble(e.A); break;
                    }
                }
            }
        }
    }

    TabelaPdf(List<object?[]> dados, List<Cel[]> cels, List<List<LinhaDec>> decs, double[] larguras, int repetir)
    {
        _dados = dados; _cels = cels; _decs = decs; _larguras = larguras; _repetir = repetir; _grade = new();
    }

    int NR => _dados.Count;
    int NC => _larguras.Length;

    double AlturaCelula(int r, int c)
    {
        var x = _cels[r][c];
        var v = _dados[r][c];
        double conteudo = v switch
        {
            List<Elemento> els => els.Sum(e => e.Altura(_larguras[c] - x.PE - x.PD)),
            Desenho d => d.AlturaFixa,
            _ => x.Tam * 1.2,
        };
        return conteudo + x.PT + x.PB;
    }

    double[] Alturas()
    {
        if (_alturas != null) return _alturas;
        _alturas = new double[NR];
        for (int r = 0; r < NR; r++)
            for (int c = 0; c < NC; c++) _alturas[r] = Math.Max(_alturas[r], AlturaCelula(r, c));
        return _alturas;
    }

    public override double Altura(double largura) => Alturas().Sum();

    public override (Elemento cabe, Elemento resto)? Dividir(double largura, double disponivel)
    {
        var al = Alturas();
        double h = 0;
        int n = 0;
        while (n < NR && h + al[n] <= disponivel) { h += al[n]; n++; }
        if (n <= _repetir || n >= NR) return null;
        var a = new TabelaPdf(_dados.Take(n).ToList(), _cels.Take(n).ToList(), _decs.Take(n).ToList(), _larguras, _repetir) { EspacoAntes = EspacoAntes };
        var idx = Enumerable.Range(0, _repetir).Concat(Enumerable.Range(n, NR - n)).ToList();
        var b = new TabelaPdf(idx.Select(i => _dados[i]).ToList(), idx.Select(i => _cels[i]).ToList(), idx.Select(i => _decs[i]).ToList(), _larguras, _repetir)
            { EspacoDepois = EspacoDepois };
        return (a, b);
    }

    public override void Desenhar(Tela t, double x0, double yTopo, double largura)
    {
        var al = Alturas();
        var xs = new double[NC + 1];
        xs[0] = x0;
        for (int c = 0; c < NC; c++) xs[c + 1] = xs[c] + _larguras[c];
        var tops = new double[NR + 1];
        tops[0] = yTopo;
        for (int r = 0; r < NR; r++) tops[r + 1] = tops[r] - al[r];
        for (int r = 0; r < NR; r++)
            for (int c = 0; c < NC; c++)
                if (_cels[r][c].Fundo is Cor f) t.Retangulo(xs[c], tops[r + 1], _larguras[c], al[r], f);
        for (int r = 0; r < NR; r++)
            for (int c = 0; c < NC; c++)
            {
                var cel = _cels[r][c];
                var v = _dados[r][c];
                double cx = xs[c] + cel.PE, cw = _larguras[c] - cel.PE - cel.PD;
                switch (v)
                {
                    case List<Elemento> els:
                        double yy = tops[r] - cel.PT;
                        foreach (var e in els) { e.Desenhar(t, cx, yy, cw); yy -= e.Altura(cw); }
                        break;
                    case Desenho d:
                        d.Desenhar(t, cx, tops[r] - cel.PT, cw);
                        break;
                    case null:
                        break;
                    default:
                        var s = Fontes.Cortar(v.ToString() ?? "", _larguras[c] - 2, cel.Fonte, cel.Tam);
                        double baseY = cel.VAlinh switch
                        {
                            'm' => (tops[r] + tops[r + 1]) / 2 - cel.Tam * 0.35,
                            't' => tops[r] - cel.PT - cel.Tam,
                            _ => tops[r + 1] + cel.PB + cel.Tam * 0.2,
                        };
                        double tx = cel.Alinh switch { 'c' => xs[c] + _larguras[c] / 2, 'd' => xs[c + 1] - cel.PD, _ => cx };
                        t.Texto(tx, baseY, s, cel.Fonte, cel.Tam, cel.Texto, cel.Alinh);
                        break;
                }
            }
        for (int r = 0; r < NR; r++)
            foreach (var d in _decs[r])
            {
                var y = d.Abaixo ? tops[r + 1] : tops[r];
                t.Linha(xs[d.C0], y, xs[d.C1 + 1], y, d.Cor, d.Esp);
            }
        foreach (var e in _grade)
        {
            var (c0, r0, c1, r1) = (e.De.c, e.De.r, e.Ate.c, e.Ate.r);
            var esp = Convert.ToDouble(e.A);
            var cor = (Cor)e.B!;
            switch (e.Cmd)
            {
                case "LINEBEFORE":
                    for (int c = c0; c <= c1; c++) t.Linha(xs[c] + esp / 2, tops[r0], xs[c] + esp / 2, tops[r1 + 1], cor, esp);
                    break;
                case "BOX":
                    t.Retangulo(xs[c0], tops[r1 + 1], xs[c1 + 1] - xs[c0], tops[r0] - tops[r1 + 1], null, cor, esp);
                    break;
                case "INNERGRID":
                    for (int c = c0 + 1; c <= c1; c++) t.Linha(xs[c], tops[r0], xs[c], tops[r1 + 1], cor, esp);
                    for (int r = r0 + 1; r <= r1; r++) t.Linha(xs[c0], tops[r], xs[c1 + 1], tops[r], cor, esp);
                    break;
            }
        }
    }
}
