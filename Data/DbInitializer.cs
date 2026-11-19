using Microsoft.EntityFrameworkCore;
using Workforce_Flow.Models;

namespace Workforce_Flow.Data;

public static class DbInitializer
{
    public static async Task EnsureAdditiveSchemaAsync(WorkforceFlowDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info('NotificationLogs');";
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(1));
            }
        }

        var upgrades = new[]
        {
            ("Type", "ALTER TABLE NotificationLogs ADD COLUMN Type TEXT NOT NULL DEFAULT 'Workflow';"),
            ("Audience", "ALTER TABLE NotificationLogs ADD COLUMN Audience TEXT NOT NULL DEFAULT 'Workflow';"),
            ("EventKey", "ALTER TABLE NotificationLogs ADD COLUMN EventKey TEXT NULL;"),
            ("IsRead", "ALTER TABLE NotificationLogs ADD COLUMN IsRead INTEGER NOT NULL DEFAULT 0;")
        };

        foreach (var (column, sql) in upgrades)
        {
            if (!columns.Contains(column))
            {
                await db.Database.ExecuteSqlRawAsync(sql);
            }
        }

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS ProjectTeams (
                Id INTEGER NOT NULL CONSTRAINT PK_ProjectTeams PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                ProjectCode TEXT NOT NULL,
                Description TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS TeamMemberships (
                Id INTEGER NOT NULL CONSTRAINT PK_TeamMemberships PRIMARY KEY AUTOINCREMENT,
                TeamId INTEGER NOT NULL,
                UserId INTEGER NOT NULL,
                Role TEXT NOT NULL,
                JoinedAt TEXT NOT NULL,
                CONSTRAINT FK_TeamMemberships_ProjectTeams_TeamId FOREIGN KEY (TeamId) REFERENCES ProjectTeams (Id) ON DELETE CASCADE,
                CONSTRAINT FK_TeamMemberships_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE RESTRICT
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_NotificationLogs_EventKey ON NotificationLogs (EventKey);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_TeamMemberships_TeamId_UserId ON TeamMemberships (TeamId, UserId);
            CREATE INDEX IF NOT EXISTS IX_TeamMemberships_UserId ON TeamMemberships (UserId);
            """);

        await db.Database.ExecuteSqlRawAsync("""
            UPDATE NotificationLogs
            SET Message = (
                SELECT Users.Name || ' is on approved leave.'
                FROM LeaveRequests JOIN Users ON Users.Id = LeaveRequests.EmployeeId
                WHERE LeaveRequests.Id = NotificationLogs.LeaveRequestId
            )
            WHERE Type = 'LeaveStarted' AND Audience = 'Team';

            UPDATE NotificationLogs
            SET Message = (
                SELECT Users.Name || ' has resumed after approved leave.'
                FROM LeaveRequests JOIN Users ON Users.Id = LeaveRequests.EmployeeId
                WHERE LeaveRequests.Id = NotificationLogs.LeaveRequestId
            )
            WHERE Type = 'LeaveResumed' AND Audience = 'Team';
            """);
    }

    public static async Task SeedAsync(WorkforceFlowDbContext db)
    {
        if (!await db.Users.AnyAsync())
        {
            var manager = new User { Name = "Morgan Manager", Role = UserRole.Manager };
            var employee = new User { Name = "Elliot Employee", Role = UserRole.Employee };
            var delegateManager = new User { Name = "Drew Delegate", Role = UserRole.Manager };
            var hrUser = new User { Name = "Harper HR", Role = UserRole.HR };
            db.Users.AddRange(manager, employee, delegateManager, hrUser);
            await db.SaveChangesAsync();

            employee.ManagerId = manager.Id;
            db.Delegations.Add(new Delegation
            {
                ManagerId = manager.Id,
                DelegateManagerId = delegateManager.Id,
                StartDate = DateTime.Today.AddDays(-1),
                EndDate = DateTime.Today.AddYears(1)
            });
            await db.SaveChangesAsync();
        }

        await SeedProjectTeamsAsync(db);
    }

    private static async Task SeedProjectTeamsAsync(WorkforceFlowDbContext db)
    {
        var teams = new[]
        {
            new TeamSeed("Atlas Platform", "ATLAS", "Core workforce scheduling and approvals.", "Morgan Manager", [
                new("Elliot Employee", TeamRole.Developer), new("Avery Brooks", TeamRole.Developer),
                new("Noah Patel", TeamRole.Developer), new("Sofia Chen", TeamRole.Developer),
                new("Mason Reed", TeamRole.Developer), new("Mia Foster", TeamRole.Tester),
                new("Owen Price", TeamRole.Tester), new("Jordan Blake", TeamRole.Lead),
                new("Priya Nair", TeamRole.SolutionArchitect)]),
            new TeamSeed("Pulse Mobile", "PULSE", "Employee-facing mobile experience.", "Casey Rivera", [
                new("Liam Carter", TeamRole.Developer), new("Zoe Morgan", TeamRole.Developer),
                new("Ezra Brooks", TeamRole.Developer), new("Ivy Cooper", TeamRole.Developer),
                new("Leo Bennett", TeamRole.Developer), new("Nora Kim", TeamRole.Tester),
                new("Ethan Ward", TeamRole.Tester), new("Asha Patel", TeamRole.Lead),
                new("Theo Clarke", TeamRole.SolutionArchitect)]),
            new TeamSeed("Northstar Insights", "NORTHSTAR", "Operational reporting and workforce insights.", "Riley Jordan", [
                new("Ava Turner", TeamRole.Developer), new("Finn Hayes", TeamRole.Developer),
                new("Ruby Singh", TeamRole.Developer), new("Caleb Ross", TeamRole.Developer),
                new("Luna Gray", TeamRole.Developer), new("Jack Ellis", TeamRole.Tester),
                new("Ella James", TeamRole.Tester), new("Aria Scott", TeamRole.Lead),
                new("Theo Martin", TeamRole.SolutionArchitect)])
        };

        var usersByName = await db.Users.ToDictionaryAsync(user => user.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var managerName in teams.Select(team => team.ManagerName).Distinct())
        {
            if (!usersByName.ContainsKey(managerName))
            {
                var manager = new User { Name = managerName, Role = UserRole.Manager };
                db.Users.Add(manager);
                usersByName.Add(managerName, manager);
            }
        }

        await db.SaveChangesAsync();
        usersByName = await db.Users.ToDictionaryAsync(user => user.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var team in teams)
        {
            var manager = usersByName[team.ManagerName];
            foreach (var member in team.Members)
            {
                if (!usersByName.TryGetValue(member.Name, out var user))
                {
                    user = new User
                    {
                        Name = member.Name,
                        Role = UserRole.Employee,
                        ManagerId = manager.Id
                    };
                    db.Users.Add(user);
                    usersByName.Add(member.Name, user);
                }
                else if (user.Role == UserRole.Employee && user.ManagerId is null)
                {
                    user.ManagerId = manager.Id;
                }
            }
        }

        await db.SaveChangesAsync();

        var teamsByCode = await db.ProjectTeams.ToDictionaryAsync(team => team.ProjectCode, StringComparer.OrdinalIgnoreCase);
        foreach (var team in teams)
        {
            if (!teamsByCode.ContainsKey(team.ProjectCode))
            {
                var projectTeam = new ProjectTeam
                {
                    Name = team.Name,
                    ProjectCode = team.ProjectCode,
                    Description = team.Description
                };
                db.ProjectTeams.Add(projectTeam);
                teamsByCode.Add(team.ProjectCode, projectTeam);
            }
        }

        await db.SaveChangesAsync();
        var existingMemberships = await db.TeamMemberships
            .Select(membership => new { membership.TeamId, membership.UserId })
            .ToListAsync();
        foreach (var team in teams)
        {
            var projectTeam = teamsByCode[team.ProjectCode];
            var members = team.Members.Append(new TeamMemberSeed(team.ManagerName, TeamRole.Manager));
            foreach (var member in members)
            {
                var user = usersByName[member.Name];
                if (!existingMemberships.Any(existing => existing.TeamId == projectTeam.Id && existing.UserId == user.Id))
                {
                    db.TeamMemberships.Add(new TeamMembership
                    {
                        TeamId = projectTeam.Id,
                        UserId = user.Id,
                        Role = member.Role,
                        JoinedAt = DateTime.UtcNow
                    });
                }
            }
        }

        await db.SaveChangesAsync();
    }

    private sealed record TeamSeed(string Name, string ProjectCode, string Description, string ManagerName, TeamMemberSeed[] Members);
    private sealed record TeamMemberSeed(string Name, TeamRole Role);
}