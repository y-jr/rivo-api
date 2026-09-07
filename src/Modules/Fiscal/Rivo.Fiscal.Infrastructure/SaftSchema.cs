using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace Rivo.Fiscal.Infrastructure;

/// <summary>
/// O XSD do SAF-T AO 1.01_01, e a validação contra ele.
///
/// <para>
/// <strong>Existe para ser usado nos testes, não só em produção.</strong> O
/// contrato de completude é explícito: «a exportação gerada deve ser validada
/// contra o XSD nos testes, não apenas em produção». Um gerador de XML sem
/// validação contra esquema produz ficheiros plausíveis que a AGT recusa, e a
/// diferença entre plausível e válido só se descobre no dia da entrega.
/// </para>
///
/// <para>
/// O esquema é carregado uma vez e guardado: são 3000 linhas, e compilá-lo a
/// cada validação seria trabalho repetido sem razão.
/// </para>
/// </summary>
public static class SaftSchema
{
    private const string RecursoIncorporado =
        "Rivo.Fiscal.Infrastructure.Schemas.SAFTAO1_01_01.xsd";

    private static readonly Lazy<XmlSchemaSet> Conjunto = new(Carregar);

    /// <summary>
    /// Valida o documento contra o XSD.
    ///
    /// <para>
    /// Devolve a lista de erros, vazia quando o ficheiro é válido. Devolver a
    /// lista em vez de lançar é deliberado: quem chama quer normalmente
    /// mostrar <em>todos</em> os problemas de uma vez, e não o primeiro.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Validar(XDocument documento)
    {
        List<string> erros = [];

        documento.Validate(
            Conjunto.Value,
            (_, args) => erros.Add(
                $"{args.Severity}: {args.Message}"
                + (args.Exception is { LineNumber: > 0 } e ? $" (linha {e.LineNumber})" : string.Empty)));

        return erros;
    }

    private static XmlSchemaSet Carregar()
    {
        var assembly = Assembly.GetExecutingAssembly();

        // O nome do recurso é gerado pelo SDK a partir do caminho, e os pontos
        // do nome do ficheiro tornam-no difícil de adivinhar — daí procurar
        // pelo sufixo em vez de o escrever à mão e falhar em silêncio.
        var nome = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(".xsd", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"O XSD do SAF-T não está incorporado no assembly. Esperado algo como "
                + $"'{RecursoIncorporado}'; encontrados: "
                + string.Join(", ", assembly.GetManifestResourceNames()));

        using var fluxo = assembly.GetManifestResourceStream(nome)!;
        using var leitor = XmlReader.Create(fluxo);

        var conjunto = new XmlSchemaSet();
        conjunto.Add(null, leitor);
        conjunto.Compile();

        return conjunto;
    }
}
