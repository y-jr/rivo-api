using Rivo.Audit.Contracts;
using Rivo.Commercial.Contracts;
using Rivo.Hr.Contracts;
using Rivo.Messaging.Application.UseCases;
using Rivo.Messaging.Contracts;
using Rivo.Messaging.Domain;

namespace Rivo.Messaging.Application.Tests;

public class ManageConversationsTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 3, 10, 0, 0, TimeSpan.Zero);

    private static readonly AuditContext Contexto = new(Guid.CreateVersion7(), "10.0.0.1", null);

    private static CustomerReference Cliente(Guid customerId, Guid? vendedorId = null) =>
        new(customerId, "Kianda Lda", "5417000000", CustomerStatus.Active,
            new BillingAddress("Rua Rainha Ginga 12", "Luanda", "AO"), vendedorId);

    private static EmployeeReference Vendedor(Guid employeeId, Guid? userId) =>
        new(employeeId, "Vendedor Teste", EmployeeStatus.Active, null, null, userId);

    // ---- SendCustomerMessage ----

    [Fact]
    public async Task SendCustomerMessage_SemConversaAberta_AbreUmaNova()
    {
        var customerId = Guid.CreateVersion7();
        var store = new FakeConversationStore();
        var send = new SendCustomerMessage(
            store, new FakeCustomerDirectory(), new FakeEmployeeDirectory(), new FakeNotifier(),
            new FakeAuditTrail(), new RelogioFixo(Agora));

        var resultado = await send.ExecuteAsync(customerId, Guid.CreateVersion7(), "Olá, preciso de ajuda.", CancellationToken.None);

        Assert.Equal(SendMessageOutcome.Sent, resultado.Outcome);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task SendCustomerMessage_ComConversaAberta_EntraNaMesma()
    {
        var customerId = Guid.CreateVersion7();
        var existente = Conversation.Open(customerId, Agora.AddDays(-1));
        var store = new FakeConversationStore().With(existente);
        var send = new SendCustomerMessage(
            store, new FakeCustomerDirectory(), new FakeEmployeeDirectory(), new FakeNotifier(),
            new FakeAuditTrail(), new RelogioFixo(Agora));

        var resultado = await send.ExecuteAsync(customerId, Guid.CreateVersion7(), "Segunda mensagem.", CancellationToken.None);

        Assert.Equal(SendMessageOutcome.Sent, resultado.Outcome);
        Assert.Equal(existente.Id, resultado.ConversationId);
        Assert.Single(existente.Messages);
    }

    [Fact]
    public async Task SendCustomerMessage_CorpoVazio_ERecusado()
    {
        var store = new FakeConversationStore();
        var send = new SendCustomerMessage(
            store, new FakeCustomerDirectory(), new FakeEmployeeDirectory(), new FakeNotifier(),
            new FakeAuditTrail(), new RelogioFixo(Agora));

        var resultado = await send.ExecuteAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), "  ", CancellationToken.None);

        Assert.Equal(SendMessageOutcome.Rejected, resultado.Outcome);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task SendCustomerMessage_ComVendedorResponsavel_AvisaOVendedor()
    {
        var customerId = Guid.CreateVersion7();
        var vendedorId = Guid.CreateVersion7();
        var vendedorUserId = Guid.CreateVersion7();

        var store = new FakeConversationStore();
        var customers = new FakeCustomerDirectory().With(Cliente(customerId, vendedorId));
        var employees = new FakeEmployeeDirectory().With(Vendedor(vendedorId, vendedorUserId));
        var notifier = new FakeNotifier();

        var send = new SendCustomerMessage(
            store, customers, employees, notifier, new FakeAuditTrail(), new RelogioFixo(Agora));

        await send.ExecuteAsync(customerId, Guid.CreateVersion7(), "Preciso de ajuda.", CancellationToken.None);

        var aviso = Assert.Single(notifier.Queued);
        Assert.Equal(vendedorUserId, aviso.RecipientUserId);
    }

    [Fact]
    public async Task SendCustomerMessage_SemVendedorResponsavel_NaoAvisaNinguem()
    {
        var customerId = Guid.CreateVersion7();
        var store = new FakeConversationStore();
        var customers = new FakeCustomerDirectory().With(Cliente(customerId));
        var notifier = new FakeNotifier();

        var send = new SendCustomerMessage(
            store, customers, new FakeEmployeeDirectory(), notifier, new FakeAuditTrail(), new RelogioFixo(Agora));

        await send.ExecuteAsync(customerId, Guid.CreateVersion7(), "Preciso de ajuda.", CancellationToken.None);

        Assert.Empty(notifier.Queued);
    }

    // ---- SendEmployeeReply ----

    [Fact]
    public async Task SendEmployeeReply_ConversaAberta_EAceite()
    {
        var conversa = Conversation.Open(Guid.CreateVersion7(), Agora);
        var store = new FakeConversationStore().With(conversa);
        var reply = new SendEmployeeReply(store, new FakeAuditTrail(), new RelogioFixo(Agora));

        var resultado = await reply.ExecuteAsync(conversa.Id, Guid.CreateVersion7(), "Já vou verificar.", Contexto, CancellationToken.None);

        Assert.Equal(ReplyOutcome.Sent, resultado.Outcome);
        Assert.Single(conversa.Messages);
    }

    [Fact]
    public async Task SendEmployeeReply_ConversaFechada_E409()
    {
        var conversa = Conversation.Open(Guid.CreateVersion7(), Agora);
        conversa.Close(Guid.CreateVersion7(), Agora);
        var store = new FakeConversationStore().With(conversa);
        var reply = new SendEmployeeReply(store, new FakeAuditTrail(), new RelogioFixo(Agora));

        var resultado = await reply.ExecuteAsync(conversa.Id, Guid.CreateVersion7(), "Ainda aqui?", Contexto, CancellationToken.None);

        Assert.Equal(ReplyOutcome.Closed, resultado.Outcome);
    }

    [Fact]
    public async Task SendEmployeeReply_Inexistente_ENotFound()
    {
        var store = new FakeConversationStore();
        var reply = new SendEmployeeReply(store, new FakeAuditTrail(), new RelogioFixo(Agora));

        var resultado = await reply.ExecuteAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), "Olá", Contexto, CancellationToken.None);

        Assert.Equal(ReplyOutcome.NotFound, resultado.Outcome);
    }

    // ---- CloseConversation ----

    [Fact]
    public async Task CloseConversation_Aberta_FicaFechada()
    {
        var conversa = Conversation.Open(Guid.CreateVersion7(), Agora);
        var store = new FakeConversationStore().With(conversa);
        var close = new CloseConversation(store, new FakeAuditTrail(), new RelogioFixo(Agora));

        var resultado = await close.ExecuteAsync(conversa.Id, Guid.CreateVersion7(), Contexto, CancellationToken.None);

        Assert.Equal(CloseConversationOutcome.Closed, resultado);
        Assert.Equal(ConversationStatus.Closed, conversa.Status);
    }

    [Fact]
    public async Task CloseConversation_JaFechada_E409()
    {
        var conversa = Conversation.Open(Guid.CreateVersion7(), Agora);
        conversa.Close(Guid.CreateVersion7(), Agora);
        var store = new FakeConversationStore().With(conversa);
        var close = new CloseConversation(store, new FakeAuditTrail(), new RelogioFixo(Agora));

        var resultado = await close.ExecuteAsync(conversa.Id, Guid.CreateVersion7(), Contexto, CancellationToken.None);

        Assert.Equal(CloseConversationOutcome.AlreadyClosed, resultado);
    }

    // ---- ListConversations ----

    [Fact]
    public async Task ListConversations_JuntaNomeDoClienteEVendedorAtribuido()
    {
        var customerId = Guid.CreateVersion7();
        var vendedorId = Guid.CreateVersion7();
        var conversa = Conversation.Open(customerId, Agora);
        var store = new FakeConversationStore().With(conversa);
        var customers = new FakeCustomerDirectory().With(Cliente(customerId, vendedorId));

        var list = new ListConversations(store, customers);

        var resultado = await list.ExecuteAsync(null, CancellationToken.None);

        var vista = Assert.Single(resultado);
        Assert.Equal("Kianda Lda", vista.CustomerName);
        Assert.Equal(vendedorId, vista.AssignedToEmployeeId);
    }

    [Fact]
    public async Task ListConversations_FiltraPorEstado()
    {
        var aberta = Conversation.Open(Guid.CreateVersion7(), Agora);
        var fechada = Conversation.Open(Guid.CreateVersion7(), Agora);
        fechada.Close(Guid.CreateVersion7(), Agora);

        var store = new FakeConversationStore().With(aberta).With(fechada);
        var list = new ListConversations(store, new FakeCustomerDirectory());

        var resultado = await list.ExecuteAsync(ConversationStatus.Open, CancellationToken.None);

        Assert.Single(resultado);
        Assert.Equal(aberta.Id, resultado[0].ConversationId);
    }

    // ---- ListMyConversations ----

    [Fact]
    public async Task ListMyConversations_DevolveAsMensagensOrdenadas()
    {
        var customerId = Guid.CreateVersion7();
        var conversa = Conversation.Open(customerId, Agora);
        conversa.AddMessage(MessageSender.Customer, Guid.CreateVersion7(), "Primeira", Agora);
        conversa.AddMessage(MessageSender.Employee, Guid.CreateVersion7(), "Resposta", Agora.AddMinutes(10));

        var store = new FakeConversationStore().With(conversa);
        var list = new ListMyConversations(store);

        var resultado = await list.ExecuteAsync(customerId, CancellationToken.None);

        var vista = Assert.Single(resultado);
        Assert.Equal(2, vista.Messages.Count);
        Assert.Equal("Primeira", vista.Messages[0].Body);
        Assert.Equal("Resposta", vista.Messages[1].Body);
    }
}
