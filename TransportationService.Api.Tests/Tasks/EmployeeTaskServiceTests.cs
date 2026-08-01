using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Tasks.Entities;
using TransportationService.Api.Modules.Tasks.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Tasks;

/// <summary>
/// Sprint fasen 6-8: statusmachine, scoping (eigen/afdeling/alles), reviewflow,
/// afrondingsvereisten, herverdeling, notificaties en concurrency-token.
/// </summary>
public class EmployeeTaskServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 01, 8, 0, 0, TimeSpan.Zero);

    private sealed class PermissionStub : IPermissionAuthorizationService
    {
        public HashSet<(Guid UserId, string Code)> Grants { get; } = [];

        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(Grants.Contains((userId, permissionCode)));
    }

    private sealed record Harness(
        SqliteTestDbContext Db, PermissionStub Permissions, TestClock Clock, Guid TenantId,
        Guid ManagerUserId, Guid WorkerUserId, Guid WorkerEmployeeId,
        Guid MateUserId, Guid MateEmployeeId, Guid OutsiderUserId, Guid OutsiderEmployeeId)
    {
        public EmployeeTaskService For(Guid userId)
        {
            var tenant = new DevTenantContext(TenantId);
            var user = new DevCurrentUserContext(userId);
            return new EmployeeTaskService(Db.Context, tenant, user,
                new AuditService(Db.Context, tenant, user), Permissions,
                new NotificationService(Db.Context, tenant, user, Clock), Clock);
        }

        public void Grant(Guid userId, params string[] codes)
        {
            foreach (var code in codes)
            {
                Permissions.Grants.Add((userId, code));
            }
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var otherDepartmentId = Guid.NewGuid();
        var managerUserId = Guid.NewGuid();
        var workerUserId = Guid.NewGuid();
        var workerEmployeeId = Guid.NewGuid();
        var mateUserId = Guid.NewGuid();
        var mateEmployeeId = Guid.NewGuid();
        var outsiderUserId = Guid.NewGuid();
        var outsiderEmployeeId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Departments.AddRange(
            new Department { Id = departmentId, TenantId = tenantId, Code = "MAG", Name = "Magazijn", IsActive = true },
            new Department { Id = otherDepartmentId, TenantId = tenantId, Code = "PLAN", Name = "Planning", IsActive = true });
        db.Context.Employees.AddRange(
            new Employee { Id = workerEmployeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Janssen", DepartmentId = departmentId, IsActive = true },
            new Employee { Id = mateEmployeeId, TenantId = tenantId, EmployeeNumber = "MED-2", FirstName = "Mia", LastName = "Maat", DepartmentId = departmentId, IsActive = true },
            new Employee { Id = outsiderEmployeeId, TenantId = tenantId, EmployeeNumber = "MED-3", FirstName = "Otto", LastName = "Overkant", DepartmentId = otherDepartmentId, IsActive = true });
        db.Context.Users.AddRange(
            new TransportationService.Api.Modules.Identity.Entities.User { Id = managerUserId, TenantId = tenantId, Email = "mgr@acme.be", FirstName = "Mark", LastName = "Manager", IsActive = true },
            new TransportationService.Api.Modules.Identity.Entities.User { Id = workerUserId, TenantId = tenantId, Email = "jan@acme.be", FirstName = "Jan", LastName = "Janssen", EmployeeId = workerEmployeeId, IsActive = true },
            new TransportationService.Api.Modules.Identity.Entities.User { Id = mateUserId, TenantId = tenantId, Email = "mia@acme.be", FirstName = "Mia", LastName = "Maat", EmployeeId = mateEmployeeId, IsActive = true },
            new TransportationService.Api.Modules.Identity.Entities.User { Id = outsiderUserId, TenantId = tenantId, Email = "otto@acme.be", FirstName = "Otto", LastName = "Overkant", EmployeeId = outsiderEmployeeId, IsActive = true });
        await db.Context.SaveChangesAsync();

        var h = new Harness(db, new PermissionStub(), new TestClock(Now), tenantId,
            managerUserId, workerUserId, workerEmployeeId, mateUserId, mateEmployeeId, outsiderUserId, outsiderEmployeeId);
        // Baseline: manager mag toewijzen en alles zien; werknemers beheren eigen taken.
        h.Grant(managerUserId, PermissionCodes.TasksAssign, PermissionCodes.TasksViewAll, PermissionCodes.TasksReview, PermissionCodes.TasksCancel);
        h.Grant(workerUserId, PermissionCodes.TasksViewOwn, PermissionCodes.TasksManageOwn);
        h.Grant(mateUserId, PermissionCodes.TasksViewOwn, PermissionCodes.TasksManageOwn);
        h.Grant(outsiderUserId, PermissionCodes.TasksViewOwn, PermissionCodes.TasksManageOwn);
        return h;
    }

    private static CreateTaskRequest NewTask(Guid employeeId, bool requiresReview = false,
        bool requiresNote = false, bool requiresEvidence = false, DateTime? dueAt = null) =>
        new("Loods opruimen", [employeeId], "Zone A", null, TaskPriority.Normal,
            DueAt: dueAt, RequiresReview: requiresReview,
            RequiresCompletionNote: requiresNote, RequiresEvidence: requiresEvidence);

    [Fact]
    public async Task Create_FansOutPerEmployee_WithSharedBatch_AndNotifiesAssignees()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.For(h.ManagerUserId).CreateAsync(new CreateTaskRequest(
            "Veiligheidscontrole", [h.WorkerEmployeeId, h.MateEmployeeId]), CancellationToken.None);

        Assert.Equal(2, created.Count);
        Assert.NotNull(created[0].BatchId);
        Assert.Equal(created[0].BatchId, created[1].BatchId);
        var notifications = await h.Db.Context.Notifications.Where(n => n.Type == "task_assigned").ToListAsync();
        Assert.Equal(2, notifications.Count);
        Assert.Contains(notifications, n => n.UserId == h.WorkerUserId);
        Assert.Contains(notifications, n => n.UserId == h.MateUserId);
    }

    [Fact]
    public async Task Worker_SeesOnlyOwnTasks_TeamViewerSeesDepartment_ViewAllSeesEverything()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var manager = h.For(h.ManagerUserId);
        await manager.CreateAsync(NewTask(h.WorkerEmployeeId), CancellationToken.None);
        await manager.CreateAsync(NewTask(h.MateEmployeeId), CancellationToken.None);
        await manager.CreateAsync(NewTask(h.OutsiderEmployeeId), CancellationToken.None);

        var own = await h.For(h.WorkerUserId).ListAsync(new TaskListQuery(), CancellationToken.None);
        Assert.Equal(1, own.Total);

        h.Grant(h.WorkerUserId, PermissionCodes.TasksViewTeam);
        var team = await h.For(h.WorkerUserId).ListAsync(new TaskListQuery(), CancellationToken.None);
        Assert.Equal(2, team.Total); // Jan + Mia (Magazijn), niet Otto (Planning)

        var all = await h.For(h.ManagerUserId).ListAsync(new TaskListQuery(), CancellationToken.None);
        Assert.Equal(3, all.Total);

        // Buiten scope = 404-equivalent.
        var otherTask = all.Items.First(t => t.AssignedEmployeeId == h.OutsiderEmployeeId);
        Assert.Null(await h.For(h.WorkerUserId).GetAsync(otherTask.Id, CancellationToken.None));
    }

    [Fact]
    public async Task AssignWithoutViewAll_IsLimitedToOwnDepartment()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Grant(h.WorkerUserId, PermissionCodes.TasksAssign, PermissionCodes.TasksViewTeam);

        // Binnen de afdeling mag het …
        var created = await h.For(h.WorkerUserId).CreateAsync(NewTask(h.MateEmployeeId), CancellationToken.None);
        Assert.Single(created);

        // … buiten de afdeling niet.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.For(h.WorkerUserId).CreateAsync(NewTask(h.OutsiderEmployeeId), CancellationToken.None));
    }

    [Fact]
    public async Task WithoutAssignPermission_CanOnlyCreateForSelf()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var own = await h.For(h.WorkerUserId).CreateAsync(NewTask(h.WorkerEmployeeId), CancellationToken.None);
        Assert.Single(own);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.For(h.WorkerUserId).CreateAsync(NewTask(h.MateEmployeeId), CancellationToken.None));
    }

    [Fact]
    public async Task StatusMachine_EnforcesTransitions_AndBlockedNeedsReason()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var task = (await h.For(h.ManagerUserId).CreateAsync(NewTask(h.WorkerEmployeeId), CancellationToken.None)).Single();
        var worker = h.For(h.WorkerUserId);

        // Todo → WaitingForReview is geen geldige overgang.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            worker.SubmitForReviewAsync(task.Id, new TaskStatusActionRequest(task.Version), CancellationToken.None));

        var started = await worker.StartAsync(task.Id, new TaskStatusActionRequest(task.Version), CancellationToken.None);
        Assert.Equal(EmployeeTaskStatus.InProgress, started!.Status);

        // Blokkeren zonder reden faalt; met reden gaat de melding naar de opdrachtgever.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            worker.BlockAsync(task.Id, new TaskStatusActionRequest(started.Version), CancellationToken.None));
        var blocked = await worker.BlockAsync(task.Id, new TaskStatusActionRequest(started.Version, "Heftruck defect"), CancellationToken.None);
        Assert.Equal(EmployeeTaskStatus.Blocked, blocked!.Status);
        Assert.Equal("Heftruck defect", blocked.BlockedReason);
        Assert.Contains(await h.Db.Context.Notifications.ToListAsync(),
            n => n.Type == "task_blocked" && n.UserId == h.ManagerUserId);

        var resumed = await worker.ResumeAsync(task.Id, new TaskStatusActionRequest(blocked.Version), CancellationToken.None);
        var completed = await worker.CompleteAsync(task.Id, new TaskStatusActionRequest(resumed!.Version), CancellationToken.None);
        Assert.Equal(EmployeeTaskStatus.Completed, completed!.Status);
        Assert.NotNull(completed.CompletedAt);
    }

    [Fact]
    public async Task ReviewFlow_BlocksSelfApproval_AndRejectionNeedsComment()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var task = (await h.For(h.ManagerUserId).CreateAsync(
            NewTask(h.WorkerEmployeeId, requiresReview: true), CancellationToken.None)).Single();
        var worker = h.For(h.WorkerUserId);

        var started = await worker.StartAsync(task.Id, new TaskStatusActionRequest(task.Version), CancellationToken.None);

        // Directe voltooiing is verboden zolang review vereist is.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            worker.CompleteAsync(task.Id, new TaskStatusActionRequest(started!.Version), CancellationToken.None));

        var submitted = await worker.SubmitForReviewAsync(task.Id, new TaskStatusActionRequest(started!.Version, "Klaar"), CancellationToken.None);
        Assert.Equal(EmployeeTaskStatus.WaitingForReview, submitted!.Status);

        // De uitvoerder kan niet zelf goedkeuren, ook niet met reviewrechten.
        h.Grant(h.WorkerUserId, PermissionCodes.TasksReview);
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            worker.ReviewAsync(task.Id, new ReviewTaskRequest(submitted.Version, Approve: true), CancellationToken.None));

        // Afkeuren zonder commentaar faalt; met commentaar → terug naar InProgress + melding.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.For(h.ManagerUserId).ReviewAsync(task.Id, new ReviewTaskRequest(submitted.Version, Approve: false), CancellationToken.None));
        var rejected = await h.For(h.ManagerUserId).ReviewAsync(task.Id,
            new ReviewTaskRequest(submitted.Version, Approve: false, "Zone B vergeten"), CancellationToken.None);
        Assert.Equal(EmployeeTaskStatus.InProgress, rejected!.Status);
        Assert.Contains(await h.Db.Context.Notifications.ToListAsync(),
            n => n.Type == "task_review_rejected" && n.UserId == h.WorkerUserId);

        var resubmitted = await worker.SubmitForReviewAsync(task.Id, new TaskStatusActionRequest(rejected.Version), CancellationToken.None);
        var approved = await h.For(h.ManagerUserId).ReviewAsync(task.Id,
            new ReviewTaskRequest(resubmitted!.Version, Approve: true), CancellationToken.None);
        Assert.Equal(EmployeeTaskStatus.Completed, approved!.Status);
        Assert.Equal(h.ManagerUserId, approved.ReviewedByUserId);
    }

    [Fact]
    public async Task CompletionRequirements_NoteAndEvidence_AreEnforced()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var task = (await h.For(h.ManagerUserId).CreateAsync(
            NewTask(h.WorkerEmployeeId, requiresNote: true, requiresEvidence: true), CancellationToken.None)).Single();
        var worker = h.For(h.WorkerUserId);
        var started = await worker.StartAsync(task.Id, new TaskStatusActionRequest(task.Version), CancellationToken.None);

        // Zonder notitie geweigerd; met notitie maar zonder bewijs ook.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            worker.CompleteAsync(task.Id, new TaskStatusActionRequest(started!.Version), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            worker.CompleteAsync(task.Id, new TaskStatusActionRequest(started!.Version, "Gedaan"), CancellationToken.None));

        h.Db.Context.TaskAttachments.Add(new TaskAttachment
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TaskId = task.Id,
            FileName = "foto.jpg", ContentType = "image/jpeg", SizeBytes = 10, StorageKey = "x",
        });
        await h.Db.Context.SaveChangesAsync();

        var completed = await worker.CompleteAsync(task.Id, new TaskStatusActionRequest(started!.Version, "Gedaan"), CancellationToken.None);
        Assert.Equal(EmployeeTaskStatus.Completed, completed!.Status);
        Assert.Equal("Gedaan", completed.CompletionNote);
    }

    [Fact]
    public async Task StaleVersion_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var task = (await h.For(h.ManagerUserId).CreateAsync(NewTask(h.WorkerEmployeeId), CancellationToken.None)).Single();
        var worker = h.For(h.WorkerUserId);
        await worker.StartAsync(task.Id, new TaskStatusActionRequest(task.Version), CancellationToken.None);

        // Tweede actie met de oude versie (0) faalt.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            worker.CompleteAsync(task.Id, new TaskStatusActionRequest(task.Version), CancellationToken.None));
    }

    [Fact]
    public async Task Reopen_RequiresPermission_AndResetsToTodo()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var task = (await h.For(h.ManagerUserId).CreateAsync(NewTask(h.WorkerEmployeeId), CancellationToken.None)).Single();
        var worker = h.For(h.WorkerUserId);
        var started = await worker.StartAsync(task.Id, new TaskStatusActionRequest(task.Version), CancellationToken.None);
        var completed = await worker.CompleteAsync(task.Id, new TaskStatusActionRequest(started!.Version), CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            worker.ReopenAsync(task.Id, new TaskStatusActionRequest(completed!.Version), CancellationToken.None));

        h.Grant(h.ManagerUserId, PermissionCodes.TasksReopen);
        var reopened = await h.For(h.ManagerUserId).ReopenAsync(task.Id, new TaskStatusActionRequest(completed!.Version), CancellationToken.None);
        Assert.Equal(EmployeeTaskStatus.Todo, reopened!.Status);
        Assert.NotNull(reopened.ReopenedAt);
        Assert.Null(reopened.CompletedAt);
    }

    [Fact]
    public async Task CrossTenantEmployee_IsRejectedOnCreate()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var foreignEmployeeId = Guid.NewGuid();
        h.Db.Context.Employees.Add(new Employee
        {
            Id = foreignEmployeeId, TenantId = Guid.NewGuid(), EmployeeNumber = "X-1",
            FirstName = "Vreemd", LastName = "Volk", IsActive = true,
        });
        await h.Db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.For(h.ManagerUserId).CreateAsync(NewTask(foreignEmployeeId), CancellationToken.None));
    }

    [Fact]
    public async Task Redistribute_Reassign_MovesOpenTasks_AuditsAndNotifies()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var manager = h.For(h.ManagerUserId);
        var task = (await manager.CreateAsync(NewTask(h.WorkerEmployeeId), CancellationToken.None)).Single();
        var done = (await manager.CreateAsync(NewTask(h.WorkerEmployeeId), CancellationToken.None)).Single();
        var worker = h.For(h.WorkerUserId);
        var startedDone = await worker.StartAsync(done.Id, new TaskStatusActionRequest(done.Version), CancellationToken.None);
        await worker.CompleteAsync(done.Id, new TaskStatusActionRequest(startedDone!.Version), CancellationToken.None);

        var result = await manager.RedistributeAsync(new RedistributeTasksRequest(
            h.WorkerEmployeeId, "reassign", h.MateEmployeeId, Reason: "Langdurig afwezig"), CancellationToken.None);

        Assert.Equal(1, result.AffectedTasks); // alleen de open taak, niet de voltooide
        var moved = await h.Db.Context.EmployeeTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(h.MateEmployeeId, moved.AssignedEmployeeId);
        Assert.Contains(await h.Db.Context.AuditLogs.ToListAsync(), l => l.Action == "Redistributed");
        Assert.Contains(await h.Db.Context.Notifications.ToListAsync(),
            n => n.Type == "task_redistributed" && n.UserId == h.MateUserId);
    }

    [Fact]
    public async Task Redistribute_Cancel_RequiresCancelPermission()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Grant(h.WorkerUserId, PermissionCodes.TasksAssign, PermissionCodes.TasksViewTeam);
        var manager = h.For(h.ManagerUserId);
        await manager.CreateAsync(NewTask(h.MateEmployeeId), CancellationToken.None);

        // Jan mag toewijzen maar niet annuleren → cancel-actie geweigerd.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.For(h.WorkerUserId).RedistributeAsync(new RedistributeTasksRequest(
                h.MateEmployeeId, "cancel"), CancellationToken.None));

        var result = await manager.RedistributeAsync(new RedistributeTasksRequest(
            h.MateEmployeeId, "cancel", Reason: "Uit dienst"), CancellationToken.None);
        Assert.Equal(1, result.AffectedTasks);
        Assert.All(await h.Db.Context.EmployeeTasks.ToListAsync(),
            t => Assert.Equal(EmployeeTaskStatus.Cancelled, t.Status));
    }

    [Fact]
    public async Task OverdueFilter_AndOpenSummary_Work()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var manager = h.For(h.ManagerUserId);
        await manager.CreateAsync(NewTask(h.WorkerEmployeeId, dueAt: Now.UtcDateTime.AddDays(-1)), CancellationToken.None);
        await manager.CreateAsync(NewTask(h.WorkerEmployeeId, dueAt: Now.UtcDateTime.AddDays(2)), CancellationToken.None);

        var overdue = await manager.ListAsync(new TaskListQuery(OverdueOnly: true), CancellationToken.None);
        Assert.Equal(1, overdue.Total);
        Assert.True(overdue.Items.Single().IsOverdue);

        var summary = await manager.OpenSummaryAsync(h.WorkerEmployeeId, CancellationToken.None);
        Assert.Equal(2, summary!.Todo);
        Assert.Equal(1, summary.Overdue);
    }
}
