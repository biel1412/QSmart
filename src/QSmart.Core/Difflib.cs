namespace QSmart.Core;

/// <summary>Port fiel do difflib.SequenceMatcher / get_close_matches do Python (mesmas notas e desempates).</summary>
public sealed class SequenceMatcher
{
    string _a = "", _b = "";
    Dictionary<char, List<int>> _b2j = new();
    HashSet<char> _bpopular = new();
    Dictionary<char, int>? _fullbcount;
    List<(int, int, int)>? _blocks;

    public void SetSeq1(string a) { _a = a; _blocks = null; }

    public void SetSeq2(string b)
    {
        _b = b; _blocks = null; _fullbcount = null;
        _b2j = new Dictionary<char, List<int>>();
        for (int i = 0; i < b.Length; i++)
        {
            if (!_b2j.TryGetValue(b[i], out var l)) _b2j[b[i]] = l = new List<int>();
            l.Add(i);
        }
        _bpopular = new HashSet<char>();
        int n = b.Length;
        if (n >= 200)
        {
            int ntest = n / 100 + 1;
            foreach (var kv in _b2j.ToList())
                if (kv.Value.Count > ntest) { _bpopular.Add(kv.Key); _b2j.Remove(kv.Key); }
        }
    }

    (int, int, int) FindLongestMatch(int alo, int ahi, int blo, int bhi)
    {
        string a = _a, b = _b;
        int besti = alo, bestj = blo, bestsize = 0;
        var j2len = new Dictionary<int, int>();
        for (int i = alo; i < ahi; i++)
        {
            var newj2len = new Dictionary<int, int>();
            if (_b2j.TryGetValue(a[i], out var js))
            {
                foreach (var j in js)
                {
                    if (j < blo) continue;
                    if (j >= bhi) break;
                    int k = (j2len.TryGetValue(j - 1, out var v) ? v : 0) + 1;
                    newj2len[j] = k;
                    if (k > bestsize) { besti = i - k + 1; bestj = j - k + 1; bestsize = k; }
                }
            }
            j2len = newj2len;
        }
        // bjunk é vazio (isjunk=None); elementos "populares" não são junk
        while (besti > alo && bestj > blo && a[besti - 1] == b[bestj - 1]) { besti--; bestj--; bestsize++; }
        while (besti + bestsize < ahi && bestj + bestsize < bhi && a[besti + bestsize] == b[bestj + bestsize]) bestsize++;
        return (besti, bestj, bestsize);
    }

    public List<(int, int, int)> GetMatchingBlocks()
    {
        if (_blocks != null) return _blocks;
        int la = _a.Length, lb = _b.Length;
        var fila = new Stack<(int, int, int, int)>();
        fila.Push((0, la, 0, lb));
        var blocos = new List<(int, int, int)>();
        while (fila.Count > 0)
        {
            var (alo, ahi, blo, bhi) = fila.Pop();
            var (i, j, k) = FindLongestMatch(alo, ahi, blo, bhi);
            if (k > 0)
            {
                blocos.Add((i, j, k));
                if (alo < i && blo < j) fila.Push((alo, i, blo, j));
                if (i + k < ahi && j + k < bhi) fila.Push((i + k, ahi, j + k, bhi));
            }
        }
        blocos.Sort();
        var res = new List<(int, int, int)>();
        int i1 = 0, j1 = 0, k1 = 0;
        foreach (var (i2, j2, k2) in blocos)
        {
            if (i1 + k1 == i2 && j1 + k1 == j2) k1 += k2;
            else
            {
                if (k1 > 0) res.Add((i1, j1, k1));
                i1 = i2; j1 = j2; k1 = k2;
            }
        }
        if (k1 > 0) res.Add((i1, j1, k1));
        res.Add((la, lb, 0));
        _blocks = res;
        return res;
    }

    static double CalcRatio(int matches, int length) => length > 0 ? 2.0 * matches / length : 1.0;

    public double Ratio() => CalcRatio(GetMatchingBlocks().Sum(t => t.Item3), _a.Length + _b.Length);

    public double QuickRatio()
    {
        if (_fullbcount == null)
        {
            _fullbcount = new Dictionary<char, int>();
            foreach (var c in _b) _fullbcount[c] = _fullbcount.GetValueOrDefault(c) + 1;
        }
        var avail = new Dictionary<char, int>();
        int matches = 0;
        foreach (var c in _a)
        {
            int numb = avail.TryGetValue(c, out var v) ? v : _fullbcount.GetValueOrDefault(c);
            avail[c] = numb - 1;
            if (numb > 0) matches++;
        }
        return CalcRatio(matches, _a.Length + _b.Length);
    }

    public double RealQuickRatio()
    {
        int la = _a.Length, lb = _b.Length;
        return CalcRatio(Math.Min(la, lb), la + lb);
    }
}

public static class Difflib
{
    /// <summary>get_close_matches(word, possibilities, n, cutoff).</summary>
    public static List<string> CloseMatches(string word, IEnumerable<string> possibilidades, int n = 3, double cutoff = 0.6)
    {
        var res = new List<(double, string)>();
        var s = new SequenceMatcher();
        s.SetSeq2(word);
        foreach (var x in possibilidades)
        {
            s.SetSeq1(x);
            if (s.RealQuickRatio() >= cutoff && s.QuickRatio() >= cutoff && s.Ratio() >= cutoff)
                res.Add((s.Ratio(), x));
        }
        return res.OrderByDescending(t => t.Item1).ThenByDescending(t => t.Item2, StringComparer.Ordinal).Take(n).Select(t => t.Item2).ToList();
    }
}
