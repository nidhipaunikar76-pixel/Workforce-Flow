using Microsoft.EntityFrameworkCore;
using Workforce_Flow.Data;
using Workforce_Flow.Infrastructure;
using Workforce_Flow.Models;
using Workforce_Flow.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSession();
builder.Services.AddSingleton<NotificationSettings>();
builder.Services.AddScoped<LeaveRequestAuditInterceptor>();
builder.Services.AddDbContext<WorkforceFlowDbContext>((services, options) =>
    options.UseSqlite(builder.Configuration.GetConnectionString("WorkforceFlow"))
        .AddInterceptors(services.GetRequiredService<LeaveRequestAuditInterceptor>()));
builder.Services.AddScoped<INotificationService, DatabaseNotificationService>();
builder.Services.AddScoped<LeavePresenceNotificationService>();
builder.Services.AddHostedService<LeavePresenceNotificationWorker>();
builder.Services.AddScoped<RoleSwitcherViewComponent>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseSession();
app.UseRouting();
app.UseAuthorization();
app.MapRazorPages();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WorkforceFlowDbContext>();
    await db.Database.EnsureCreatedAsync();
    await DbInitializer.EnsureAdditiveSchemaAsync(db);
    await DbInitializer.SeedAsync(db);
}

app.Run();

public partial class Program;
