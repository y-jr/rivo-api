namespace Rivo.Procurement.Domain;

/// <summary>
/// Requisição Interna — o pedido de compra que nasce dentro da empresa e o
/// primeiro elo da cadeia requisição → ordem de compra → recepção → factura.
///
/// <para>
/// <strong>Não tem workflow de aprovação próprio</strong>, e é regra explícita
/// de `modules/procurement.md`: submete a `approval` e espera. O que vive aqui
/// é o estado do <em>pedido</em>, não o do processo de decisão — quem decidiu,
/// em que passo, e por que ordem é de `approval`, e perguntar-lhe é a única
/// forma de saber.
/// </para>
///
/// <para>
/// <strong>As linhas são inferência, não estão em `docs`.</strong> O esquema em
/// `docs/rivo-dados-integracoes-seguranca-v1.md` lista para a Requisição apenas
/// id, requisitante, departamento, justificação e estado. Mas sem saber o que
/// se pede e por quanto, duas coisas ficam impossíveis: gerar a Ordem de Compra
/// a partir da requisição aprovada, e dar a `approval` o valor que selecciona a
/// faixa da política. São "atributos principais", não uma lista fechada — e
/// esta é a leitura mínima que faz a cadeia funcionar.
/// </para>
/// </summary>
public sealed class PurchaseRequisition
{
    private readonly List<RequisitionLine> _lines = [];

    private PurchaseRequisition(
        Guid id,
        Guid requestedByEmployeeId,
        Guid? departmentId,
        string justification,
        string currency,
        DateOnly requestedOn)
    {
        Id = id;
        RequestedByEmployeeId = requestedByEmployeeId;
        DepartmentId = departmentId;
        Justification = justification;
        Currency = currency;
        RequestedOn = requestedOn;
        Status = RequisitionStatus.Draft;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private PurchaseRequisition()
    {
        Justification = string.Empty;
        Currency = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// O requisitante, como Colaborador de `hr` (ADR-010).
    ///
    /// <para>
    /// É contra ele que BR-2 é verificada em `approval` — quem pede não decide
    /// sobre o próprio pedido. `procurement` guarda o identificador e mais
    /// nada; o nome lê-se pelo contrato, nunca se copia (BR-18).
    /// </para>
    /// </summary>
    public Guid RequestedByEmployeeId { get; private set; }

    /// <summary>
    /// Departamento, para `approval` escolher a política.
    ///
    /// <para>
    /// Opcional no agregado porque nem todo o colaborador tem departamento
    /// atribuído — <c>EmployeeReference.DepartmentId</c> é ele próprio
    /// anulável. Sem departamento, a política aplicável é a genérica.
    /// </para>
    /// </summary>
    public Guid? DepartmentId { get; private set; }

    /// <summary>
    /// Justificação, obrigatória.
    ///
    /// <para>
    /// É o que dá a quem decide alguma coisa sobre que decidir. Uma requisição
    /// sem justificação obriga o aprovador a aprovar às cegas ou a ir perguntar
    /// — e o segundo caso não deixa rasto nenhum no sistema.
    /// </para>
    /// </summary>
    public string Justification { get; private set; }

    /// <summary>ISO 4217. `AOA` por omissão de quem chama, não do agregado.</summary>
    public string Currency { get; private set; }

    public DateOnly RequestedOn { get; private set; }

    public RequisitionStatus Status { get; private set; }

    /// <summary>
    /// O processo em `approval`, quando já foi submetido.
    ///
    /// <para>
    /// É a única ligação entre os dois módulos, e é por identificador: nenhum
    /// dos lados lê as tabelas do outro.
    /// </para>
    /// </summary>
    public Guid? ApprovalRequestId { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Porquê recusada ou cancelada. Nulo enquanto estiver viva.</summary>
    public string? ClosingReason { get; private set; }

    public IReadOnlyCollection<RequisitionLine> Lines => _lines.AsReadOnly();

    /// <summary>
    /// Valor estimado total.
    ///
    /// <para>
    /// <strong>Estimado, e a palavra importa.</strong> É o que o requisitante
    /// acha que custa, antes de haver cotação e antes de haver factura. Serve
    /// para escolher a faixa da política de aprovação; não é compromisso
    /// financeiro nem entra na contabilidade.
    /// </para>
    /// </summary>
    public decimal EstimatedTotal => _lines.Sum(line => line.EstimatedTotal);

    /// <summary>
    /// Concorrência optimista (ADR-025). O domínio nunca lhe toca.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>Verdadeiro enquanto ainda se pode alterar o que se pede.</summary>
    public bool IsEditable => Status is RequisitionStatus.Draft;

    public static PurchaseRequisition Open(
        Guid requestedByEmployeeId,
        Guid? departmentId,
        string justification,
        string currency,
        DateOnly requestedOn)
    {
        if (requestedByEmployeeId == Guid.Empty)
        {
            throw new ArgumentException(
                "Uma requisição precisa de requisitante — é contra ele que a segregação de funções é verificada.",
                nameof(requestedByEmployeeId));
        }

        if (string.IsNullOrWhiteSpace(justification))
        {
            throw new ArgumentException(
                "Uma requisição precisa de justificação: é o que quem decide vai ler.",
                nameof(justification));
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new ArgumentException(
                "A moeda é o código ISO 4217, com três letras (`AOA` para o kwanza).",
                nameof(currency));
        }

        return new PurchaseRequisition(
            Guid.CreateVersion7(),
            requestedByEmployeeId,
            departmentId,
            justification.Trim(),
            currency.Trim().ToUpperInvariant(),
            requestedOn);
    }

    /// <summary>
    /// Acrescenta uma linha. Só enquanto for rascunho.
    ///
    /// <para>
    /// Depois de submetida, alterar o que se pede mudaria o objecto da decisão
    /// debaixo de quem a está a tomar — e o valor que seleccionou a faixa da
    /// política já foi congelado do outro lado.
    /// </para>
    /// </summary>
    public RequisitionLine AddLine(string description, decimal quantity, decimal estimatedUnitPrice)
    {
        EnsureEditable("acrescentar linhas");

        var linha = new RequisitionLine(
            Guid.CreateVersion7(), Id, description, quantity, estimatedUnitPrice);

        _lines.Add(linha);

        return linha;
    }

    public void RemoveLine(Guid lineId)
    {
        EnsureEditable("remover linhas");

        var linha = _lines.FirstOrDefault(l => l.Id == lineId)
            ?? throw new InvalidOperationException("Linha não encontrada nesta requisição.");

        _lines.Remove(linha);
    }

    public void ChangeJustification(string justification)
    {
        EnsureEditable("alterar a justificação");

        if (string.IsNullOrWhiteSpace(justification))
        {
            throw new ArgumentException("Uma requisição precisa de justificação.", nameof(justification));
        }

        Justification = justification.Trim();
    }

    /// <summary>
    /// Marca a requisição como submetida a decisão.
    ///
    /// <para>
    /// O agregado não fala com `approval` — recebe o identificador do processo
    /// já criado. Quem o cria é a camada Application, e é ela que decide o que
    /// fazer se a submissão falhar.
    /// </para>
    /// </summary>
    public void MarkSubmitted(Guid approvalRequestId, DateTimeOffset at)
    {
        if (Status is not RequisitionStatus.Draft)
        {
            throw new InvalidOperationException(
                $"Só um rascunho se submete. Esta requisição está em {Status}.");
        }

        if (_lines.Count == 0)
        {
            throw new InvalidOperationException(
                "Uma requisição sem linhas não diz o que se pretende comprar, e não há sobre o que decidir.");
        }

        if (approvalRequestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Sem processo de aprovação não há submissão.", nameof(approvalRequestId));
        }

        ApprovalRequestId = approvalRequestId;
        SubmittedAt = at;
        Status = RequisitionStatus.PendingApproval;
    }

    /// <summary>
    /// Regista a decisão favorável vinda de `approval`.
    ///
    /// <para>
    /// <strong>É `procurement` que aplica o efeito, nunca `approval` que o
    /// empurra</strong> — `modules/approval.md` proíbe expressamente que o
    /// motor de governança altere dados de negócio do módulo de origem.
    /// </para>
    /// </summary>
    public void MarkApproved(DateTimeOffset at)
    {
        EnsurePending("aprovar");

        ClosedAt = at;
        Status = RequisitionStatus.Approved;
    }

    public void MarkRefused(string reason, DateTimeOffset at)
    {
        EnsurePending("recusar");

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "Uma recusa precisa de razão: sem ela, o requisitante não sabe o que corrigir.",
                nameof(reason));
        }

        ClosingReason = reason.Trim();
        ClosedAt = at;
        Status = RequisitionStatus.Refused;
    }

    /// <summary>
    /// Cancela a requisição.
    ///
    /// <para>
    /// Vale em rascunho e em pendente — desistir de um pedido em curso é
    /// legítimo. <strong>Não vale depois de aprovada</strong>: aí já há decisão
    /// registada, e desfazê-la unilateralmente apagaria a decisão de outra
    /// pessoa. O que se cancela nesse ponto é a Ordem de Compra, não isto.
    /// </para>
    ///
    /// <para><strong>Nunca eliminar</strong> — BR-14.</para>
    /// </summary>
    public void Cancel(string reason, DateTimeOffset at)
    {
        if (Status is not (RequisitionStatus.Draft or RequisitionStatus.PendingApproval))
        {
            throw new InvalidOperationException(
                $"Uma requisição em {Status} já não se cancela.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Um cancelamento precisa de razão.", nameof(reason));
        }

        ClosingReason = reason.Trim();
        ClosedAt = at;
        Status = RequisitionStatus.Cancelled;
    }

    private void EnsureEditable(string acto)
    {
        if (!IsEditable)
        {
            throw new InvalidOperationException(
                $"Não é possível {acto}: a requisição está em {Status} e só um rascunho se altera.");
        }
    }

    private void EnsurePending(string acto)
    {
        if (Status is not RequisitionStatus.PendingApproval)
        {
            throw new InvalidOperationException(
                $"Não é possível {acto}: a requisição está em {Status}, e não à espera de decisão.");
        }
    }
}

public enum RequisitionStatus
{
    /// <summary>Ainda a ser escrita. É o único estado em que se altera.</summary>
    Draft,

    /// <summary>Submetida a `approval` e à espera de decisão.</summary>
    PendingApproval,

    /// <summary>Decidida favoravelmente. É daqui que a Ordem de Compra pode nascer.</summary>
    Approved,

    Refused,

    Cancelled,
}

/// <summary>
/// Linha de requisição: o que se pede, quanto, e por quanto se estima.
/// </summary>
public sealed class RequisitionLine
{
    internal RequisitionLine(
        Guid id,
        Guid requisitionId,
        string description,
        decimal quantity,
        decimal estimatedUnitPrice)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException(
                "Uma linha precisa de descrição do que se pretende comprar.", nameof(description));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity), quantity, "A quantidade tem de ser positiva.");
        }

        if (estimatedUnitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(estimatedUnitPrice), estimatedUnitPrice,
                "O preço estimado não pode ser negativo. Zero é aceite — há pedidos por cotar.");
        }

        Id = id;
        RequisitionId = requisitionId;
        Description = description.Trim();
        Quantity = quantity;
        EstimatedUnitPrice = estimatedUnitPrice;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private RequisitionLine()
    {
        Description = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid RequisitionId { get; private set; }

    public string Description { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal EstimatedUnitPrice { get; private set; }

    public decimal EstimatedTotal => Quantity * EstimatedUnitPrice;
}
