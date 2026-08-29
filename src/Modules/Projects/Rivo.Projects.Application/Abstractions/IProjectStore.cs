using Rivo.Projects.Domain;

namespace Rivo.Projects.Application.Abstractions;

/// <summary>
/// Persistência de `projects`. Definida aqui e implementada em
/// Infrastructure, para que os casos de uso não conheçam o EF Core.
/// </summary>
public interface IProjectStore
{
    Task<Project?> FindAsync(Guid projectId, CancellationToken cancellationToken);

    Task<Project?> FindForUpdateAsync(Guid projectId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Project>> ListAsync(bool includeClosed, CancellationToken cancellationToken);

    Task AddAsync(Project project, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
