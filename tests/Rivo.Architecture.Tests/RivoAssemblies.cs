using System.Reflection;

namespace Rivo.Architecture.Tests;

/// <summary>
/// Descoberta dos assemblies do Rivo e do seu lugar na arquitectura.
///
/// <para>
/// Os assemblies são descobertos a partir do directório de saída, e não de uma
/// lista escrita à mão. A diferença importa: uma lista esquecida deixaria um
/// módulo novo por verificar, e um teste de arquitectura que silenciosamente
/// não cobre um módulo é pior do que não existir — dá confiança sem a
/// sustentar.
/// </para>
/// </summary>
internal static class RivoAssemblies
{
    /// <summary>Host da aplicação. É o composition root, e tem regras próprias.</summary>
    internal const string Host = "Rivo.Api";

    internal const string DomainLayer = "Domain";
    internal const string ApplicationLayer = "Application";
    internal const string InfrastructureLayer = "Infrastructure";
    internal const string ApiLayer = "Api";
    internal const string ContractsLayer = "Contracts";

    /// <summary>
    /// Todos os assemblies `Rivo.*` presentes na saída do build, excluindo os
    /// projectos de teste.
    /// </summary>
    internal static IReadOnlyList<Assembly> All { get; } = Discover();

    /// <summary>Assemblies de módulo — tudo excepto o host.</summary>
    internal static IReadOnlyList<Assembly> Modules { get; } =
        [.. All.Where(a => Name(a) != Host)];

    internal static string Name(Assembly assembly) => assembly.GetName().Name!;

    /// <summary>
    /// Nome do módulo a que um assembly pertence: `Rivo.Hr.Domain` → `Hr`.
    /// Devolve o nome inteiro para o host, que não pertence a módulo nenhum.
    /// </summary>
    internal static string Module(Assembly assembly) => Module(Name(assembly));

    internal static string Module(string assemblyName)
    {
        var parts = assemblyName.Split('.');
        return parts.Length >= 3 ? parts[1] : assemblyName;
    }

    /// <summary>Camada de um assembly: `Rivo.Hr.Domain` → `Domain`.</summary>
    internal static string Layer(Assembly assembly) => Layer(Name(assembly));

    internal static string Layer(string assemblyName)
    {
        var parts = assemblyName.Split('.');
        return parts.Length >= 3 ? parts[^1] : string.Empty;
    }

    internal static bool IsRivo(string assemblyName) =>
        assemblyName.StartsWith("Rivo.", StringComparison.Ordinal);

    /// <summary>Assemblies `Rivo.*` que um assembly referencia directamente.</summary>
    internal static IReadOnlyList<string> RivoReferences(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(IsRivo)
            .OrderBy(name => name, StringComparer.Ordinal)];

    /// <summary>Assemblies de uma camada concreta.</summary>
    internal static IReadOnlyList<Assembly> InLayer(string layer) =>
        [.. Modules.Where(a => Layer(a) == layer)];

    private static IReadOnlyList<Assembly> Discover()
    {
        var directory = Path.GetDirectoryName(typeof(RivoAssemblies).Assembly.Location)!;

        var assemblies = Directory
            .EnumerateFiles(directory, "Rivo.*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null && !name.EndsWith(".Tests", StringComparison.Ordinal))
            .Select(name => Assembly.Load(name!))
            .OrderBy(RivoAssemblies.Name, StringComparer.Ordinal)
            .ToList();

        // Uma descoberta vazia faria todos os testes abaixo passar por vacuidade
        // — a pior falha possível num teste de arquitectura. Falha aqui, alto.
        if (assemblies.Count == 0)
        {
            throw new InvalidOperationException(
                $"Nenhum assembly Rivo.* encontrado em '{directory}'. " +
                "Os testes de arquitectura estariam a verificar o vazio.");
        }

        return assemblies;
    }
}
