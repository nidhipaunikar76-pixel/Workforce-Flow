using Microsoft.EntityFrameworkCore;
using Workforce_Flow.Models;

namespace Workforce_Flow.Data;

public class WorkforceFlowDbContext(DbContextOptions<WorkforceFlowDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Delegation> Delegations => Set<Delegation>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    public DbSet<ProjectTeam> ProjectTeams => Set<ProjectTeam>();
    public DbSet<TeamMembership> TeamMemberships => Set<TeamMembership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().Property(user => user.Role).HasConversion<string>();
        modelBuilder.Entity<User>().HasOne<User>().WithMany().HasForeignKey(user => user.ManagerId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Delegation>().HasOne<User>().WithMany().HasForeignKey(item => item.ManagerId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Delegation>().HasOne<User>().WithMany().HasForeignKey(item => item.DelegateManagerId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<LeaveRequest>().Property(request => request.Status).HasConversion<string>();
        modelBuilder.Entity<LeaveRequest>().HasIndex(request => request.IdempotencyKey).IsUnique();
        modelBuilder.Entity<LeaveRequest>().HasOne<User>().WithMany().HasForeignKey(request => request.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<LeaveRequest>().HasOne<User>().WithMany().HasForeignKey(request => request.ApproverId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AuditLog>().HasOne(log => log.LeaveRequest).WithMany().HasForeignKey(log => log.LeaveRequestId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AuditLog>().HasOne(log => log.Actor).WithMany().HasForeignKey(log => log.ActorId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<NotificationLog>().HasOne<LeaveRequest>().WithMany().HasForeignKey(log => log.LeaveRequestId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<NotificationLog>().HasOne<User>().WithMany().HasForeignKey(log => log.RecipientId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<NotificationLog>().Property(log => log.Type).HasConversion<string>().HasDefaultValue(NotificationType.Workflow);
        modelBuilder.Entity<NotificationLog>().Property(log => log.Audience).HasConversion<string>().HasDefaultValue(NotificationAudience.Workflow);
        modelBuilder.Entity<NotificationLog>().HasIndex(log => log.EventKey).IsUnique();

        modelBuilder.Entity<TeamMembership>().Property(membership => membership.Role).HasConversion<string>();
        modelBuilder.Entity<TeamMembership>().HasIndex(membership => new { membership.TeamId, membership.UserId }).IsUnique();
        modelBuilder.Entity<TeamMembership>().HasOne(membership => membership.Team).WithMany(team => team.Memberships).HasForeignKey(membership => membership.TeamId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<TeamMembership>().HasOne(membership => membership.User).WithMany().HasForeignKey(membership => membership.UserId).OnDelete(DeleteBehavior.Restrict);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAuditLogImmutability();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardAuditLogImmutability();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void GuardAuditLogImmutability()
    {
        if (ChangeTracker.Entries<AuditLog>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Audit log entries are immutable.");
        }
    }
}