using System.Xml.Linq;

namespace Rivo.Architecture.Tests;

/// <summary>
/// O grafo de referências <strong>declarado</strong>, lido dos `.csproj`.
///
/// <para>
/// Complementa <see cref="LayerDependencyTests"/> e <see cref="ModuleBoundaryTests"/>,
/// que inspeccionam assemblies compilados. A distinção não é académica:
/// <c>Assembly.GetReferencedAssemblies()</c> só reporta assemblies
/// <em>efectivamente usados</em> — o compilador poda referências de projecto
/// que nenhum tipo utiliza.
/// </para>
///
/// <para>
/// <strong>Descoberto por mutação em 2026-08-16:</strong> acrescentar uma
/// referência de `Rivo.Hr.Domain` para `Rivo.Audit.Application` não fazia
/// falhar nenhum teste, porque nenhum tipo a usava ainda. A referência ficava
/// lá, à espera de alguém a usar. Este ficheiro fecha essa janela: o ADR-017
/// é uma regra sobre <em>referências de projecto</em>, e é assim que se
/// verifica.
/// </para>
/// </summary>
public class ProjectReferenceTests
{
    /// <summary>
    /// Tabela de `architecture/dependency-rules.md` §"Dependências permitidas
    /// entre módulos", limitada aos módulos implementados.
    ///
    /// Acrescentar aqui é aceitar a dependência de forma explícita — que é o
    /// ponto: uma direcção nova é decisão arquitectural e tem de aparecer como
    /// alteração deliberada.
    /// </summary>
    private static readonly Dictionary<string, string[]> DependenciasDeclaradas = new(StringComparer.Ordinal)
    {
        ["Audit"] = [],
        ["Notifications"] = [],

        // Só `audit`: introduzir uma versão de taxa é operação de dados
        // auditada (ADR-011 §5).
        //
        // De resto `fiscal` não depende de módulo nenhum, e é isso que o torna
        // a raiz da ordem de execução do ADR-036 — a direcção que existe é a
        // inversa: `commercial` e `finance` perguntam-lhe o imposto.
        //
        // Virá a **ler** desses módulos para relato e exportação SAF-T
        // (`modules/fiscal.md`, "duas direcções, duas capacidades"), mas isso
        // está adiado pelo ADR-036 e a linha muda quando lá se chegar.
        ["Fiscal"] = ["Audit"],

        // `commercial` está reduzido ao Cliente pelo ADR-036, e o Cliente só
        // depende de `audit` — registar um cliente altera a base de um
        // documento fiscal. As dependências que `modules/commercial.md` lista
        // — `hr`, `fiscal`, `approval`, `documents`, `finance` — pertencem ao
        // funil comercial e à facturação, que não estão feitos.
        ["Commercial"] = ["Audit"],
        // `finance`/AR é o encontro dos três: `commercial` dá o cliente,
        // `fiscal` dá a taxa à data do facto gerador, e `finance` possui o
        // documento (ADR-036). Nenhum lê as tabelas do outro.
        //
        // As direcções que `modules/finance.md` lista e que faltam —
        // `procurement`, `hr`, `approval` — pertencem a Contas a Pagar,
        // Tesouraria e à execução de pagamento, que não estão feitas.
        ["Finance"] = ["Audit", "Fiscal", "Commercial"],

        ["Documents"] = ["Audit"],
        ["Hr"] = ["Audit", "Documents"],
        // `identity` compõe o catálogo de permissões a partir do que cada
        // módulo declara — cada um diz que permissões existem, `identity`
        // decide que perfis as recebem (ADR-005).
        ["Identity"] = ["Audit", "Hr", "Documents", "Notifications", "Approval", "Fiscal", "Commercial", "Finance"],

        // `approval` resolve aprovadores por Cargo, que é de `hr` (ADR-034).
        //
        // Isto forma o ciclo `hr ↔ approval` que o ADR-015 §R1 previu, e a
        // resolução é a que ele fixou: cada lado referencia o assembly de
        // *contratos* do outro, e os contratos não dependem de nada. O teste
        // `Modules_HaveNoDependencyCycles` continua a valer, e é ele que
        // garante que a resolução se mantém.
        //
        // O domínio de `approval` ainda não referencia `hr`: a resolução de
        // aprovadores é feita na camada Application, que não existe ainda.
        ["Approval"] = ["Hr", "Audit"],
    };

    [Fact]
    public void Module_ReferencesAnotherModuleOnlyThroughItsContracts()
    {
        var violations = new List<string>();

        foreach (var project in Projects.Where(p => p.Name != RivoAssemblies.Host))
        {
            foreach (var reference in project.References)
            {
                var target = RivoAssemblies.Module(reference);

                if (target == project.Module)
                {
                    continue;
                }

                if (RivoAssemblies.Layer(reference) != RivoAssemblies.ContractsLayer)
                {
                    violations.Add($"{project.Name} -> {reference} (devia ser Rivo.{target}.Contracts)");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void Contracts_ReferenceNothing()
    {
        var violations = Projects
            .Where(p => p.Layer == RivoAssemblies.ContractsLayer && p.References.Count > 0)
            .Select(p => $"{p.Name} -> {string.Join(", ", p.References)}")
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Domain_ReferencesNothing()
    {
        var violations = Projects
            .Where(p => p.Layer == RivoAssemblies.DomainLayer && p.References.Count > 0)
            .Select(p => $"{p.Name} -> {string.Join(", ", p.References)}")
            .ToList();

        Assert.Empty(violations);
    }

    /// <summary>
    /// O domínio também não traz pacotes: nem ORM, nem framework web.
    ///
    /// <para>
    /// A verificação sobre assemblies compilados não apanha um pacote
    /// referenciado e ainda não usado — o mesmo ponto cego que motivou este
    /// ficheiro.
    /// </para>
    /// </summary>
    [Fact]
    public void Domain_ReferencesNoPackage()
    {
        var violations = Projects
            .Where(p => p.Layer == RivoAssemblies.DomainLayer)
            .Where(p => p.Packages.Count > 0 || p.FrameworkReferences.Count > 0)
            .Select(p => $"{p.Name} -> {string.Join(", ", p.Packages.Concat(p.FrameworkReferences))}")
            .ToList();

        Assert.Empty(violations);
    }

    /// <summary>
    /// A camada API de um módulo nunca referencia `Infrastructure`. O host é a
    /// excepção declarada, por ser o composition root
    /// (dependency-rules.md §API).
    /// </summary>
    [Fact]
    public void ModuleApi_ReferencesNoInfrastructure()
    {
        var violations = Projects
            .Where(p => p.Name != RivoAssemblies.Host && p.Layer == RivoAssemblies.ApiLayer)
            .SelectMany(p => p.References
                .Where(r => RivoAssemblies.Layer(r) == RivoAssemblies.InfrastructureLayer)
                .Select(r => $"{p.Name} -> {r}"))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Application_ReferencesNoInfrastructure()
    {
        var violations = Projects
            .Where(p => p.Layer == RivoAssemblies.ApplicationLayer)
            .SelectMany(p => p.References
                .Where(r => RivoAssemblies.Layer(r) == RivoAssemblies.InfrastructureLayer)
                .Select(r => $"{p.Name} -> {r}"))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Modules_ReferenceOnlyDeclaredDirections()
    {
        var violations = new List<string>();

        foreach (var project in Projects.Where(p => p.Name != RivoAssemblies.Host))
        {
            if (!DependenciasDeclaradas.TryGetValue(project.Module, out var permitidas))
            {
                violations.Add($"módulo '{project.Module}' não está na tabela de dependências declaradas");
                continue;
            }

            violations.AddRange(project.References
                .Select(RivoAssemblies.Module)
                .Where(target => target != project.Module && !permitidas.Contains(target))
                .Distinct()
                .Select(target => $"{project.Module} -> {target} não está declarada ({project.Name})"));
        }

        Assert.Empty(violations);
    }

    /// <summary>
    /// A descoberta encontra os projectos. Sem isto, tudo acima passaria por
    /// vacuidade se o caminho da raiz mudasse.
    /// </summary>
    [Fact]
    public void ProjectDiscovery_FindsEveryModuleProject()
    {
        Assert.NotEmpty(Projects);
        Assert.Contains(Projects, p => p.Name == "Rivo.Hr.Domain");
        Assert.Contains(Projects, p => p.Name == RivoAssemblies.Host);

        // Cada módulo implementado tem, no mínimo, Domain, Application e Api.
        foreach (var module in DependenciasDeclaradas.Keys)
        {
            Assert.Contains(Projects, p => p.Module == module && p.Layer == RivoAssemblies.DomainLayer);
        }
    }

    // --- Leitura dos ficheiros de projecto --------------------------------

    private sealed record Project(
        string Name,
        string Module,
        string Layer,
        IReadOnlyList<string> References,
        IReadOnlyList<string> Packages,
        IReadOnlyList<string> FrameworkReferences);

    private static IReadOnlyList<Project> Projects { get; } = LoadProjects();

    private static IReadOnlyList<Project> LoadProjects()
    {
        var root = RepositoryRoot();

        var projects = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(Parse)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        if (projects.Count == 0)
        {
            throw new InvalidOperationException(
                $"Nenhum .csproj encontrado em '{Path.Combine(root, "src")}'.");
        }

        return projects;
    }

    private static Project Parse(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var document = XDocument.Load(path);

        IReadOnlyList<string> Values(string element, Func<string, string> project) =>
            [.. document.Descendants(element)
                .Select(e => e.Attribute("Include")?.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => project(v!))
                .Distinct()
                .OrderBy(v => v, StringComparer.Ordinal)];

        return new Project(
            Name: name,
            Module: RivoAssemblies.Module(name),
            Layer: RivoAssemblies.Layer(name),
            References: Values("ProjectReference", v => Path.GetFileNameWithoutExtension(v.Replace('\\', '/'))),
            Packages: Values("PackageReference", v => v),
            FrameworkReferences: Values("FrameworkReference", v => v));
    }

    /// <summary>
    /// Sobe a partir da saída do build até encontrar a solução. Evita fixar um
    /// número de `..` que se parte assim que a estrutura de pastas mude.
    /// </summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rivo.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Raiz do repositório não encontrada a partir de '{AppContext.BaseDirectory}'.");
    }
}
