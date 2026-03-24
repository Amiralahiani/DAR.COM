# DAR.COM - RealEstateAdmin

Application ASP.NET Core MVC (.NET 8) pour la gestion immobiliere (User / Admin / SuperAdmin), avec MySQL et Identity.

## Prerequis

1. .NET SDK 8.x
2. MySQL Server (accessible depuis votre machine)
3. (Optionnel) un client DB (MySQL Workbench, DBeaver, etc.)

## Structure du repo

- `RealEstateAdmin-main.sln`
- `RealEstateAdmin-main/` (projet web)

## Configuration rapide

1. Ouvrir `RealEstateAdmin-main/appsettings.json`
2. Verifier `ConnectionStrings:DefaultConnection`
3. Si besoin, surcharger par variable d'environnement:

```powershell
$env:ConnectionStrings__DefaultConnection="server=localhost;port=3306;database=realestate_db;user=root;password=YOUR_PASSWORD"
```

## Lancer le projet

Depuis la racine du repo:

```powershell
dotnet restore
dotnet build
dotnet run --project .\RealEstateAdmin-main\RealEstateAdmin.csproj
```

Si le port est occupe:

```powershell
dotnet run --project .\RealEstateAdmin-main\RealEstateAdmin.csproj --urls "http://localhost:5180"
```

## Comptes admin auto (optionnel, dev)

Definir ces variables dans le terminal AVANT `dotnet run`:

```powershell
$env:DAR_BOOTSTRAP_SUPERADMIN_EMAIL="superadmin@admin.com"
$env:DAR_BOOTSTRAP_SUPERADMIN_PASSWORD="MotDePasse123!"
$env:DAR_BOOTSTRAP_ADMIN_EMAIL="admin@admin.com"
$env:DAR_BOOTSTRAP_ADMIN_PASSWORD="MotDePasse123!"
```

Sans ces variables, l'application demarre quand meme, mais les comptes admin ne sont pas crees automatiquement.

## Migrations

Les migrations sont appliquees automatiquement au demarrage (dans `Program.cs`).

## Fichiers a ne pas commit

- `**/bin/`
- `**/obj/`
- `.vs/`
- fichiers temporaires/logs (`*.tmp`, `*.log`, `.tmp*/`)
- secrets reels (mots de passe, chaines de connexion sensibles)

