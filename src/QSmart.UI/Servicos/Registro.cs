using Microsoft.Extensions.DependencyInjection;

namespace QSmart.UI.Servicos;

public static class Registro
{
    public static IServiceCollection AddQSmart(this IServiceCollection s)
    {
        s.AddScoped<Navegador>();
        s.AddScoped<Dados>();
        s.AddScoped<Janelas>();
        s.AddScoped<Estado>();
        return s;
    }
}
