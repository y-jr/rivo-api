using System.Reflection;
using Rivo.Audit.Domain;

namespace Rivo.Audit.Domain.Tests;

/// <summary>
/// `audit` não tem regras de negócio — tem uma garantia técnica. A
/// imutabilidade do registo é a única invariante do módulo, e BR-10 exige
/// append-only.
/// </summary>
public class AuditEventTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 3, 1, 10, 30, 0, TimeSpan.Zero);

    private static AuditEvent Record(
        string action = "hr.employee.hired",
        string entityType = "hr.employee",
        string entityId = "d1f0c2a4") =>
        AuditEvent.Record(
            action, entityType, entityId,
            actorId: Guid.CreateVersion7(),
            ipAddress: "197.149.0.1",
            correlationId: "corr-1",
            previousValue: null,
            newValue: "{}",
            occurredAt: OccurredAt);

    // --- Imutabilidade ----------------------------------------------------

    /// <summary>
    /// Guarda estrutural de BR-10, verificada por reflexão.
    ///
    /// <para>
    /// Um teste que criasse um evento e verificasse valores passaria na mesma
    /// se alguém acrescentasse um setter público. Este falha — que é o ponto:
    /// impede que a imutabilidade se perca por conveniência num futuro
    /// <c>Corrigir()</c> ou <c>Anonimizar()</c>.
    /// </para>
    ///
    /// <para>
    /// A garantia equivalente ao nível da base de dados continua por
    /// implementar (K9). Esta cobre o caminho aplicacional, que hoje é o único
    /// caminho de escrita.
    /// </para>
    /// </summary>
    [Fact]
    public void AuditEvent_ExposesNoPublicSetter()
    {
        var writable = typeof(AuditEvent)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is { IsPublic: true })
            .Select(property => property.Name)
            .ToArray();

        Assert.Empty(writable);
    }

    [Fact]
    public void AuditEvent_ExposesNoInstanceMethodBeyondObject()
    {
        var mutators = typeof(AuditEvent)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName) // exclui os getters das propriedades
            .Select(method => method.Name)
            .ToArray();

        Assert.Empty(mutators);
    }

    // --- Validação --------------------------------------------------------

    /// <summary>
    /// Um registo sem acção ou sem alvo é ruído: dá a impressão de haver
    /// trilha onde não há, que é pior do que não ter registo nenhum.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Record_WithoutAction_Throws(string action)
    {
        Assert.Throws<ArgumentException>(() => Record(action: action));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Record_WithoutEntityType_Throws(string entityType)
    {
        Assert.Throws<ArgumentException>(() => Record(entityType: entityType));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Record_WithoutEntityId_Throws(string entityId)
    {
        Assert.Throws<ArgumentException>(() => Record(entityId: entityId));
    }

    // --- Actores não interactivos ----------------------------------------

    /// <summary>
    /// O modelo tem de suportar acções sem utilizador autenticado: jobs
    /// agendados, processos de sistema, integrações. Recusá-las obrigaria a
    /// inventar um utilizador falso, e a trilha passaria a mentir sobre quem
    /// agiu.
    /// </summary>
    [Fact]
    public void Record_WithoutActor_IsAllowed()
    {
        var entry = AuditEvent.Record(
            "notifications.delivery.abandoned", "notifications.notification", "a1b2",
            actorId: null, ipAddress: null, correlationId: null,
            previousValue: null, newValue: null, occurredAt: OccurredAt);

        Assert.Null(entry.ActorId);
        Assert.Null(entry.IpAddress);
    }

    [Fact]
    public void Record_KeepsWhatItWasGiven()
    {
        var entry = Record();

        Assert.Equal("hr.employee.hired", entry.Action);
        Assert.Equal("hr.employee", entry.EntityType);
        Assert.Equal("d1f0c2a4", entry.EntityId);
        Assert.Equal(OccurredAt, entry.OccurredAt);
        Assert.NotEqual(Guid.Empty, entry.Id);
    }
}
