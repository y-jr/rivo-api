namespace Rivo.Inventory.Application.Abstractions;

/// <summary>
/// Governança de decisões, <strong>nas palavras de `inventory`</strong>.
///
/// <para>
/// Mesma inversão que `hr`, `procurement` e `payroll` já fazem, e pela mesma
/// razão: `inventory` declara o que precisa, e quem o satisfaz — falando com
/// `approval` — é o composition root, o único sítio autorizado a conhecer
/// implementações de todos os módulos.
/// </para>
/// </summary>
public interface IInventoryApprovalSubmission
{
    /// <summary>
    /// Falso quando não há motor de governança ligado neste ambiente.
    ///
    /// <para>
    /// Sem ele, a contagem fecha e aplica os ajustes — ao contrário da folha
    /// salarial, que fica em rascunho. A diferença é deliberada e está no
    /// ADR-064: uma folha por aprovar não paga ninguém, e esperar não custa
    /// nada; uma contagem por fechar deixa o stock do sistema a divergir do
    /// stock real, que é a situação que a contagem existe para terminar.
    /// </para>
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Submete a divergência a decisão.
    ///
    /// <para>
    /// <strong>`inventory` não sabe qual é o limiar, e é assim de propósito.</strong>
    /// Submete sempre que há divergência e deixa `approval` responder: se
    /// nenhuma alçada configurada cobre este valor, a resposta é
    /// <see cref="InventoryApprovalOutcome.NoApplicablePolicy"/> e a contagem
    /// segue o seu caminho. Quem decide o que precisa de aprovação é quem
    /// configura as políticas — não o código deste módulo.
    /// </para>
    /// </summary>
    /// <param name="varianceValue">
    /// Soma de |variância| × custo médio de cada item divergente. É o número que
    /// escolhe a faixa da política — uma falta de mil unidades de um artigo
    /// barato não vale o mesmo que uma de dez de um artigo caro.
    /// </param>
    /// <param name="requestedByUserId">
    /// A <strong>conta</strong> que fechou a contagem, não o colaborador.
    /// Traduzir uma na outra é trabalho do composition root: `approval` precisa
    /// do colaborador (é contra ele que a segregação de funções é verificada),
    /// e `inventory` não conhece `hr` nem passa a conhecer por causa disto.
    /// </param>
    Task<InventoryApprovalSubmissionResult> SubmitAsync(
        Guid countId,
        Guid requestedByUserId,
        decimal varianceValue,
        string summary,
        CancellationToken cancellationToken);

    /// <summary>
    /// Estado corrente de um processo. `inventory` pergunta; `approval` nunca
    /// empurra — o efeito é aplicado deste lado.
    /// </summary>
    Task<InventoryApprovalState> GetStateAsync(Guid approvalRequestId, CancellationToken cancellationToken);
}

public enum InventoryApprovalOutcome
{
    Submitted,

    /// <summary>
    /// Não há alçada configurada que cubra este valor. <strong>Não é erro</strong>:
    /// é a resposta que diz «isto não precisa de aprovação».
    /// </summary>
    NoApplicablePolicy,

    /// <summary>
    /// Há política, mas a submissão não passou — duas políticas igualmente
    /// aplicáveis, nenhum aprovador resolvido, ou verificação orçamental
    /// indisponível. <strong>Aqui não se fecha</strong>: há governança
    /// configurada e ela não foi cumprida.
    /// </summary>
    Blocked,
}

public sealed record InventoryApprovalSubmissionResult(
    InventoryApprovalOutcome Outcome,
    Guid? RequestId,
    string? Reason)
{
    public static InventoryApprovalSubmissionResult Submitted(Guid requestId) =>
        new(InventoryApprovalOutcome.Submitted, requestId, null);

    public static InventoryApprovalSubmissionResult NoApplicablePolicy(string? reason = null) =>
        new(InventoryApprovalOutcome.NoApplicablePolicy, null, reason);

    public static InventoryApprovalSubmissionResult Blocked(string reason) =>
        new(InventoryApprovalOutcome.Blocked, null, reason);
}

public enum InventoryApprovalState
{
    Pending,
    Approved,
    Refused,

    /// <summary>O processo não existe em `approval` — não se adivinha o que decidir.</summary>
    Unknown,
}
