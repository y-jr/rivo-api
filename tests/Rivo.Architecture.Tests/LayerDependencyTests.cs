namespace Rivo.Architecture.Tests;

/// <summary>
/// Direcção das dependências entre camadas, verificada sobre o que os
/// assemblies compilados <em>usam</em>
/// (architecture/dependency-rules.md §"Direcção entre camadas").
///
/// <code>
/// API → Application → Domain
/// Infrastructure → Application / Domain
/// </code>
///
/// <para>
/// As regras sobre referências declaradas vivem em
/// <see cref="ProjectReferenceTests"/>. Aqui ficam as que só o assembly
/// compilado revela — designadamente o que chega por via transitiva, que
/// nenhum `.csproj` mostra.
/// </para>
/// </summary>
public class LayerDependencyTests
{
    /// <summary>
    /// O domínio não conhece framework: nem ORM, nem ASP.NET Core, nem SDKs
    /// externos.
    ///
    /// <para>
    /// Complementa a verificação de pacotes declarados: um pacote pode chegar
    /// por via transitiva sem aparecer no `.csproj` do domínio, e é por aí que
    /// o vazamento costuma entrar — um atributo de EF Core numa entidade, um
    /// `IFormFile` numa fábrica.
    /// </para>
    ///
    /// <para>
    /// É a regra de que depende toda a estratégia de teste: se uma invariante
    /// precisasse de base de dados para ser testada, teria vazado da camada —
    /// e os 100 testes de domínio (ADR-022), que correm em menos de dois
    /// segundos sem Docker, deixariam de ser possíveis.
    /// </para>
    /// </summary>
    [Fact]
    public void Domain_KnowsNoFramework()
    {
        string[] proibidos =
        [
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore",
            "Microsoft.Extensions.DependencyInjection",
            "Microsoft.Extensions.Configuration",
            "Microsoft.Extensions.Hosting",
            "Npgsql",
            "System.IdentityModel",
        ];

        var violations = new List<string>();

        foreach (var assembly in RivoAssemblies.InLayer(RivoAssemblies.DomainLayer))
        {
            violations.AddRange(assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name!)
                .Where(name => proibidos.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
                .Select(name => $"{RivoAssemblies.Name(assembly)} -> {name}"));
        }

        Assert.Empty(violations);
    }

    /// <summary>
    /// Nada usa `Infrastructure` para fora — excepto o host.
    ///
    /// <para>
    /// <strong>O host é a excepção declarada</strong>, e é deliberada:
    /// `Rivo.Api` é o composition root e tem de registar implementações
    /// concretas no contentor de DI. A alternativa seria varrimento de
    /// assemblies — mais magia, não menos acoplamento
    /// (dependency-rules.md §API).
    /// </para>
    ///
    /// <para>
    /// É o que garante que trocar a implementação de um port — o
    /// `IDocumentStorage` de sistema de ficheiros por armazenamento de
    /// objectos, quando o K11 for resolvido — não obriga a tocar em nada acima
    /// dela.
    /// </para>
    /// </summary>
    [Fact]
    public void Infrastructure_IsUsedOnlyByTheHost()
    {
        var violations = new List<string>();

        foreach (var assembly in RivoAssemblies.All)
        {
            if (RivoAssemblies.Name(assembly) == RivoAssemblies.Host)
            {
                continue;
            }

            violations.AddRange(RivoAssemblies
                .RivoReferences(assembly)
                .Where(reference => RivoAssemblies.Layer(reference) == RivoAssemblies.InfrastructureLayer)
                .Select(reference => $"{RivoAssemblies.Name(assembly)} -> {reference}"));
        }

        Assert.Empty(violations);
    }

    /// <summary>
    /// Nenhum ciclo entre módulos, mesmo passando por contratos.
    ///
    /// <para>
    /// O ADR-017 garante que o <em>compilador</em> aceita `A → B.Contracts` e
    /// `B → A.Contracts` em simultâneo. Isso resolve a compilação, não o
    /// desenho: dois módulos que se leem mutuamente continuam acoplados. Este
    /// teste torna essa situação visível em vez de silenciosa — e vai importar
    /// quando `hr ↔ approval` existir (ADR-015 §R1).
    /// </para>
    /// </summary>
    [Fact]
    public void Modules_HaveNoDependencyCycles()
    {
        var graph = RivoAssemblies.Modules
            .GroupBy(RivoAssemblies.Module)
            .ToDictionary(
                group => group.Key,
                group => group
                    .SelectMany(RivoAssemblies.RivoReferences)
                    .Select(RivoAssemblies.Module)
                    .Where(target => target != group.Key)
                    .Distinct()
                    .ToList());

        var cycles = new List<string>();

        foreach (var origin in graph.Keys)
        {
            Walk(origin, [origin], graph, cycles);
        }

        Assert.Empty(cycles);
    }

    private static void Walk(
        string current,
        List<string> path,
        Dictionary<string, List<string>> graph,
        List<string> cycles)
    {
        foreach (var next in graph.GetValueOrDefault(current, []))
        {
            if (next == path[0])
            {
                // Regista o ciclo uma so vez, a partir do modulo
                // alfabeticamente menor: senao o mesmo ciclo apareceria uma vez
                // por participante.
                if (path[0] == path.Min(StringComparer.Ordinal))
                {
                    cycles.Add(string.Join(" -> ", path.Concat([next])));
                }

                continue;
            }

            if (path.Contains(next))
            {
                continue;
            }

            Walk(next, [.. path, next], graph, cycles);
        }
    }
}
