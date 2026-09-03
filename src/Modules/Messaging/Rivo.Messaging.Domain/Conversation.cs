namespace Rivo.Messaging.Domain;

/// <summary>
/// Uma conversa entre um cliente e a equipa comercial (ADR-045). Assíncrona:
/// não é chat, é mensagem em fila.
///
/// <para>
/// <strong>Uma por cliente, não uma por assunto.</strong> O cliente não
/// escolhe conversa — escreve, e cai na aberta que já houver, ou abre uma
/// nova se não houver nenhuma (a camada Application resolve isto, não o
/// agregado). Categorização por assunto pertence a "tickets de suporte", a
/// terceira capacidade adiada do Portal do Cliente — inventar aqui uma
/// segunda forma de agrupar seria antecipar essa decisão.
/// </para>
/// </summary>
public sealed class Conversation
{
    /// <summary>Uma mensagem de 4000 caracteres já é uma carta — o limite existe para não deixar a coluna sem tecto.</summary>
    private const int MaxBodyLength = 4000;

    private readonly List<Message> _messages = [];

    private Conversation(Guid id, Guid customerId, DateTimeOffset openedAt)
    {
        Id = id;
        CustomerId = customerId;
        Status = ConversationStatus.Open;
        OpenedAt = openedAt;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private Conversation()
    {
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public ConversationStatus Status { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public Guid? ClosedByUserId { get; private set; }

    public IReadOnlyList<Message> Messages => _messages;

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    public static Conversation Open(Guid customerId, DateTimeOffset at) =>
        new(Guid.CreateVersion7(), customerId, at);

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
