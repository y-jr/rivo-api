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
        // `Hr` — 2026-09-03 (ADR-045): atribuir o vendedor responsável exige
        // validar o Colaborador pelo contrato de `hr`, nunca por leitura de
        // tabela (ADR-010).
        ["Commercial"] = ["Audit", "Hr"],
        // `finance`/AR é o encontro dos três: `commercial` dá o cliente,
        // `fiscal` dá a taxa à data do facto gerador, e `finance` possui o
        // documento (ADR-036). Nenhum lê as tabelas do outro.
        //
        // `procurement` — 2026-08-28. `ISupplierDirectory` é por onde a
        // factura de compra liga ao Fornecedor, em vez de o guardar só como
        // retrato em texto. Mesma forma de `ICustomerDirectory` em cima.
        //
        // As direcções que `modules/finance.md` lista e que ainda faltam —
        // `hr`, `approval` — não precisam de referência directa: quem executa
        // um pagamento e quem o aprova chegam por identificador simples e por
        // `IPaymentApproval` invertido, sem resolver atributos do outro lado.
        ["Finance"] = ["Audit", "Fiscal", "Commercial", "Procurement", "Documents"],

        // `procurement` é dono do Fornecedor e da Requisição Interna. Duas
        // direcções, e nenhuma delas é `approval`:
        //
        //   - `audit`, porque qualificar um fornecedor e submeter uma
        //     requisição são actos auditados — e o IBAN em particular decide
        //     para onde o dinheiro sai;
        //   - `hr`, porque o requisitante é um Colaborador, lido pelo contrato
        //     e nunca por leitura de tabela (ADR-010).
        //
        // **`approval` não aparece, e é deliberado.** `procurement` submete a
        // decisão por `IProcurementApprovalSubmission`, declarado nas suas
        // próprias palavras e ligado ao motor no composition root. Aqui não
        // havia ciclo a quebrar — `approval` não lê `procurement` —, mas a
        // inversão mantém a propriedade do ADR-034: o módulo de negócio não
        // sabe qual é o motor de governança.
        //
        // As direcções que `modules/procurement.md` lista e que faltam —
        // `documents` (cotações), `inventory` (recepção de bens) e
        // `notifications` — pertencem à Ordem de Compra e à Recepção, que não
        // estão feitas.
        ["Procurement"] = ["Audit", "Hr"],

        ["Documents"] = ["Audit"],
        ["Hr"] = ["Audit", "Documents"],

        // Esqueletos (ver `modules/payroll.md`, `inventory.md`) — 2026-08-29.
        // Só o catálogo de permissões publicado e `audit`, como qualquer
        // módulo novo. Sem regra de negócio ainda, e sem as dependências que
        // os `.md` listam a mais: essas chegam com as funcionalidades que as
        // justificam.
        ["Payroll"] = ["Audit", "Fiscal", "Documents"],
        ["Inventory"] = ["Audit"],

        // `projects` ganhou Marco e Tarefa — 2026-08-30, já não é esqueleto
        // puro. `hr` entra porque atribuir uma Tarefa referencia um
        // Colaborador, lido pelo contrato e nunca por leitura de tabela
        // (ADR-010) — mesma forma da referência ao requisitante em
        // `procurement`. As direcções que `modules/projects.md` lista e que
        // faltam — `finance`, `commercial`, `fleet`, `documents`, `approval`,
        // `notifications` — pertencem a Orçamento de Projecto e Alocação de
        // Recursos, que ainda não estão feitos.
        ["Projects"] = ["Audit", "Hr", "Fleet"],

        // `fleet` ganhou Manutenção e Atribuição — 2026-08-30, mesma razão de
        // `projects`: atribuir uma viatura a um motorista referencia um
        // Colaborador pelo contrato de `hr` (ADR-010), nunca por leitura de
        // tabela. A 2026-08-31, Seguros e documentação legal ligaram-se a
        // `documents` (ADR-009, mesmo desenho de `hr`). As direcções que
        // `modules/fleet.md` lista e que faltam — `finance`, `inventory`,
        // `notifications` — pertencem a partes ainda por implementar.
        ["Fleet"] = ["Audit", "Hr", "Documents"],

        // `identity` compõe o catálogo de permissões a partir do que cada
        // módulo declara — cada um diz que permissões existem, `identity`
        // decide que perfis as recebem (ADR-005). `Dashboard` entra pela
        // mesma razão a partir de 2026-08-31: uma camada de composição
        // também declara o seu catálogo de permissões, e `identity` é
        // consumidor dele da mesma forma que é de qualquer módulo.
        // `Analytics` — 2026-09-04 (ADR-047), mesma razão.
        ["Identity"] = ["Audit", "Hr", "Documents", "Notifications", "Approval", "Fiscal", "Commercial", "Finance", "Procurement", "Payroll", "Projects", "Inventory", "Fleet", "Dashboard", "Messaging", "Analytics"],

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
        // `approval` resolve aprovadores por Cargo (`hr`) e verifica o
        // disponível orçamental (`finance`) antes de deixar decidir — BR-8.
        //
        // **`finance` não aparece do outro lado, e é o que impede o ciclo.**
        // `finance` declara `IPaymentApproval` nas suas próprias palavras e o
        // composition root é que o liga ao motor de `approval`. A direcção
        // `approval → finance` é uma só, e `Modules_HaveNoDependencyCycles`
        // continua a valer — é ele que garante que assim fica.
        ["Approval"] = ["Hr", "Audit", "Finance"],

        // `Settings` não é módulo — é camada de composição (ADR-041), sem
        // Domain nem Infrastructure própria. A regra de dependência não muda
        // por isso: só pelos contratos de quem compõe. Primeira aplicação de
        // Configurações & Administração (domain-map.md §Read models):
        // perfis de acesso de `identity`, políticas de `approval`.
        // `Commercial`/`Hr`/`Procurement` — 2026-09-04 (ADR-047): a
        // importação em massa via CSV escreve através dos contratos já
        // publicados de Cliente, Colaborador e Fornecedor.
        ["Settings"] = ["Identity", "Approval", "Commercial", "Hr", "Procurement"],

        // Segunda camada de composição (ADR-042, Portal do Colaborador) —
        // resolve "o próprio" pelo contrato de `hr`. Não depende de
        // `Identity`: a conta autenticada chega já resolvida (o `sub` do
        // token), lida directamente do `HttpContext` na camada Api, não por
        // contrato.
        ["EmployeePortal"] = ["Hr"],

        // Terceira camada de composição — o Dashboard Executivo, primeiro
        // consumidor de `IReceivablesOverview`/`IPayablesOverview`.
        ["Dashboard"] = ["Finance"],

        // Quarta camada de composição (ADR-043, Portal do Cliente) — resolve
        // "o próprio" pelo contrato de `commercial`, depois lê `finance`
        // pelas variantes por cliente dos mesmos números do Dashboard.
        // Mesma razão de `EmployeePortal`: não depende de `Identity`, a
        // conta autenticada chega já resolvida do `HttpContext`.
        // `Messaging` — 2026-09-03 (ADR-045): terceiro contrato de escrita
        // que o Portal do Cliente consome, mesmo padrão de `ICustomerPayments`.
        ["CustomerPortal"] = ["Commercial", "Finance", "Messaging"],

        // Novo módulo (ADR-045). `Commercial` resolve o vendedor responsável
        // atribuído ao cliente; `Hr` resolve o `UserId` desse vendedor para o
        // notificar; `Notifications` enfileira o aviso. Nenhuma dependência a
        // `Approval` — sem SLA, sem alçada, é fila simples.
        ["Messaging"] = ["Audit", "Commercial", "Hr", "Notifications"],

        // Quinta camada de composição (ADR-047, Analytics & IA) — tendência
        // mensal de `finance` (variante de `Dashboard`), mais actividade de
        // `fleet` e valorização de `inventory`.
        ["Analytics"] = ["Finance", "Fleet", "Inventory"],
    };

    /// <summary>
    /// Módulos declarados acima que são camadas de composição (ADR-041): sem
    /// Domain, por desenho — não têm agregado, não têm base de dados própria.
    /// <see cref="ProjectDiscovery_FindsEveryModuleProject"/> verifica-os pela
    /// camada que realmente têm, `Api`, em vez de assumir `Domain` como todos
    /// os outros.
    /// </summary>
    private static readonly HashSet<string> CamadasDeComposicao =
        new(StringComparer.Ordinal) { "Settings", "EmployeePortal", "Dashboard", "CustomerPortal", "Analytics" };

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
        // Uma camada de composição (ADR-041) não tem Domain por desenho — só
        // se verifica que tem, pelo menos, Api.
        foreach (var module in DependenciasDeclaradas.Keys)
        {
            var camadaMinima = CamadasDeComposicao.Contains(module)
                ? RivoAssemblies.ApiLayer
                : RivoAssemblies.DomainLayer;

            Assert.Contains(Projects, p => p.Module == module && p.Layer == camadaMinima);
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
