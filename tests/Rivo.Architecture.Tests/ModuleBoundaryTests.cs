using System.Reflection;

namespace Rivo.Architecture.Tests;

/// <summary>
/// Fronteiras entre módulos, verificadas sobre os assemblies compilados.
///
/// <para>
/// <strong>Divisão de trabalho com <see cref="ProjectReferenceTests"/>:</strong>
/// lá verificam-se as referências <em>declaradas</em> nos `.csproj`; aqui
/// verifica-se o que o código <em>efectivamente usa</em>. Uma referência
/// declarada e não usada é invisível a este ficheiro (o compilador poda-a), e
/// um uso indirecto é invisível ao outro. As regras que os dois cobririam da
/// mesma forma vivem só lá, na forma mais forte.
/// </para>
///
/// <para>
/// Estes testes existem por causa do risco número 1 do projecto. O protótipo
/// acabou com cinco implementações paralelas de aprovação e duas tabelas de
/// auditoria quase idênticas, e a causa não foi ignorância — foi que a regra
/// só existia escrita.
/// </para>
/// </summary>
public class ModuleBoundaryTests
{
    /// <summary>
    /// Nenhum módulo <em>usa</em> tipos de outro módulo que não venham dos seus
    /// contratos.
    ///
    /// <para>
    /// É a regra central do ADR-017 na sua forma substantiva: não basta que a
    /// referência declarada seja legítima, o uso real também tem de o ser.
    /// É esta que impede o God Module quando `approval` chegar.
    /// </para>
    /// </summary>
    [Fact]
    public void Module_UsesNothingFromAnotherModuleBeyondItsContracts()
    {
        var violations = new List<string>();

        foreach (var assembly in RivoAssemblies.Modules)
        {
            var module = RivoAssemblies.Module(assembly);

            violations.AddRange(RivoAssemblies
                .RivoReferences(assembly)
                .Where(reference => RivoAssemblies.Module(reference) != module)
                .Where(reference => RivoAssemblies.Layer(reference) != RivoAssemblies.ContractsLayer)
                .Select(reference =>
                    $"{RivoAssemblies.Name(assembly)} -> {reference} " +
                    $"(devia ser Rivo.{RivoAssemblies.Module(reference)}.Contracts)"));
        }

        Assert.Empty(violations);
    }

    /// <summary>
    /// Um contrato não expõe entidades de domínio.
    ///
    /// <para>
    /// É o risco que o próprio ADR-017 assinala: contratos a engordar até
    /// serem a `Application` inteira. Um contrato que devolva a entidade em vez
    /// de um DTO acopla o consumidor ao modelo interno do fornecedor, e a
    /// fronteira passa a ser decorativa.
    /// </para>
    ///
    /// <para>
    /// Só verificável aqui: é uma regra sobre <em>tipos</em>, e nenhuma leitura
    /// de `.csproj` a alcança.
    /// </para>
    /// </summary>
    [Fact]
    public void Contracts_ExposeNoTypeFromAnotherRivoAssembly()
    {
        var violations = new List<string>();

        foreach (var assembly in RivoAssemblies.InLayer(RivoAssemblies.ContractsLayer))
        {
            foreach (var type in assembly.GetExportedTypes())
            {
                violations.AddRange(SignatureTypes(type)
                    .Select(member => new { member, origin = member.Assembly.GetName().Name! })
                    .Where(x => RivoAssemblies.IsRivo(x.origin) && x.origin != RivoAssemblies.Name(assembly))
                    .Select(x => $"{type.FullName} expõe {x.member.FullName} de {x.origin}"));
            }
        }

        Assert.Empty(violations);
    }

    /// <summary>
    /// Os assemblies seguem a nomenclatura de camada combinada.
    ///
    /// <para>
    /// Não é zelo de nomenclatura: todos os testes deste projecto identificam a
    /// camada pelo sufixo do assembly. Um módulo que fugisse à convenção não
    /// seria verificado por nenhum deles — e passaria por conforme sem nunca
    /// ter sido olhado.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryModuleAssembly_UsesTheAgreedLayerNaming()
    {
        string[] camadas =
        [
            RivoAssemblies.DomainLayer,
            RivoAssemblies.ApplicationLayer,
            RivoAssemblies.InfrastructureLayer,
            RivoAssemblies.ApiLayer,
            RivoAssemblies.ContractsLayer,
        ];

        var violations = RivoAssemblies.Modules
            .Select(RivoAssemblies.Name)
            .Where(name => !camadas.Contains(RivoAssemblies.Layer(name)))
            .ToList();

        Assert.Empty(violations);
    }

    /// <summary>Tipos que aparecem na superfície pública de um tipo.</summary>
    private static IEnumerable<Type> SignatureTypes(Type type)
    {
        const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

        foreach (var property in type.GetProperties(Public))
        {
            yield return property.PropertyType;
        }

        foreach (var method in type.GetMethods(Public).Where(m => !m.IsSpecialName))
        {
            yield return method.ReturnType;

            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }
}
