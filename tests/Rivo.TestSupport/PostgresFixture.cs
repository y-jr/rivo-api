using Testcontainers.PostgreSql;

namespace Rivo.TestSupport;

/// <summary>
/// PostgreSQL real, em container, para testes de integração.
///
/// <para>
/// <strong>Porquê o real e não um substituto em memória:</strong> o que estes
/// testes verificam só existe no PostgreSQL. O provider em memória do EF Core
/// não tem schemas, não tem chaves estrangeiras, não impõe restrições e —
/// decisivamente — não detecta escrita concorrente. Um teste de persistência
/// contra um substituto que não persiste como o real dá confiança falsa.
/// </para>
///
/// <para>
/// A versão da imagem acompanha a do `docker-compose.yml` (ADR-021). Testar
/// contra uma versão diferente daquela em que se corre é uma classe inteira de
/// defeitos que só aparecem em produção.
/// </para>
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("rivo_test")
        .WithUsername("rivo")
        .WithPassword("rivo_test")
        // Sem isto, o container é dado por pronto quando o processo arranca —
        // e o primeiro teste bate numa base de dados que ainda não aceita
        // ligações.
        .WithCleanUp(true)
        .Build();

    /// <summary>Connection string do container, válida depois de <see cref="InitializeAsync"/>.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>
/// Nome da colecção que partilha um container por assembly de teste.
///
/// <para>
/// A <c>[CollectionDefinition]</c> em si <strong>tem de viver no assembly dos
/// testes</strong> — o xUnit não a encontra noutro. Por isso cada projecto de
/// integração declara a sua, em quatro linhas, e reutiliza este nome e este
/// fixture. É constrangimento da ferramenta, não escolha de desenho.
/// </para>
///
/// <para>
/// Um container por assembly, e não por classe: arrancar um PostgreSQL por
/// classe multiplicaria dezenas de segundos por nada, porque os testes
/// isolam-se pelos dados que criam e não pela instância.
/// </para>
/// </summary>
public static class PostgresCollection
{
    public const string Name = "postgres";
}
