using Rivo.TestSupport;

namespace Rivo.Notifications.Infrastructure.Tests;

/// <summary>
/// Liga este assembly ao container partilhado de <see cref="PostgresFixture"/>.
///
/// Tem de estar aqui, e não em `Rivo.TestSupport`: o xUnit só encontra uma
/// <c>[CollectionDefinition]</c> no mesmo assembly dos testes que a usam.
/// </summary>
[CollectionDefinition(PostgresCollection.Name)]
public sealed class PostgresCollectionDefinition : ICollectionFixture<PostgresFixture>;
