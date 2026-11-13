using System.Text.Json;
using System.Text.Json.Serialization;
using QSmart.Core.Planilhas;

namespace QSmart.Core.Coordenacao;

/// <summary>Item da EAP (fase, entrega ou pacote de trabalho). A ordem na lista do projeto é a ordem entre irmãos.</summary>
public sealed class ItemEap
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Item pai (null = primeiro nível, abaixo do projeto).</summary>
    public string? PaiId { get; set; }
    public string Nome { get; set; } = "";
    public string Responsavel { get; set; } = "";
    public DateOnly? Inicio { get; set; }
    public DateOnly? Fim { get; set; }
    public double? Custo { get; set; }
    /// <summary>Não iniciado, Em andamento ou Concluído (nos itens com subitens é calculado).</summary>
    public string Status { get; set; } = Eap.NaoIniciado;
    /// <summary>0 a 100 (nos itens com subitens é calculado).</summary>
    public int Progresso { get; set; }
    public string Notas { get; set; } = "";
}

public sealed class ProjetoEap
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Nome { get; set; } = "";
    public string Descricao { get; set; } = "";
    public string Gerente { get; set; } = "";
    public DateTime CriadoEm { get; set; } = DateTime.Now;
    public List<ItemEap> Itens { get; set; } = new();
}

public sealed class DadosEap
{
    public int Versao { get; set; } = 1;
    public List<ProjetoEap> Projetos { get; set; } = new();
    public string? Selecionado { get; set; }
}

/// <summary>Nó calculado da árvore: código, nível e valores somados dos subitens.</summary>
public sealed class NoEap
{
    public ItemEap Item = null!;
    public NoEap? Pai;
    public string Codigo = "";
    public int Nivel;                      // 1 = fase
    public List<NoEap> Filhos = new();
    public DateOnly? Inicio, Fim;
    public double Custo;
    public double Progresso;               // 0..100
    public string Status = Eap.NaoIniciado;
    public bool Atrasado;
    public int Pacotes;                    // itens sem subitens abaixo (ou 1, se for um)
    public bool Folha => Filhos.Count == 0;
    public string TipoNivel => Folha && Nivel > 1 ? "Pacote de trabalho" : Nivel switch { 1 => "Fase", 2 => "Entrega", _ => "Subentrega" };
}

public sealed class ResumoEap
{
    public int Itens, Pacotes, Concluidos, EmAndamento, Atrasados, SemResponsavel;
    public double Custo, Progresso;
    public DateOnly? Inicio, Fim;
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DadosEap))]
internal partial class JsonEap : JsonSerializerContext { }

public static class Eap
{
    public const string NaoIniciado = "Não iniciado", EmAndamento = "Em andamento", Concluido = "Concluído";
    public static readonly string[] Status = { NaoIniciado, EmAndamento, Concluido };

    static IEnumerable<ItemEap> FilhosDe(ProjetoEap p, string? paiId) => p.Itens.Where(i => i.PaiId == paiId);

    /// <summary>Monta a árvore com códigos (1, 1.1, 1.1.1…) e valores somados de baixo para cima.</summary>
    public static List<NoEap> Arvore(ProjetoEap p, DateOnly hoje)
    {
        var ids = p.Itens.Select(i => i.Id).ToHashSet();
        foreach (var i in p.Itens) if (i.PaiId != null && (!ids.Contains(i.PaiId) || i.PaiId == i.Id)) i.PaiId = null;   // pai inexistente: sobe para o 1º nível
        var visitados = new HashSet<string>();
        List<NoEap> Montar(string? paiId, NoEap? pai, string prefixo, int nivel)
        {
            var lista = new List<NoEap>();
            int n = 0;
            foreach (var it in FilhosDe(p, paiId).ToList())
            {
                if (!visitados.Add(it.Id)) continue;             // proteção contra ciclos
                n++;
                var no = new NoEap { Item = it, Pai = pai, Codigo = prefixo + n, Nivel = nivel };
                no.Filhos = Montar(it.Id, no, no.Codigo + ".", nivel + 1);
                Calcular(no, hoje);
                lista.Add(no);
            }
            return lista;
        }
        var raiz = Montar(null, null, "", 1);
        // itens perdidos em ciclos voltam ao 1º nível
        foreach (var it in p.Itens.Where(i => !visitados.Contains(i.Id)).ToList()) { it.PaiId = null; }
        if (p.Itens.Any(i => !visitados.Contains(i.Id))) return Arvore(p, hoje);
        return raiz;
    }

    static void Calcular(NoEap no, DateOnly hoje)
    {
        var it = no.Item;
        if (no.Folha)
        {
            no.Inicio = it.Inicio; no.Fim = it.Fim;
            no.Custo = it.Custo ?? 0;
            no.Status = Status.Contains(it.Status) ? it.Status : NaoIniciado;
            no.Progresso = no.Status == Concluido ? 100 : Math.Clamp(it.Progresso, 0, 100);
            if (no.Progresso >= 100) no.Status = Concluido;
            else if (no.Progresso > 0 && no.Status == NaoIniciado) no.Status = EmAndamento;
            no.Pacotes = 1;
        }
        else
        {
            var ini = no.Filhos.Where(f => f.Inicio != null).Select(f => f.Inicio!.Value).Concat(it.Inicio is DateOnly a ? new[] { a } : Array.Empty<DateOnly>()).ToList();
            var fim = no.Filhos.Where(f => f.Fim != null).Select(f => f.Fim!.Value).Concat(it.Fim is DateOnly b ? new[] { b } : Array.Empty<DateOnly>()).ToList();
            no.Inicio = ini.Count > 0 ? ini.Min() : null;
            no.Fim = fim.Count > 0 ? fim.Max() : null;
            no.Custo = no.Filhos.Sum(f => f.Custo) + (it.Custo ?? 0);
            no.Pacotes = no.Filhos.Sum(f => f.Pacotes);
            no.Progresso = no.Pacotes == 0 ? 0 : no.Filhos.Sum(f => f.Progresso * f.Pacotes) / no.Pacotes;
            no.Status = no.Filhos.All(f => f.Status == Concluido) ? Concluido
                : no.Filhos.Any(f => f.Status != NaoIniciado) ? EmAndamento : NaoIniciado;
        }
        no.Atrasado = no.Fim is DateOnly f2 && f2 < hoje && no.Status != Concluido;
    }

    public static IEnumerable<NoEap> EmOrdem(IEnumerable<NoEap> nos)
    {
        foreach (var n in nos)
        {
            yield return n;
            foreach (var f in EmOrdem(n.Filhos)) yield return f;
        }
    }

    public static ResumoEap Resumo(List<NoEap> arvore)
    {
        var todos = EmOrdem(arvore).ToList();
        var folhas = todos.Where(n => n.Folha).ToList();
        var ini = arvore.Where(n => n.Inicio != null).Select(n => n.Inicio!.Value).ToList();
        var fim = arvore.Where(n => n.Fim != null).Select(n => n.Fim!.Value).ToList();
        int pac = arvore.Sum(n => n.Pacotes);
        return new ResumoEap
        {
            Itens = todos.Count, Pacotes = folhas.Count,
            Concluidos = folhas.Count(n => n.Status == Concluido), EmAndamento = folhas.Count(n => n.Status == EmAndamento),
            Atrasados = folhas.Count(n => n.Atrasado), SemResponsavel = folhas.Count(n => string.IsNullOrWhiteSpace(n.Item.Responsavel)),
            Custo = arvore.Sum(n => n.Custo), Progresso = pac == 0 ? 0 : arvore.Sum(n => n.Progresso * n.Pacotes) / pac,
            Inicio = ini.Count > 0 ? ini.Min() : null, Fim = fim.Count > 0 ? fim.Max() : null,
        };
    }

    // ---------------- edição da estrutura ----------------
    static int IndiceUltimoDescendente(ProjetoEap p, ItemEap it)
    {
        var ids = new HashSet<string> { it.Id };
        int ultimo = p.Itens.IndexOf(it);
        bool mudou = true;
        while (mudou)
        {
            mudou = false;
            for (int i = 0; i < p.Itens.Count; i++)
                if (p.Itens[i].PaiId is string pai && ids.Contains(pai) && ids.Add(p.Itens[i].Id)) { mudou = true; ultimo = Math.Max(ultimo, i); }
        }
        return ultimo;
    }

    public static ItemEap NovoFilho(ProjetoEap p, string? paiId, string nome)
    {
        var it = new ItemEap { PaiId = paiId, Nome = nome };
        var irmaos = FilhosDe(p, paiId).ToList();
        if (irmaos.Count == 0) p.Itens.Add(it);
        else p.Itens.Insert(IndiceUltimoDescendente(p, irmaos[^1]) + 1, it);
        return it;
    }

    public static ItemEap NovoAbaixo(ProjetoEap p, string id, string nome)
    {
        var ref_ = p.Itens.First(i => i.Id == id);
        var it = new ItemEap { PaiId = ref_.PaiId, Nome = nome };
        p.Itens.Insert(IndiceUltimoDescendente(p, ref_) + 1, it);
        return it;
    }

    /// <summary>Sobe (-1) ou desce (+1) o item entre os irmãos.</summary>
    public static bool Mover(ProjetoEap p, string id, int delta)
    {
        var it = p.Itens.First(i => i.Id == id);
        var irmaos = FilhosDe(p, it.PaiId).ToList();
        int k = irmaos.IndexOf(it), alvo = k + delta;
        if (alvo < 0 || alvo >= irmaos.Count) return false;
        var outro = irmaos[alvo];
        // troca as posições relativas: basta trocar os dois na lista (a ordem entre irmãos é a ordem na lista)
        int ia = p.Itens.IndexOf(it), io = p.Itens.IndexOf(outro);
        p.Itens[ia] = outro; p.Itens[io] = it;
        return true;
    }

    /// <summary>Vira subitem do irmão de cima (→).</summary>
    public static bool Avancar(ProjetoEap p, string id)
    {
        var it = p.Itens.First(i => i.Id == id);
        var irmaos = FilhosDe(p, it.PaiId).ToList();
        int k = irmaos.IndexOf(it);
        if (k <= 0) return false;
        var novoPai = irmaos[k - 1];
        p.Itens.Remove(it);
        it.PaiId = novoPai.Id;
        var filhos = FilhosDe(p, novoPai.Id).ToList();
        int pos = filhos.Count == 0 ? p.Itens.IndexOf(novoPai) + 1 : IndiceUltimoDescendente(p, filhos[^1]) + 1;
        p.Itens.Insert(pos, it);
        return true;
    }

    /// <summary>Sobe um nível (←): fica logo depois do antigo pai.</summary>
    public static bool Recuar(ProjetoEap p, string id)
    {
        var it = p.Itens.First(i => i.Id == id);
        if (it.PaiId == null) return false;
        var pai = p.Itens.First(i => i.Id == it.PaiId);
        p.Itens.Remove(it);
        it.PaiId = pai.PaiId;
        p.Itens.Insert(IndiceUltimoDescendente(p, pai) + 1, it);
        return true;
    }

    /// <summary>Exclui o item e tudo abaixo dele. Devolve quantos itens saíram.</summary>
    public static int Excluir(ProjetoEap p, string id)
    {
        var ids = new HashSet<string> { id };
        bool mudou = true;
        while (mudou)
        {
            mudou = false;
            foreach (var i in p.Itens) if (i.PaiId is string pai && ids.Contains(pai) && ids.Add(i.Id)) mudou = true;
        }
        return p.Itens.RemoveAll(i => ids.Contains(i.Id));
    }

    public static int Descendentes(ProjetoEap p, string id)
    {
        var ids = new HashSet<string> { id };
        bool mudou = true;
        while (mudou)
        {
            mudou = false;
            foreach (var i in p.Itens) if (i.PaiId is string pai && ids.Contains(pai) && ids.Add(i.Id)) mudou = true;
        }
        return ids.Count - 1;
    }

    /// <summary>Projeto inicial com as fases clássicas, para não começar do zero.</summary>
    public static ProjetoEap Modelo(string nome)
    {
        var p = new ProjetoEap { Nome = nome };
        foreach (var (fase, entregas) in new[]
        {
            ("Iniciação", new[] { "Termo de abertura", "Partes interessadas" }),
            ("Planejamento", new[] { "Escopo e EAP", "Cronograma", "Orçamento" }),
            ("Execução", new[] { "Entrega 1", "Entrega 2" }),
            ("Monitoramento e controle", new[] { "Relatórios de status" }),
            ("Encerramento", new[] { "Aceite final", "Lições aprendidas" }),
        })
        {
            var f = NovoFilho(p, null, fase);
            foreach (var e in entregas) NovoFilho(p, f.Id, e);
        }
        return p;
    }

    public static ProjetoEap Duplicar(ProjetoEap p)
    {
        var mapa = p.Itens.ToDictionary(i => i.Id, _ => Guid.NewGuid().ToString("N"));
        return new ProjetoEap
        {
            Nome = p.Nome + " (cópia)", Descricao = p.Descricao, Gerente = p.Gerente,
            Itens = p.Itens.Select(i => new ItemEap
            {
                Id = mapa[i.Id], PaiId = i.PaiId != null && mapa.TryGetValue(i.PaiId, out var np) ? np : null,
                Nome = i.Nome, Responsavel = i.Responsavel, Inicio = i.Inicio, Fim = i.Fim, Custo = i.Custo, Status = i.Status, Progresso = i.Progresso, Notas = i.Notas,
            }).ToList(),
        };
    }

    public static string Gravar(DadosEap d) => JsonSerializer.Serialize(d, JsonEap.Default.DadosEap);

    public static DadosEap Ler(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new DadosEap();
        var d = JsonSerializer.Deserialize(json, JsonEap.Default.DadosEap) ?? throw new InvalidDataException("Arquivo de projetos vazio.");
        d.Projetos ??= new();
        foreach (var p in d.Projetos) p.Itens ??= new();
        return d;
    }

    public static byte[] Exportar(ProjetoEap p, DateOnly hoje)
    {
        var arv = Arvore(p, hoje);
        var res = Resumo(arv);
        var wb = new XlsxWriter();
        var s = wb.Planilha("EAP");
        s.Cabecalho("Código", "Item", "Nível", "Responsável", "Início", "Fim", "Dias", "Custo (R$)", "Status", "% concluído", "Atrasado", "Notas");
        var cores = new[] { "E0E7FF", "EEF2FF", "" };
        foreach (var n in EmOrdem(arv))
        {
            s.Linha(n.Codigo, new string(' ', (n.Nivel - 1) * 4) + n.Item.Nome, n.TipoNivel, n.Item.Responsavel,
                n.Inicio is DateOnly a ? Texto.Dmy(a) : "", n.Fim is DateOnly b ? Texto.Dmy(b) : "",
                n.Inicio is DateOnly a2 && n.Fim is DateOnly b2 ? b2.DayNumber - a2.DayNumber + 1 : null,
                n.Custo > 0 ? Math.Round(n.Custo, 2) : null, n.Status, Math.Round(n.Progresso), n.Atrasado ? "Sim" : "", n.Item.Notas);
            if (!n.Folha) { s.NegritoLinha(s.N - 1); if (n.Nivel <= 2) s.PreencherLinha(s.N - 1, cores[n.Nivel - 1]); }
            if (n.Atrasado) s.Preencher(s.N - 1, 10, "FDECEC", true);
        }
        s.Linha("", "TOTAL", "", "", res.Inicio is DateOnly ri ? Texto.Dmy(ri) : "", res.Fim is DateOnly rf ? Texto.Dmy(rf) : "", null,
            res.Custo > 0 ? Math.Round(res.Custo, 2) : null, "", Math.Round(res.Progresso), "", "");
        s.NegritoLinha(s.N - 1);
        s.Congelar(1, 2);
        s.AutoLargura(8, 60);
        var so = wb.Planilha("Projeto");
        so.Cabecalho("Item", "Valor");
        so.Linha("Projeto", p.Nome);
        so.Linha("Descrição", p.Descricao);
        so.Linha("Gerente", p.Gerente);
        so.Linha("Pacotes de trabalho", res.Pacotes);
        so.Linha("Concluídos", res.Concluidos);
        so.Linha("Atrasados", res.Atrasados);
        so.Linha("% concluído", Math.Round(res.Progresso, 1));
        so.Linha("Custo total (R$)", Math.Round(res.Custo, 2));
        so.Linha("Gerado em", Texto.Agora());
        so.AutoLargura(10, 70);
        return wb.Salvar();
    }
}
