using System.Text.RegularExpressions;

namespace Rivo.Finance.Domain;

/// <summary>
/// Uma conta do plano de contas.
///
/// <para>
/// <strong>O Rivo fixa a estrutura, não o conteúdo.</strong> O XSD do SAF-T AO
/// — que é fonte de verdade — fixa o formato do código, as seis categorias e a
/// regra da conta agregadora. **Não fixa o plano de contas angolano**, e esse
/// não está em fonte primária neste projecto: o levantamento fiscal é
/// provisório e o `CLAUDE.md` proíbe implementar a partir dele.
/// </para>
///
/// <para>
/// Por isso o plano <strong>carrega-se, não vem semeado</strong>. É a mesma
/// posição que `fiscal` tomou para as taxas (ADR-011): o sistema sabe a forma
/// de uma taxa e recusa-se a inventar a percentagem. Um plano de contas
/// inventado seria pior do que nenhum — pareceria certo, e a divergência só
/// apareceria no primeiro ficheiro entregue à AGT.
/// </para>
/// </summary>
public sealed class LedgerAccount
{
    /// <summary>
    /// Formato do código, do XSD (<c>SAFAOGLAccountID</c>): letras, dígitos e
    /// <c>- / . _ + *</c>, até 30 caracteres. Nada de espaços.
    /// </summary>
    private static readonly Regex CodeFormat =
        new(@"^[0-9a-zA-Z\-/._+*]{1,30}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private LedgerAccount(
        string code,
        string name,
        AccountCategory category,
        Guid? parentId,
        string? parentCode)
    {
        Id = Guid.CreateVersion7();
        Code = code;
        Name = name;
        Category = category;
        ParentId = parentId;
        ParentCode = parentCode;
        IsActive = true;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private LedgerAccount()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary><c>AccountID</c> no SAF-T. Único no plano.</summary>
    public string Code { get; private set; }

    /// <summary><c>AccountDescription</c> no SAF-T.</summary>
    public string Name { get; private set; }

    public AccountCategory Category { get; private set; }

    public Guid? ParentId { get; private set; }

    /// <summary>
    /// <c>GroupingCode</c> no SAF-T — o **código** da conta-pai, não o
    /// identificador.
    ///
    /// <para>
    /// Guardado além do <see cref="ParentId"/> porque é o código que o ficheiro
    /// exporta, e ler a hierarquia toda para o descobrir a cada linha exportada
    /// seria caro sem ganho nenhum. Move-se com o pai: o código de uma conta
    /// não se altera depois de haver movimentos.
    /// </para>
    /// </summary>
    public string? ParentCode { get; private set; }

    /// <summary>
    /// Desactivada deixa de aceitar lançamentos novos. **Não se elimina**
    /// (BR-14): as linhas lançadas continuam a apontar-lhe, e um balancete
    /// histórico tem de continuar legível.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>Concorrência optimista (ADR-025, BR-17).</summary>
    public int Version { get; private set; }

    /// <summary>
    /// Verdadeiro nas contas que <strong>recebem lançamentos</strong>.
    ///
    /// <para>
    /// É a distinção que dá sentido às seis categorias do SAF-T: uma conta
    /// agregadora existe para somar as filhas, e lançar directamente nela faria
    /// o total deixar de ser a soma — o erro clássico que um plano de contas
    /// hierárquico existe para impedir.
    /// </para>
    /// </summary>
    public bool AcceptsPostings =>
        Category is AccountCategory.GM or AccountCategory.AM;

    /// <summary>Conta de 1.º grau: no topo, e por isso sem agregadora.</summary>
    public bool IsFirstDegree =>
        Category is AccountCategory.GR or AccountCategory.AR;

    /// <summary>
    /// Contabilidade analítica (<c>A*</c>) por oposição a geral (<c>G*</c>).
    /// São duas árvores, e não se cruzam.
    /// </summary>
    public bool IsAnalytic =>
        Category is AccountCategory.AR or AccountCategory.AA or AccountCategory.AM;

    /// <param name="parent">
    /// A conta agregadora. Obrigatória excepto no 1.º grau — é o que o XSD diz
    /// por "deve ser indicada a conta agregadora respectiva, do grau
    /// imediatamente superior".
    /// </param>
    public static LedgerAccount Open(
        string code,
        string name,
        AccountCategory category,
        LedgerAccount? parent)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Uma conta precisa de código.", nameof(code));
        }

        var normalizado = code.Trim().ToUpperInvariant();

        if (!CodeFormat.IsMatch(normalizado))
        {
            throw new ArgumentException(
                $"O código '{code}' não serve para o SAF-T: são letras, dígitos e " +
                "'- / . _ + *', até 30 caracteres, sem espaços.",
                nameof(code));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Uma conta precisa de descrição.", nameof(name));
        }

        var conta = new LedgerAccount(normalizado, name.Trim(), category, null, null);

        if (conta.IsFirstDegree)
        {
            if (parent is not null)
            {
                throw new ArgumentException(
                    $"'{normalizado}' é conta de 1.º grau e não tem agregadora acima.",
                    nameof(parent));
            }

            return conta;
        }

        if (parent is null)
        {
            throw new ArgumentException(
                $"'{normalizado}' não é de 1.º grau, logo tem de indicar a conta agregadora.",
                nameof(parent));
        }

        // Uma conta de movimento é folha por definição — "conta de movimento",
        // não "agregadora ou integradora". Pendurar-lhe filhas faria o saldo
        // dela deixar de ser o que lá foi lançado.
        if (parent.AcceptsPostings)
        {
            throw new ArgumentException(
                $"'{parent.Code}' é conta de movimento e não agrega outras.",
                nameof(parent));
        }

        // Geral e analítica são duas árvores. Cruzá-las faria o total da
        // contabilidade geral incluir contas analíticas, que são outra vista
        // sobre os mesmos factos — contava-se tudo duas vezes.
        if (parent.IsAnalytic != conta.IsAnalytic)
        {
            throw new ArgumentException(
                $"'{normalizado}' e '{parent.Code}' são de contabilidades diferentes " +
                "— uma é geral e a outra analítica.",
                nameof(parent));
        }

        conta.ParentId = parent.Id;
        conta.ParentCode = parent.Code;

        return conta;
    }

    /// <summary>
    /// Corrige a descrição. <strong>O código não se altera</strong>: é a
    /// referência das linhas já lançadas e do que já foi exportado.
    /// </summary>
    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Uma conta precisa de descrição.", nameof(name));
        }

        Name = name.Trim();
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}

/// <summary>
/// <c>GroupingCategory</c> do SAF-T AO. As seis, tal como o XSD as enumera —
/// não é uma classificação inventada aqui.
/// </summary>
public enum AccountCategory
{
    /// <summary>Conta de 1.º grau da contabilidade geral.</summary>
    GR,

    /// <summary>Conta agregadora ou integradora da contabilidade geral.</summary>
    GA,

    /// <summary>Conta de movimento da contabilidade geral.</summary>
    GM,

    /// <summary>Conta de 1.º grau da contabilidade analítica.</summary>
    AR,

    /// <summary>Conta agregadora ou integradora da contabilidade analítica.</summary>
    AA,

    /// <summary>Conta de movimento da contabilidade analítica.</summary>
    AM,
}
