namespace Rivo.Projects.Contracts;

/// <summary>
/// Superfície publicada de `projects`. Assembly sem dependências (ADR-017).
///
/// <para>
/// <strong>Só o catálogo de permissões, por agora.</strong> Sem consumidor
/// ainda para nenhum contrato de leitura — `identity` referencia este
/// assembly só para saber que permissões existem. Criar uma interface de
/// leitura sem quem a use seria construir superfície pública para ninguém
/// (ADR-017).
/// </para>
/// </summary>
public static class ProjectsPermissions
{
    public const string ProjectsRead = "projects.projects.read";
    public const string ProjectsWrite = "projects.projects.write";

    public static readonly IReadOnlyList<string> All = [ProjectsRead, ProjectsWrite];
}
