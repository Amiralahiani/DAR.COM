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

// Démarrage: migrations et bootstrap de base (rôles/comptes de test/biens d'exemple)
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("StartupBootstrap");
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var identityDbContext = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();

    await identityDbContext.Database.MigrateAsync();
    await dbContext.Database.MigrateAsync();

    var roles = new[] { "SuperAdmin", "Admin", "Utilisateur" };
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    var bootstrapSection = builder.Configuration.GetSection("Bootstrap");
    var seedDefaultAccounts = bootstrapSection.GetValue<bool?>("SeedDefaultAccounts") ?? app.Environment.IsDevelopment();
    var seedSampleBiens = bootstrapSection.GetValue<bool?>("SeedSampleBiens") ?? app.Environment.IsDevelopment();

    ApplicationUser? superAdminUser = null;
    ApplicationUser? adminUser = null;

    if (seedDefaultAccounts)
    {
        var superAdminEmail = bootstrapSection["SuperAdminEmail"]
            ?? Environment.GetEnvironmentVariable("DAR_BOOTSTRAP_SUPERADMIN_EMAIL");
        var superAdminPassword = bootstrapSection["SuperAdminPassword"]
            ?? Environment.GetEnvironmentVariable("DAR_BOOTSTRAP_SUPERADMIN_PASSWORD");

        superAdminUser = await EnsurePrivilegedUserAsync(
            userManager,
            logger,
            superAdminEmail,
            superAdminPassword,
            "Super Administrateur",
            new[] { "SuperAdmin", "Admin" });

        var adminEmail = bootstrapSection["AdminEmail"]
            ?? Environment.GetEnvironmentVariable("DAR_BOOTSTRAP_ADMIN_EMAIL");
        var adminPassword = bootstrapSection["AdminPassword"]
            ?? Environment.GetEnvironmentVariable("DAR_BOOTSTRAP_ADMIN_PASSWORD");

        adminUser = await EnsurePrivilegedUserAsync(
            userManager,
            logger,
            adminEmail,
            adminPassword,
            "Administrateur",
            new[] { "Admin" });
    }
    else
    {
        logger.LogInformation("Bootstrap:SeedDefaultAccounts est désactivé.");
    }

    if (seedSampleBiens && !await dbContext.Biens.AnyAsync())
    {
        var adminUserId = adminUser?.Id ?? superAdminUser?.Id;
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            var users = await userManager.Users.OrderBy(u => u.UserName).ToListAsync();
            foreach (var user in users)
            {
                if (await userManager.IsInRoleAsync(user, "Admin"))
                {
                    adminUserId = user.Id;
                    break;
                }
            }
        }

        var biens = new List<BienImmobilier>
        {
            new BienImmobilier
            {
                Titre = "Villa moderne avec piscine",
                Description = "Magnifique villa contemporaine de 250m² avec piscine privée, jardin paysager et vue panoramique. Située dans un quartier résidentiel calme, cette propriété offre un espace de vie exceptionnel avec 5 chambres, 3 salles de bain, cuisine équipée et salon spacieux.",
                Prix = 850000,
                Adresse = "Avenue Habib Bourguiba, La Marsa, Tunis",
                Surface = 250,
                NombrePieces = 5,
                ImageUrl = "https://images.unsplash.com/photo-1613977257363-707ba9348227?w=800&h=600&fit=crop",
                UserId = adminUserId,
                PublicationStatus = "Publié",
                IsPublished = true,
                StatutCommercial = "Disponible"
            },
            new BienImmobilier
            {
                Titre = "Appartement luxueux centre-ville",
                Description = "Superbe appartement de standing au cœur de la ville, entièrement rénové. Grandes baies vitrées, balcon avec vue sur la mer, parking privé. Idéal pour investissement locatif ou résidence principale.",
                Prix = 420000,
                Adresse = "Avenue de la République, Sfax",
                Surface = 95,
                NombrePieces = 3,
                ImageUrl = "https://images.unsplash.com/photo-1522708323590-d24dbb6b0267?w=800&h=600&fit=crop",
                UserId = adminUserId,
                PublicationStatus = "Publié",
                IsPublished = true,
                StatutCommercial = "Disponible"
            },
            new BienImmobilier
            {
                Titre = "Maison de campagne avec terrain",
                Description = "Charmante maison de campagne sur terrain de 2000m². Maison rénovée avec charme, 4 chambres, 2 salles de bain, grande cuisine, salon avec cheminée. Parfait pour une famille recherchant le calme et l'espace.",
                Prix = 320000,
                Adresse = "Route de Zaghouan, Bizerte",
                Surface = 180,
                NombrePieces = 4,
                ImageUrl = "https://images.unsplash.com/photo-1570129477492-45c003edd2be?w=800&h=600&fit=crop",
                UserId = adminUserId,
                PublicationStatus = "Publié",
                IsPublished = true,
                StatutCommercial = "Disponible"
            },
            new BienImmobilier
            {
                Titre = "Studio moderne proche plage",
                Description = "Studio récent et lumineux à 200m de la plage. Idéal pour investissement locatif saisonnier ou résidence secondaire. Meublé, climatisé, avec balcon et parking.",
                Prix = 185000,
                Adresse = "Corniche de Sousse, Sousse",
                Surface = 35,
                NombrePieces = 1,
                ImageUrl = "https://images.unsplash.com/photo-1502672260266-1c1ef2d93688?w=800&h=600&fit=crop",
                UserId = adminUserId,
                PublicationStatus = "Publié",
                IsPublished = true,
                StatutCommercial = "Disponible"
            },
            new BienImmobilier
            {
                Titre = "Penthouse avec terrasse panoramique",
                Description = "Exceptionnel penthouse au dernier étage avec terrasse de 80m² offrant une vue à 360° sur la ville et la mer. 3 chambres, 2 salles de bain, cuisine ouverte design, salon double hauteur. Parking et cave inclus.",
                Prix = 1200000,
                Adresse = "Avenue Habib Bourguiba, Hammamet",
                Surface = 180,
                NombrePieces = 3,
                ImageUrl = "https://images.unsplash.com/photo-1600607687939-ce8a6c25118c?w=800&h=600&fit=crop",
                UserId = adminUserId,
                PublicationStatus = "Publié",
                IsPublished = true,
                StatutCommercial = "Disponible"
            },
            new BienImmobilier
            {
                Titre = "Duplex rénové avec jardin",
                Description = "Magnifique duplex entièrement rénové avec jardin privatif. Rez-de-chaussée avec cuisine ouverte et salon, étage avec 3 chambres et 2 salles de bain. Proche commerces et transports.",
                Prix = 550000,
                Adresse = "Rue de la Kasbah, Tunis",
                Surface = 140,
                NombrePieces = 3,
                ImageUrl = "https://images.unsplash.com/photo-1600585154340-be6161a56a0c?w=800&h=600&fit=crop",
                UserId = adminUserId,
                PublicationStatus = "Publié",
                IsPublished = true,
                StatutCommercial = "Disponible"
            },
            new BienImmobilier
            {
                Titre = "Appartement T3 avec balcon",
                Description = "Bel appartement de 75m² avec balcon, dans immeuble récent. 2 chambres, 1 salle de bain, cuisine équipée, salon lumineux. Proche métro et commerces. Idéal premier achat ou investissement.",
                Prix = 280000,
                Adresse = "Avenue Mohamed V, Monastir",
                Surface = 75,
                NombrePieces = 2,
                ImageUrl = "https://images.unsplash.com/photo-1600607687644-c7171b42498f?w=800&h=600&fit=crop",
                UserId = adminUserId,
                PublicationStatus = "Publié",
                IsPublished = true,
                StatutCommercial = "Disponible"
            },
            new BienImmobilier
            {
                Titre = "Maison familiale avec garage",
                Description = "Spacieuse maison de 200m² sur terrain de 500m². 5 chambres, 3 salles de bain, cuisine aménagée, salon, bureau, garage double. Quartier résidentiel calme, proche écoles.",
                Prix = 650000,
                Adresse = "Cité Ennasr, Ariana, Tunis",
                Surface = 200,
                NombrePieces = 5,
                ImageUrl = "https://images.unsplash.com/photo-1600596542815-ffad4c1539a9?w=800&h=600&fit=crop",
                UserId = adminUserId,
                PublicationStatus = "Publié",
                IsPublished = true,
                StatutCommercial = "Disponible"
            }
        };

        dbContext.Biens.AddRange(biens);
        await dbContext.SaveChangesAsync();
    }
}

static async Task<ApplicationUser?> EnsurePrivilegedUserAsync(
    UserManager<ApplicationUser> userManager,
    ILogger logger,
    string? email,
    string? password,
    string displayName,
    IReadOnlyCollection<string> roles)
{
    if (string.IsNullOrWhiteSpace(email))
    {
        logger.LogWarning("Bootstrap ignoré: email manquant pour {DisplayName}.", displayName);
        return null;
    }

    var user = await userManager.FindByEmailAsync(email);
    if (user == null)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Bootstrap ignoré: mot de passe manquant pour le compte {Email}.", email);
            return null;
        }

        user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            Nom = displayName
        };

        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            var errors = string.Join(" | ", createResult.Errors.Select(e => e.Description));
            logger.LogWarning("Impossible de créer {Email}: {Errors}", email, errors);
            return null;
        }
    }

    if (!user.EmailConfirmed)
    {
        user.EmailConfirmed = true;
        await userManager.UpdateAsync(user);
    }

    foreach (var role in roles)
    {
        if (!await userManager.IsInRoleAsync(user, role))
        {
            await userManager.AddToRoleAsync(user, role);
        }
    }

    return user;
}

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
using (var scope = app.Services.CreateScope())
{
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    var email = "omarsuperadmin@gmail.com";
    var password = "Ot12345";

    // Créer les rôles si besoin
    foreach (var role in new[] { "Admin", "SuperAdmin" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    // Créer l'utilisateur si inexistant
    var user = await userManager.FindByEmailAsync(email);
    if (user == null)
    {
        user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            Nom = "omar Super Admin"
        };

        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            // log / inspect createResult.Errors
        }
    }

    // Assigner les rôles (SuperAdmin + Admin)
    if (!await userManager.IsInRoleAsync(user, "SuperAdmin"))
        await userManager.AddToRoleAsync(user, "SuperAdmin");
    if (!await userManager.IsInRoleAsync(user, "Admin"))
        await userManager.AddToRoleAsync(user, "Admin");
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

