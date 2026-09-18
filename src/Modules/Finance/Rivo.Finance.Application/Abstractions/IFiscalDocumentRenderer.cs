namespace Rivo.Finance.Application.Abstractions;

/// <summary>
/// Compõe o papel de um documento fiscal.
///
/// <para>
/// Declarada aqui e implementada em Infrastructure pela mesma razão que os
/// stores: o caso de uso sabe <em>que</em> documento quer imprimir e não sabe
/// nada sobre PDF. Trocar a biblioteca de composição não toca em nada acima
/// desta linha.
/// </para>
///
/// <para>
/// Devolve <c>byte[]</c> e não <c>Stream</c> de propósito: o conteúdo vai ser
/// resumido (SHA-256) e guardado, logo é lido por inteiro de qualquer forma, e
/// um array poupa a dança de rebobinar o stream duas vezes.
/// </para>
/// </summary>
public interface IFiscalDocumentRenderer
{
    byte[] Render(FiscalDocumentPrintout printout);
}
