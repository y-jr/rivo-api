using System.Reflection;

namespace Rivo.Architecture.Tests;

/// <summary>
/// Concorrência optimista (ADR-002, ADR-025).
///
/// <para>
/// O ADR-002 exige coluna `version` em todos os agregados, e durante muito
/// tempo **nenhum a tinha** — o desvio ficou registado como K14 e só foi
/// descoberto ao escrever o ADR-019. Este ficheiro existe para que não volte a
/// acontecer em silêncio.
/// </para>
///
/// <para>
/// O modo de falha que interessa não é o de hoje: é o agregado que alguém
/// acrescenta daqui a seis meses, sem se lembrar da regra, e que só dá sinal
/// quando duas escritas concorrentes se sobrepõem em produção.
/// </para>
/// </summary>
public class ConcurrencyTokenTests
{
    /// <summary>
    /// Agregados sem contador de concorrência, e a razão.
    ///
    /// <para>
    /// A lista é a inversão deliberada da regra: por omissão, <strong>todo</strong>
    /// o agregado precisa de contador. Isentar um é uma decisão que tem de
    /// aparecer aqui, com justificação — não a ausência silenciosa de uma
    /// propriedade.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> IsentosPorDesenho = new(StringComparer.Ordinal)
    {
        ["AuditEvent"] =
            "Append-only por BR-10. Nunca é alterado depois de escrito, logo não há escrita " +
            "concorrente que possa sobrepor-se. Um contador aqui seria peso morto.",

        ["Position"] =
            "Sem métodos que alterem estado: o catálogo de Cargos cria-se e não se edita. " +
            "Quando a marca de autoridade passar a ser alterável (BR-21), passa a precisar.",

        ["EmployeeDocument"] =
            "Linha de ligação: cria-se e elimina-se, nunca se altera.",

        ["Decision"] =
            "Imutável por BR-17 e pelo ADR-034: uma decisão de aprovação é facto histórico, " +
            "e corrigi-la é decidir outra vez, não reescrever. Mesma razão de AuditEvent.",

        ["PolicyStep"] =
            "Criado com a política e nunca editado — alterar uma alçada é criar outra política, " +
            "porque os pedidos em curso guardam a que lhes foi aplicada (BR-6).",

        ["GoodsReceiptLine"] =
            "Contagem de uma linha, dentro do agregado GoodsReceipt. Nasce com a recepcao e nunca " +
            "se altera — anular a recepcao inteira e o que existe, e o contador dela cobre isso.",

        ["PurchaseOrderLine"] =
            "Parte do agregado PurchaseOrder, que nasce completo e não se altera depois de sair " +
            "para o fornecedor — o que existe para o corrigir é cancelar e emitir outra. O contador " +
            "da ordem cobre o cancelamento, que é a única alteração que há.",

        ["RequisitionLine"] =
            "Parte do agregado PurchaseRequisition e só alterável enquanto ele for rascunho — " +
            "depois de submetido nada lhe toca, porque o valor que escolheu a faixa da alçada já " +
            "foi congelado do lado de `approval`. O contador da requisição cobre as duas coisas.",

        ["PayeeParty"] =
            "Retrato de a quem se paga, congelado. Objecto de valor sem identidade nem ciclo de " +
            "vida próprio — vive nas colunas de quem o guarda, e é essa entidade que tem o contador.",

        ["CreditNoteLine"] =
            "Imutável, como a linha de factura: criada com a nota e nunca alterada. " +
            "O cancelamento é na raiz, que tem o contador.",

        ["ReceiptLine"] =
            "Uma liquidação é facto: que quantia foi para que factura. Não se corrige — " +
            "estorna-se o recibo inteiro e regista-se outro.",

        ["SalesInvoiceLine"] =
            "Imutável: criada com a factura e nunca alterada. A factura emite-se inteira e a partir " +
            "daí só o cancelamento altera estado — e esse é na raiz, que tem o contador.",

        ["DocumentNumber"] =
            "Objecto de valor imutável. As colunas vivem na tabela da factura, que tem o contador. " +
            "A corrida que interessa é na atribuição, e essa colide em `DocumentSeries`.",

        ["InvoicedParty"] =
            "Retrato do cliente no momento da emissão, não entidade. Nunca muda depois de gravado — " +
            "é isso que faz a factura ser facto histórico e não uma vista sobre `commercial`.",

        ["TaxRateVersion"] =
            "Imutável depois de introduzida: é facto histórico, e alterá-la mudaria retroactivamente " +
            "o imposto de documentos já emitidos. Corrigir é fechar esta e introduzir outra (ADR-011). " +
            "A série que a contém tem o contador, e é lá que a sobreposição colide.",

        ["BillingAddress"] =
            "Objecto de valor, não agregado: substitui-se inteira e não tem identidade nem ciclo de " +
            "vida próprio. As colunas vivem na tabela de `Customer`, que tem o contador.",

        ["JournalEntryLine"] =
            "Imutável: criada com o lançamento e nunca alterada. Um lançamento lança-se inteiro " +
            "ou não equilibra, e corrigir faz-se com outro lançamento — de regularização ou de " +
            "ajustamento, que o SAF-T distingue precisamente para isso. A anulação é na raiz, " +
            "que tem o contador.",

        ["PostingRuleLine"] =
            "Criada com a regra e nunca alterada — rever uma tradução é definir outra regra, " +
            "porque as que já lançaram documentos não podem mudar de sentido a posteriori. " +
            "A raiz tem o contador, e é lá que a desactivação colide.",

        ["BudgetLine"] =
            "O tecto de um mês, alterável **só de dentro de Budget** e só enquanto ele for " +
            "rascunho. Duas revisões simultâneas colidem na raiz antes de chegarem aqui — mesma " +
            "razão de Assignment.",

        ["BankMovement"] =
            "Linha de extracto, append-only e imposta como tal pela base de dados: nunca é " +
            "alterada depois de escrita, e corrigir faz-se com outro movimento em sentido " +
            "contrário. Quem colide é o saldo da conta, e é `BankAccount` que tem o contador — " +
            "mesma razão de AuditEvent.",

        ["Assignment"] =
            "Mutável (MarkDecided), mas só de dentro de ApprovalRequest, que tem o contador. " +
            "Duas decisões simultâneas colidem na raiz do agregado antes de chegarem aqui — " +
            "um contador próprio protegeria contra uma corrida que não existe.",
    };

    /// <summary>
    /// Todo o agregado mutável tem `Version`, ou está isento com razão escrita.
    /// </summary>
    [Fact]
    public void EveryAggregate_HasAConcurrencyCounterOrADocumentedExemption()
    {
        var violations = new List<string>();

        foreach (var aggregate in Aggregates())
        {
            var name = aggregate.Name;
            var temVersion = aggregate.GetProperty("Version", BindingFlags.Public | BindingFlags.Instance) is not null;
            var isento = IsentosPorDesenho.ContainsKey(name);

            if (!temVersion && !isento)
            {
                violations.Add(
                    $"{aggregate.FullName} não tem `Version` nem está isento. " +
                    "Acrescenta o contador, ou a isenção com razão em IsentosPorDesenho.");
            }

            if (temVersion && isento)
            {
                violations.Add(
                    $"{aggregate.FullName} tem `Version` mas está listado como isento — " +
                    "a isenção deixou de fazer sentido e deve sair da lista.");
            }
        }

        Assert.Empty(violations);
    }

    /// <summary>
    /// O contador é `int` e não tem `set` público.
    ///
    /// <para>
    /// É a infraestrutura que o incrementa, ao gravar. Um `set` público
    /// convidaria o domínio a mexer-lhe — e uma regra que obriga cada método de
    /// negócio a lembrar-se de incrementar um contador é uma regra que se
    /// esquece uma vez e falha em silêncio para sempre.
    /// </para>
    /// </summary>
    [Fact]
    public void ConcurrencyCounter_IsNotWritableFromOutside()
    {
        var violations = new List<string>();

        foreach (var aggregate in Aggregates())
        {
            var version = aggregate.GetProperty("Version", BindingFlags.Public | BindingFlags.Instance);

            if (version is null)
            {
                continue;
            }

            if (version.PropertyType != typeof(int))
            {
                violations.Add($"{aggregate.FullName}.Version é {version.PropertyType.Name}, devia ser int");
            }

            if (version.SetMethod is { IsPublic: true })
            {
                violations.Add($"{aggregate.FullName}.Version tem setter público");
            }
        }

        Assert.Empty(violations);
    }

    /// <summary>
    /// A lista de isenções não cria entradas mortas — um agregado que
    /// desapareceu ou mudou de nome deixaria lá uma justificação que já não
    /// corresponde a nada, e esconderia que a lista deixou de ser revista.
    /// </summary>
    [Fact]
    public void EveryExemption_StillMatchesAnAggregate()
    {
        var existentes = Aggregates().Select(a => a.Name).ToHashSet(StringComparer.Ordinal);

        var mortas = IsentosPorDesenho.Keys
            .Where(name => !existentes.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(mortas);
    }

    /// <summary>
    /// A descoberta encontra agregados. Sem isto, tudo acima passaria por
    /// vacuidade.
    /// </summary>
    [Fact]
    public void AggregateDiscovery_FindsTheDomainEntities()
    {
        var nomes = Aggregates().Select(a => a.Name).ToList();

        Assert.NotEmpty(nomes);
        Assert.Contains("Notification", nomes);
        Assert.Contains("Employee", nomes);
        Assert.Contains("AuditEvent", nomes);
    }

    /// <summary>
    /// Agregados: tipos de domínio com construtor privado sem parâmetros — a
    /// marca de materialização do EF Core, e portanto de "isto é uma linha
    /// numa tabela", que é o que a concorrência optimista protege.
    ///
    /// <para>
    /// Distingue-os de DTOs, enumerações e tipos de valor, que não são
    /// persistidos por si.
    /// </para>
    /// </summary>
    private static IEnumerable<Type> Aggregates() =>
        RivoAssemblies
            .InLayer(RivoAssemblies.DomainLayer)
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null) is not null)
            .OrderBy(t => t.Name, StringComparer.Ordinal);
}
