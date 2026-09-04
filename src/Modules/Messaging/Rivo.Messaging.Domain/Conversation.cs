namespace Rivo.Messaging.Domain;

/// <summary>
/// Uma conversa entre um cliente e a equipa comercial. Assíncrona: não é
/// chat, é mensagem em fila. Serve dois casos, distinguidos por
/// <see cref="Kind"/> — mesma máquina de estados para os dois (ADR-046):
///
/// <para>
/// <strong><see cref="ConversationKind.Message"/> (ADR-045):</strong> sem
/// assunto, uma aberta por cliente de cada vez. O cliente não escolhe
/// conversa — escreve, e cai na aberta que já houver, ou abre uma nova se
/// não houver nenhuma (a camada Application resolve isto, não o agregado).
/// </para>
///
/// <para>
/// <strong><see cref="ConversationKind.Ticket"/> (ADR-046):</strong> com
/// <see cref="Subject"/> obrigatório, e **várias abertas ao mesmo tempo por
/// cliente** — cada uma rastreia um assunto diferente, ao contrário da
/// conversa única de mensagens directas. Sem categorias fixas: o assunto é
/// texto livre, escrito pelo cliente ao abrir.
/// </para>
/// </summary>
public sealed class Conversation
{
    /// <summary>Uma mensagem de 4000 caracteres já é uma carta — o limite existe para não deixar a coluna sem tecto.</summary>
    private const int MaxBodyLength = 4000;

    private const int MaxSubjectLength = 200;

    private readonly List<Message> _messages = [];

    private Conversation(Guid id, Guid customerId, ConversationKind kind, string? subject, DateTimeOffset openedAt)
    {
        Id = id;
        CustomerId = customerId;
        Kind = kind;
        Subject = subject;
        Status = ConversationStatus.Open;
        OpenedAt = openedAt;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private Conversation()
    {
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public ConversationKind Kind { get; private set; }

    /// <summary>Só em <see cref="ConversationKind.Ticket"/> — nulo em <see cref="ConversationKind.Message"/>.</summary>
    public string? Subject { get; private set; }

    public ConversationStatus Status { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public Guid? ClosedByUserId { get; private set; }

    public IReadOnlyList<Message> Messages => _messages;

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    public static Conversation OpenMessage(Guid customerId, DateTimeOffset at) =>
        new(Guid.CreateVersion7(), customerId, ConversationKind.Message, null, at);

    /// <summary>Um ticket sem assunto não se distingue de nenhum outro — é obrigatório, ao contrário de mensagens directas.</summary>
    public static Conversation OpenTicket(Guid customerId, string subject, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("Um ticket precisa de assunto.", nameof(subject));
        }

        var texto = subject.Trim();

        if (texto.Length > MaxSubjectLength)
        {
            throw new ArgumentException(
                $"O assunto tem no máximo {MaxSubjectLength} caracteres.", nameof(subject));
        }

        return new(Guid.CreateVersion7(), customerId, ConversationKind.Ticket, texto, at);
    }

    /// <summary>
    /// Acrescenta uma mensagem — de qualquer um dos dois lados.
    ///
    /// <para>
    /// <strong>Nunca numa conversa fechada.</strong> Fechar é o Sales dizer
    /// "resolvido"; escrever a seguir sem reabrir explicitamente confundiria
    /// o que está por decidir com o que já foi. A próxima mensagem do
    /// cliente abre uma conversa nova — não reabre esta.
    /// </para>
    /// </summary>
    public Message AddMessage(MessageSender sender, Guid senderUserId, string body, DateTimeOffset at)
    {
        if (Status is ConversationStatus.Closed)
        {
            throw new InvalidOperationException(
                "Esta conversa está fechada — uma mensagem nova do cliente abre outra.");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ArgumentException("Uma mensagem não pode ir vazia.", nameof(body));
        }

        var texto = body.Trim();

        if (texto.Length > MaxBodyLength)
        {
            throw new ArgumentException(
                $"Uma mensagem tem no máximo {MaxBodyLength} caracteres.", nameof(body));
        }

        var mensagem = Message.Write(Id, sender, senderUserId, texto, at);

        _messages.Add(mensagem);

        return mensagem;
    }

    /// <summary>Sales marca como resolvida. Não apaga nada — as mensagens ficam (BR-14).</summary>
    public void Close(Guid closedByUserId, DateTimeOffset at)
    {
        if (Status is ConversationStatus.Closed)
        {
            throw new InvalidOperationException("Esta conversa já está fechada.");
        }

        Status = ConversationStatus.Closed;
        ClosedAt = at;
        ClosedByUserId = closedByUserId;
    }
}

public enum ConversationStatus
{
    Open,
    Closed,
}

public enum ConversationKind
{
    Message,
    Ticket,
}

/// <summary>Uma mensagem, imutável desde que escrita.</summary>
public sealed class Message
{
    private Message(
        Guid id, Guid conversationId, MessageSender sender, Guid senderUserId, string body, DateTimeOffset sentAt)
    {
        Id = id;
        ConversationId = conversationId;
        Sender = sender;
        SenderUserId = senderUserId;
        Body = body;
        SentAt = sentAt;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private Message() => Body = string.Empty;

    public Guid Id { get; private set; }

    public Guid ConversationId { get; private set; }

    /// <summary>Quem escreveu — o cliente, ou alguém da equipa comercial.</summary>
    public MessageSender Sender { get; private set; }

    /// <summary>
    /// A conta de `identity` de quem escreveu — o cliente, ou o Sales que
    /// respondeu. Guardado como identificador, sem resolver nome aqui: quem
    /// lê já sabe que lado é (<see cref="Sender"/>) e resolve quem é a
    /// pessoa pelo contrato certo, se precisar.
    /// </summary>
    public Guid SenderUserId { get; private set; }

    public string Body { get; private set; }

    public DateTimeOffset SentAt { get; private set; }

    internal static Message Write(
        Guid conversationId, MessageSender sender, Guid senderUserId, string body, DateTimeOffset at) =>
        new(Guid.CreateVersion7(), conversationId, sender, senderUserId, body, at);
}

public enum MessageSender
{
    Customer,
    Employee,
}
