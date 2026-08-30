using Microsoft.EntityFrameworkCore;
using Rivo.Projects.Application.Abstractions;
using Rivo.Projects.Domain;

namespace Rivo.Projects.Infrastructure.Persistence;

public sealed class ProjectStore(ProjectsDbContext context) : IProjectStore
{
    public async Task<Project?> FindAsync(Guid projectId, CancellationToken cancellationToken) =>
        await context.Projects.AsNoTracking()
            .Include(p => p.Milestones)
            .Include(p => p.Tasks)
            .Include(p => p.Budget)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

    public async Task<Project?> FindForUpdateAsync(Guid projectId, CancellationToken cancellationToken) =>
        await context.Projects
            .Include(p => p.Milestones)
            .Include(p => p.Tasks)
            .Include(p => p.Budget)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

    public async Task<IReadOnlyList<Project>> ListAsync(bool includeClosed, CancellationToken cancellationToken)
    {
        var query = context.Projects.AsNoTracking()
            .Include(p => p.Milestones)
            .Include(p => p.Tasks)
            .Include(p => p.Budget)
            .AsQueryable();

        if (!includeClosed)
        {
            query = query.Where(p => p.Status == ProjectStatus.Active);
        }

        return await query.OrderBy(p => p.Name).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Project project, CancellationToken cancellationToken) =>
        await context.Projects.AddAsync(project, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
