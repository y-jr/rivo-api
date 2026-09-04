namespace Rivo.Messaging.Domain.Tests;

/// <summary>
/// Uma conversa entre um cliente e a equipa comercial (ADR-045) — assíncrona,
/// uma por cliente, não uma por assunto.
/// </summary>
public class ConversationTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 3, 10, 0, 0, TimeSpan.Zero);

    private static Conversation Aberta() => Conversation.OpenMessage(Guid.CreateVersion7(), Agora);

    [Fact]
    public void OpenMessage_NasceComoOpenSemMensagensSemAssunto()
    {
        var conversa = Aberta();

        Assert.Equal(ConversationStatus.Open, conversa.Status);
        Assert.Equal(ConversationKind.Message, conversa.Kind);
        Assert.Null(conversa.Subject);
        Assert.Empty(conversa.Messages);
    }

    [Fact]
    public void OpenTicket_NasceComoOpenComAssunto()
    {
        var conversa = Conversation.OpenTicket(Guid.CreateVersion7(), "Problema com login", Agora);

        Assert.Equal(ConversationStatus.Open, conversa.Status);
        Assert.Equal(ConversationKind.Ticket, conversa.Kind);
        Assert.Equal("Problema com login", conversa.Subject);
    }

    [Fact]
    public void OpenTicket_SemAssunto_ERecusado()
    {
        Assert.Throws<ArgumentException>(() => Conversation.OpenTicket(Guid.CreateVersion7(), "  ", Agora));
    }

    [Fact]
    public void OpenTicket_AssuntoAcimaDoLimite_ERecusado()
    {
        var demasiado = new string('a', 201);

        Assert.Throws<ArgumentException>(() => Conversation.OpenTicket(Guid.CreateVersion7(), demasiado, Agora));
    }

    [Fact]
    public void OpenTicket_AssuntoENormalizado()
    {
        var conversa = Conversation.OpenTicket(Guid.CreateVersion7(), "  Problema com login  ", Agora);

        Assert.Equal("Problema com login", conversa.Subject);
    }

    [Fact]
    public void AddMessage_DosDoisLados_FicamNaOrdem()
    {
        var conversa = Aberta();
        var clienteId = Guid.CreateVersion7();
        var vendedorId = Guid.CreateVersion7();

        conversa.AddMessage(MessageSender.Customer, clienteId, "Onde está a minha factura?", Agora);
        conversa.AddMessage(MessageSender.Employee, vendedorId, "Já vou verificar.", Agora.AddMinutes(5));

        Assert.Equal(2, conversa.Messages.Count);
        Assert.Equal(MessageSender.Customer, conversa.Messages[0].Sender);
        Assert.Equal(MessageSender.Employee, conversa.Messages[1].Sender);
    }

    [Fact]
    public void AddMessage_CorpoVazio_ERecusado()
    {
        var conversa = Aberta();

        Assert.Throws<ArgumentException>(
            () => conversa.AddMessage(MessageSender.Customer, Guid.CreateVersion7(), "   ", Agora));
    }

    [Fact]
    public void AddMessage_AcimaDoLimite_ERecusado()
    {
        var conversa = Aberta();
        var demasiado = new string('a', 4001);

        Assert.Throws<ArgumentException>(
            () => conversa.AddMessage(MessageSender.Customer, Guid.CreateVersion7(), demasiado, Agora));
    }

    [Fact]
    public void AddMessage_ConversaFechada_ERecusada()
    {
        var conversa = Aberta();
        conversa.Close(Guid.CreateVersion7(), Agora);

        Assert.Throws<InvalidOperationException>(
            () => conversa.AddMessage(MessageSender.Customer, Guid.CreateVersion7(), "Ainda aqui?", Agora));
    }

    [Fact]
    public void Close_FicaClosedComQuemFechouEQuando()
    {
        var conversa = Aberta();
        var vendedorId = Guid.CreateVersion7();

        conversa.Close(vendedorId, Agora);

        Assert.Equal(ConversationStatus.Closed, conversa.Status);
        Assert.Equal(vendedorId, conversa.ClosedByUserId);
        Assert.Equal(Agora, conversa.ClosedAt);
    }

    [Fact]
    public void Close_JaFechada_ERecusado()
    {
        var conversa = Aberta();
        conversa.Close(Guid.CreateVersion7(), Agora);

        Assert.Throws<InvalidOperationException>(() => conversa.Close(Guid.CreateVersion7(), Agora));
    }
}
