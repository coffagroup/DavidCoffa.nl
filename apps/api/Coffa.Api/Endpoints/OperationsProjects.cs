using Coffa.Api.Data;
using Coffa.Api.Domain;
using Coffa.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Coffa.Api.Endpoints;

public static partial class OperationsEndpoints
{
    public sealed record ProjectInput(Guid OrganizationId, string Name, string? Description, string? Status, string? Priority, DateOnly? StartDate, DateOnly? Deadline, decimal? Budget, decimal? EstimatedHours, bool SharedWithCustomer, string? RepositoryUrl, string? DeploymentUrl);
    public sealed record TaskInput(Guid OrganizationId, Guid? ProjectId, Guid? ParentTaskId, string Title, string? Description, string? AssignedToUserId, string? Status, string? Priority, DateOnly? DueDate, bool SharedWithCustomer);
    private static IQueryable<Project> AccessibleProjects(AppDbContext db, CurrentActor actor)
    {
        ReadAccess(actor, "projects.read");
        var query = db.Projects.Where(x => !x.IsArchived && db.Organizations.Any(o => o.Id == x.OrganizationId && !o.IsArchived));
        if (actor.IsCustomer) query = query.Where(x => x.OrganizationId == actor.OrganizationId && x.SharedWithCustomer);
        return query;
    }
    private static IQueryable<WorkTask> AccessibleTasks(AppDbContext db, CurrentActor actor)
    {
        ReadAccess(actor, "projects.read");
        var query = db.WorkTasks.Where(x => !x.IsArchived && db.Organizations.Any(o => o.Id == x.OrganizationId && !o.IsArchived));
        if (actor.IsCustomer) query = query.Where(x => x.OrganizationId == actor.OrganizationId && x.SharedWithCustomer && x.ProjectId != null && db.Projects.Any(p => p.Id == x.ProjectId && p.OrganizationId == x.OrganizationId && p.SharedWithCustomer && !p.IsArchived));
        return query;
    }
    private static object ProjectDto(Project project, CurrentActor actor) => actor.IsCustomer
        ? new { project.Id, project.OrganizationId, project.Name, project.Description, project.Status, project.Priority, project.StartDate, project.Deadline, project.DeploymentUrl, project.CreatedAt }
        : actor.HasPermission("finance.read") ? (object)project
        : new { project.Id, project.OrganizationId, project.Name, project.Description, project.Status, project.Priority, project.StartDate, project.Deadline, project.EstimatedHours, project.SharedWithCustomer, project.RepositoryUrl, project.DeploymentUrl, project.CreatedAt };
    private static object TaskDto(WorkTask task, bool customer) => customer
        ? new { task.Id, task.OrganizationId, task.ProjectId, task.ParentTaskId, task.Title, task.Description, task.Status, task.Priority, task.DueDate, task.CreatedAt }
        : (object)task;
    private static void MapProjects(RouteGroupBuilder api)
    {
        api.MapGet("/projects", async (AppDbContext db, CurrentActor actor, HttpRequest request, CancellationToken ct) =>
        {
            var query = AccessibleProjects(db, actor).AsNoTracking();
            if (QueryId(request, "organizationId") is { } orgId) { actor.DemandOrganization(orgId); query = query.Where(x => x.OrganizationId == orgId); }
            if (Query(request, "status") is { } status) query = query.Where(x => x.Status == status);
            if (Query(request, "q") is { } q) query = query.Where(x => EF.Functions.ILike(x.Name, "%" + q + "%") || EF.Functions.ILike(x.Description, "%" + q + "%"));
            var (page, pageSize) = Validation.Page(int.TryParse(Query(request, "page"), out var p) ? p : null, int.TryParse(Query(request, "pageSize"), out var s) ? s : null); page = Math.Min(page, 1000000);
            var total = await query.CountAsync(ct); var items = (await query.OrderByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct)).Select(x => ProjectDto(x, actor));
            return Results.Ok(new { items, total, page, pageSize });
        });
        api.MapGet("/projects/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) =>
        {
            var project = await AccessibleProjects(db, actor).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
            var tasks = (await AccessibleTasks(db, actor).AsNoTracking().Where(x => x.ProjectId == id).OrderBy(x => x.DueDate).ToListAsync(ct)).Select(x => TaskDto(x, actor.IsCustomer));
            if (actor.IsCustomer) return Results.Ok(new { project = ProjectDto(project, actor), tasks });
            var registeredHours = await db.TimeEntries.Where(x => x.ProjectId == id).SumAsync(x => (decimal?)x.DurationMinutes, ct) / 60m ?? 0;
            return Results.Ok(new { project = ProjectDto(project, actor), tasks, registeredHours });
        });
        api.MapPost("/projects", async (ProjectInput input, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("projects.write"); await OrganizationAccess(db, actor, input.OrganizationId, "projects.write", ct);
            var project = new Project(); ApplyProject(project, input, actor); db.Projects.Add(project); await db.SaveChangesAsync(ct);
            await audit.RecordAsync("project.created", "project", project.Id.ToString(), organizationId: project.OrganizationId, cancellationToken: ct); return Results.Created($"/api/projects/{project.Id}", ProjectDto(project, actor));
        });
        api.MapPut("/projects/{id:guid}", async (Guid id, ProjectInput input, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("projects.write"); var project = await db.Projects.SingleOrDefaultAsync(x => x.Id == id && !x.IsArchived, ct) ?? throw Missing();
            if (project.OrganizationId != input.OrganizationId) throw new ApiException(400, "validation", "Een project kan niet naar een andere organisatie worden verplaatst.");
            ApplyProject(project, input, actor); await db.SaveChangesAsync(ct); await audit.RecordAsync("project.updated", "project", id.ToString(), organizationId: project.OrganizationId, cancellationToken: ct); return Results.Ok(ProjectDto(project, actor));
        });
        api.MapDelete("/projects/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("projects.write"); var project = await db.Projects.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing(); project.IsArchived = true; project.Status = "archived"; await db.SaveChangesAsync(ct);
            await audit.RecordAsync("project.archived", "project", id.ToString(), organizationId: project.OrganizationId, cancellationToken: ct); return Results.NoContent();
        });
        api.MapGet("/tasks", async (AppDbContext db, CurrentActor actor, HttpRequest request, CancellationToken ct) =>
        {
            var query = AccessibleTasks(db, actor).AsNoTracking();
            if (QueryId(request, "organizationId") is { } orgId) { actor.DemandOrganization(orgId); query = query.Where(x => x.OrganizationId == orgId); }
            if (QueryId(request, "projectId") is { } projectId) query = query.Where(x => x.ProjectId == projectId);
            if (Query(request, "status") is { } status) query = query.Where(x => x.Status == status);
            if (Flag(request, "mine")) query = query.Where(x => x.AssignedToUserId == actor.UserId);
            if (Query(request, "q") is { } q) query = query.Where(x => EF.Functions.ILike(x.Title, "%" + q + "%"));
            if (actor.IsCustomer) return await Page(query.OrderBy(x => x.DueDate).Select(x => new { x.Id, x.OrganizationId, x.ProjectId, x.ParentTaskId, x.Title, x.Description, x.Status, x.Priority, x.DueDate }), request, ct);
            return await Page(query.OrderBy(x => x.DueDate).ThenByDescending(x => x.CreatedAt), request, ct);
        });
        api.MapGet("/tasks/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) =>
        { var task = await AccessibleTasks(db, actor).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing(); return Results.Ok(TaskDto(task, actor.IsCustomer)); });
        api.MapPost("/tasks", async (TaskInput input, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("projects.write"); await OrganizationAccess(db, actor, input.OrganizationId, "projects.write", ct); var task = new WorkTask(); await ApplyTask(task, input, db, actor, ct); db.WorkTasks.Add(task); await db.SaveChangesAsync(ct);
            await audit.RecordAsync("task.created", "task", task.Id.ToString(), organizationId: task.OrganizationId, cancellationToken: ct); return Results.Created($"/api/tasks/{task.Id}", task);
        });
        api.MapPut("/tasks/{id:guid}", async (Guid id, TaskInput input, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("projects.write"); var task = await db.WorkTasks.SingleOrDefaultAsync(x => x.Id == id && !x.IsArchived, ct) ?? throw Missing();
            if (input.OrganizationId != task.OrganizationId) throw new ApiException(400, "validation", "Een taak kan niet naar een andere organisatie worden verplaatst.");
            await ApplyTask(task, input, db, actor, ct); await db.SaveChangesAsync(ct); await audit.RecordAsync("task.updated", "task", id.ToString(), organizationId: task.OrganizationId, cancellationToken: ct); return Results.Ok(task);
        });
        api.MapDelete("/tasks/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) =>
        { actor.DemandPermission("projects.write"); var task = await db.WorkTasks.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing(); task.IsArchived = true; await db.SaveChangesAsync(ct); return Results.NoContent(); });
    }
    private static void ApplyProject(Project project, ProjectInput input, CurrentActor actor)
    {
        project.OrganizationId = input.OrganizationId; project.Name = Required(input.Name, "een projectnaam"); project.Description = Text(input.Description, 20000);
        project.Status = Choice(input.Status, ["draft", "planned", "development", "waiting_customer", "testing", "completed", "archived"], "draft"); project.Priority = Choice(input.Priority, Priorities, "normal");
        if (input.StartDate > input.Deadline || input.Budget is < 0 or > 1000000000m || input.EstimatedHours is < 0 or > 1000000m) throw new ApiException(400, "validation", "Controleer de datums, het budget en de geschatte uren.");
        if (input.Budget != null && !actor.HasPermission("finance.write")) Forbidden();
        if (actor.HasPermission("finance.write")) project.Budget = input.Budget;
        project.StartDate = input.StartDate; project.Deadline = input.Deadline; project.EstimatedHours = input.EstimatedHours; project.SharedWithCustomer = input.SharedWithCustomer; project.RepositoryUrl = Url(input.RepositoryUrl); project.DeploymentUrl = Url(input.DeploymentUrl);
    }
    private static async Task ApplyTask(WorkTask task, TaskInput input, AppDbContext db, CurrentActor actor, CancellationToken ct)
    {
        await CheckProject(db, actor, input.OrganizationId, input.ProjectId, ct); await CheckAssignee(db, input.AssignedToUserId, ct);
        if (input.ParentTaskId is { } parentId)
        {
            var parent = await db.WorkTasks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == parentId && !x.IsArchived && x.OrganizationId == input.OrganizationId && x.ProjectId == input.ProjectId, ct) ?? throw Missing();
            var visited = new HashSet<Guid> { task.Id };
            while (true)
            {
                if (!visited.Add(parent.Id)) throw new ApiException(400, "task_cycle", "Een taak kan niet zijn eigen bovenliggende taak zijn.");
                if (parent.ParentTaskId == null) break;
                parent = await db.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == parent.ParentTaskId, ct);
            }
        }
        task.OrganizationId = input.OrganizationId; task.ProjectId = input.ProjectId; task.ParentTaskId = input.ParentTaskId; task.Title = Required(input.Title, "een titel"); task.Description = Text(input.Description, 10000); task.AssignedToUserId = string.IsNullOrWhiteSpace(input.AssignedToUserId) ? null : input.AssignedToUserId;
        task.Status = Choice(input.Status, ["todo", "in_progress", "review", "done"], "todo"); task.Priority = Choice(input.Priority, Priorities, "normal"); task.DueDate = input.DueDate; task.SharedWithCustomer = input.SharedWithCustomer;
    }
}
