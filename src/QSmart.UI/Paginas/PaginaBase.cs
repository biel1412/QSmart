using Microsoft.AspNetCore.Components;
using QSmart.UI.Servicos;

namespace QSmart.UI.Paginas;

/// <summary>Base das páginas: acesso ao estado, redesenho quando algo muda e bloqueio de abas desativadas.</summary>
public abstract class PaginaBase : ComponentBase, IDisposable
{
    [Inject] protected Estado E { get; set; } = null!;
    [Inject] protected NavigationManager Rotas { get; set; } = null!;
    protected Janelas J => E.Janelas;
    protected abstract string Chave { get; }
    protected Aba A => Abas.Por(Chave)!;

    protected override void OnInitialized()
    {
        E.Mudou += Atualizar;
        Entrar();
    }

    protected virtual void Entrar() { }

    void Atualizar() => InvokeAsync(StateHasChanged);

    public virtual void Dispose() => E.Mudou -= Atualizar;

    protected static string Fmt(long n) => Core.Texto.Fmt(n);
    protected static string F1(double x) => Core.Texto.F(x);
    protected static string Fv(double x) => Core.Texto.Fv(x);
    protected static string Cor(string nome) => $"var(--{nome})";
    protected static string CorOrigem(string o) => o switch { "Interno" => "var(--blue)", "Externo" => "var(--amber)", "Chamados" => "var(--purple)", _ => "var(--muted)" };
}
