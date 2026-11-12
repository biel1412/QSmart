using QSmart.Core;

namespace QSmart.UI.Servicos;

/// <summary>Uma janela (diálogo) aberta: componente + parâmetros + resultado aguardado.</summary>
public sealed class JanelaAberta
{
    public Type Tipo { get; init; } = null!;
    public Dictionary<string, object?> Parametros { get; init; } = new();
    public TaskCompletionSource<object?> Resultado { get; } = new();
    public int Id { get; init; }
}

/// <summary>Serviço de diálogos: mensagens, perguntas e janelas de escolha (equivalentes às QDialogs do original).</summary>
public sealed class Janelas
{
    readonly List<JanelaAberta> _abertas = new();
    int _seq;
    public IReadOnlyList<JanelaAberta> Abertas => _abertas;
    public event Action? Mudou;

    public async Task<T?> Abrir<T>(Type componente, Dictionary<string, object?>? pars = null)
    {
        var j = new JanelaAberta { Tipo = componente, Parametros = pars ?? new(), Id = ++_seq };
        j.Parametros["Fechar"] = (Action<object?>)(v =>
        {
            _abertas.Remove(j);
            Mudou?.Invoke();
            j.Resultado.TrySetResult(v);
        });
        _abertas.Add(j);
        Mudou?.Invoke();
        var r = await j.Resultado.Task;
        return r is T t ? t : default;
    }

    public Task Mensagem(string tipo, string titulo, string texto)
        => Abrir<object>(typeof(Dialogos.DlgMensagem), new() { ["Tipo"] = tipo, ["Titulo"] = titulo, ["Texto"] = texto });

    public Task Info(string t, string m) => Mensagem("info", t, m);
    public Task Aviso(string t, string m) => Mensagem("aviso", t, m);
    public Task Erro(string t, string m) => Mensagem("erro", t, m);

    public async Task<bool> Pergunta(string titulo, string texto)
        => await Abrir<bool?>(typeof(Dialogos.DlgMensagem), new() { ["Tipo"] = "pergunta", ["Titulo"] = titulo, ["Texto"] = texto }) == true;

    public Task<string?> Entrada(string titulo, string rotulo, string valor, bool multilinha = false)
        => Abrir<string>(typeof(Dialogos.DlgEntrada), new() { ["Titulo"] = titulo, ["Rotulo"] = rotulo, ["Valor"] = valor, ["Multilinha"] = multilinha });

    /// <summary>Lista pesquisável com caixas de marcação. opcoes = [(valor, rótulo)].</summary>
    public Task<HashSet<string>?> Multi(string titulo, IEnumerable<(string valor, string rotulo)> opcoes, IEnumerable<string> atuais, string dica = "")
        => Abrir<HashSet<string>>(typeof(Dialogos.DlgMulti), new() { ["Titulo"] = titulo, ["Opcoes"] = opcoes.ToList(), ["Atuais"] = atuais.ToHashSet(), ["Dica"] = dica });

    /// <summary>Escolha de um item. Devolve null (cancelou), "" (limpou) ou o valor.</summary>
    public Task<string?> Um(string titulo, IEnumerable<(string valor, string rotulo)> opcoes, string atual = "", string dica = "", string limpar = "Limpar escolha")
        => Abrir<string>(typeof(Dialogos.DlgUm), new() { ["Titulo"] = titulo, ["Opcoes"] = opcoes.ToList(), ["Atual"] = atual, ["Dica"] = dica, ["Limpar"] = limpar });

    public Task<Dictionary<string, string>?> Classes(List<(string proc, long n)> procs, IReadOnlyDictionary<string, string> classes)
        => Abrir<Dictionary<string, string>>(typeof(Dialogos.DlgClasses), new() { ["Procs"] = procs, ["ClassesAtuais"] = classes });

    public Task Tabela(string titulo, string subtitulo, List<Componentes.ColunaTab> colunas, List<Componentes.LinhaTab> linhas, string rodape, bool larga = false)
        => Abrir<object>(typeof(Dialogos.DlgTabela), new() { ["Titulo"] = titulo, ["Subtitulo"] = subtitulo, ["Colunas"] = colunas, ["Linhas"] = linhas, ["Rodape"] = rodape, ["Larga"] = larga });

    public Task Texto(string titulo, string subtitulo, string texto, List<(string rotulo, string variante, Func<string, Task<string>> acao)> acoes)
        => Abrir<object>(typeof(Dialogos.DlgTexto), new() { ["Titulo"] = titulo, ["Subtitulo"] = subtitulo, ["Texto"] = texto, ["Acoes"] = acoes });

    public Task Configuracoes() => Abrir<object>(typeof(Dialogos.DlgConfig));
}
