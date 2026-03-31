using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using QuestPDF.Infrastructure;
using RealEstateAdmin.Data;
using RealEstateAdmin.Models;
using RealEstateAdmin.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuration de la chaîne de connexion MySQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// Configuration du DbContext pour les données de l'application
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
        mySqlOptions => mySqlOptions.SchemaBehavior(MySqlSchemaBehavior.Ignore)));

// Configuration d'Identity avec MySQL
builder.Services.AddDbContext<ApplicationIdentityDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
        mySqlOptions => mySqlOptions.SchemaBehavior(MySqlSchemaBehavior.Ignore)));

// Configuration d'Identity
builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    // Configuration des options de mot de passe
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;
    
    // Configuration des options utilisateur
    options.User.RequireUniqueEmail = true;
    
    // Configuration de la connexion
    options.SignIn.RequireConfirmedAccount = true;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationIdentityDbContext>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Login";
});

builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<PdfExportService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<IShopService, ShopService>();
builder.Services.AddScoped<IBienImmobilierService, BienImmobilierService>();
builder.Services.AddScoped<IMessageService, MessageService>();
builder.Services.AddScoped<IVenteService, VenteService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IAgendaService, AgendaService>();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddScoped<IAgentPerformanceService, AgentPerformanceService>();
builder.Services.AddHttpClient();

// Add services to the container.
builder.Services.AddControllersWithViews();

var app = builder.Build();
QuestPDF.Settings.License = LicenseType.Community;

// Démarrage: Le bootstrap initial (migrations et seeding) a été effectué manuellement pour éviter les conflits de contextes.



// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/Identity", StringComparison.OrdinalIgnoreCase))
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var query = context.Request.QueryString.Value ?? string.Empty;

        if (path.Equals("/Identity/Account/Login", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Redirect($"/Account/Login{query}");
            return;
        }

        if (path.Equals("/Identity/Account/Register", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Redirect($"/Account/Register{query}");
            return;
        }

        if (path.Equals("/Identity/Account/ConfirmEmail", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Redirect($"/Account/ConfirmEmail{query}");
            return;
        }

        context.Response.Redirect("/Account/Login");
        return;
    }

    await next();
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

