namespace Rivo.Procurement.Domain;

/// <summary>
/// Fornecedor. `procurement` é o dono confirmado — `docs` §3 diz que é quem
/// qualifica o fornecedor, e `finance` consome.
///
/// <para>
/// <strong>A ambiguidade era narrativa, não técnica.</strong> O documento de
/// produto lista "cadastro de fornecedores" em Finanças <em>e</em> em
/// Procurement, e a análise do protótipo corrigiu-o: existia uma única tabela
/// `suppliers`, referenciada por `purchase_orders`, `purchase_invoices` e
/// `payment_requests` sem duplicação. O ownership aqui ratifica o que os dados
/// já diziam.
/// </para>
///
/// <para>
/// <strong>Hoje `finance` guarda nome e NIF do fornecedor em texto, dentro da
/// factura de compra.</strong> Isso é o que se podia fazer sem dono; não muda
/// com este agregado, e não deve mudar retroactivamente — a factura guarda o
/// que vigorava à data, e reescrevê-la seria mudar o passado. O que muda é o
/// futuro: a factura passa a poder apontar para um fornecedor com identidade.
/// </para>
/// </summary>
public sealed class Supplier
{
    private Supplier(Guid id, string name, string taxId)
    {
        Id = id;
        Name = name;
        TaxId = taxId;
        Status = SupplierStatus.Active;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private Supplier()
    {
        Name = string.Empty;
        TaxId = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    /// <summary>
    /// NIF, normalizado sem espaços e em maiúsculas.
    ///
    /// <para>
    /// <strong>Sem validação de formato</strong>, pela mesma razão que em
    /// `commercial.Customer`: as regras de composição do NIF angolano não estão
    /// verificadas em fonte primária neste repositório, e o `CLAUDE.md` proíbe
    /// implementá-las a partir do levantamento provisório. Um validador
    /// inventado recusaria fornecedores legítimos, e recusaria com ar de
    /// correcto.
    /// </para>
    /// </summary>
    public string TaxId { get; private set; }

    /// <summary>
    /// IBAN de pagamento, opcional.
    ///
    /// <para>
    /// Opcional porque um fornecedor pode existir antes de se saber para onde
    /// se lhe paga — qualificar e pagar são momentos diferentes. Quem paga é
    /// `finance`/Tesouraria, e é lá que a ausência custa.
    /// </para>
    /// </summary>
    public string? Iban { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public SupplierStatus Status { get; private set; }

    /// <summary>
    /// Concorrência optimista (ADR-025). O domínio nunca lhe toca — quem o
    /// incrementa é o <c>SaveChangesAsync</c> do DbContext.
    /// </summary>
    public int Version { get; private set; }

    public static Supplier Register(string name, string taxId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Um fornecedor precisa de nome.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(taxId))
        {
            throw new ArgumentException(
                "Um fornecedor precisa de NIF — é o que o identifica na factura de compra.",
                nameof(taxId));
        }

        return new Supplier(Guid.CreateVersion7(), name.Trim(), NormalizeTaxId(taxId));
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Um fornecedor precisa de nome.", nameof(name));
        }

        Name = name.Trim();
    }

    public void CorrectTaxId(string taxId)
    {
        if (string.IsNullOrWhiteSpace(taxId))
        {
            throw new ArgumentException("Um fornecedor precisa de NIF.", nameof(taxId));
        }

        TaxId = NormalizeTaxId(taxId);
    }

    /// <summary>
    /// Fixa ou apaga o IBAN.
    ///
    /// <para>
    /// <strong>Aqui há validação, ao contrário do NIF, e a diferença tem razão
    /// de ser.</strong> O dígito de controlo do IBAN é a norma ISO 13616 —
    /// internacional, publicada, e igual em todos os países. Não é regra fiscal
    /// angolana, e por isso não cai na proibição do `CLAUDE.md`.
    /// </para>
    ///
    /// <para>
    /// E o custo do erro é assimétrico. Um NIF errado dá uma factura por
    /// corrigir; um IBAN errado manda dinheiro para a conta de outra pessoa, e
    /// esse não volta por se corrigir o registo. O mod-97 apanha exactamente a
    /// classe de erro que aqui acontece — um dígito trocado ao copiar.
    /// </para>
    /// </summary>
    public void SetIban(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban))
        {
            Iban = null;
            return;
        }

        var normalizado = NormalizeIban(iban);

        if (!IsWellFormedIban(normalizado))
        {
            throw new ArgumentException(
                "O IBAN não passa a verificação da norma ISO 13616. " +
                "Confirme os dígitos — um IBAN errado paga a outra pessoa.",
                nameof(iban));
        }

        Iban = normalizado;
    }

    public void ChangeContacts(string? email, string? phone)
    {
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
    }

    /// <summary>
    /// Desactiva o fornecedor.
    ///
    /// <para>
    /// <strong>Nunca eliminar</strong> (BR-14). Um fornecedor referenciado por
    /// facturas de compra e por pagamentos executados é parte desses registos,
    /// e apagá-lo deixaria histórico sem contraparte.
    /// </para>
    /// </summary>
    public void Deactivate()
    {
        Status = SupplierStatus.Inactive;
    }

    public void Reactivate()
    {
        Status = SupplierStatus.Active;
    }

    private static string NormalizeTaxId(string taxId) =>
        taxId.Replace(" ", string.Empty).Trim().ToUpperInvariant();

    private static string NormalizeIban(string iban) =>
        iban.Replace(" ", string.Empty).Replace("-", string.Empty).Trim().ToUpperInvariant();

    /// <summary>
    /// Verificação ISO 13616: comprimento entre 15 e 34, duas letras de país,
    /// dois dígitos de controlo, o resto alfanumérico — e depois o mod-97.
    ///
    /// <para>
    /// <strong>Não verifica o comprimento por país.</strong> O registo do
    /// comprimento nacional de cada IBAN é uma tabela que muda quando um país
    /// entra ou altera o esquema, e não está em fonte primária aqui. O mod-97
    /// já apanha o erro de transcrição, que é o caso real.
    /// </para>
    /// </summary>
    internal static bool IsWellFormedIban(string iban)
    {
        if (iban.Length is < 15 or > 34)
        {
            return false;
        }

        if (!char.IsAsciiLetterUpper(iban[0]) || !char.IsAsciiLetterUpper(iban[1])
            || !char.IsAsciiDigit(iban[2]) || !char.IsAsciiDigit(iban[3]))
        {
            return false;
        }

        if (!iban.All(char.IsAsciiLetterOrDigit))
        {
            return false;
        }

        // Os quatro primeiros caracteres vão para o fim, e cada letra passa a
        // dois dígitos (A=10 … Z=35). O resultado tem de dar resto 1 mod 97.
        var reordenado = string.Concat(iban.AsSpan(4), iban.AsSpan(0, 4));

        var resto = 0;

        foreach (var caracter in reordenado)
        {
            // O resto acumula-se caracter a caracter porque o número inteiro
            // não cabe em nenhum tipo primitivo: um IBAN de 34 caracteres com
            // letras chega perto de 40 algarismos.
            if (char.IsAsciiDigit(caracter))
            {
                resto = ((resto * 10) + (caracter - '0')) % 97;
            }
            else
            {
                var valor = caracter - 'A' + 10;
                resto = ((resto * 100) + valor) % 97;
            }
        }

        return resto == 1;
    }
}

public enum SupplierStatus
{
    Active,
    Inactive,
}
