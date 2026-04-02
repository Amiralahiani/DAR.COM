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

## Comptes equipe centralises

Les comptes prets a l'emploi sont centralises dans:

- `RealEstateAdmin-main/appsettings.json`
- `RealEstateAdmin-main/appsettings.Development.json`

Cle de configuration:

- `Bootstrap:SeedDefaultAccounts` active le bootstrap au demarrage
- `Bootstrap:ForcePasswordResetOnStartup` remet les mots de passe configures a chaque lancement
- `Bootstrap:DisablePublicRegistration` bloque l'inscription libre et force l'usage des comptes equipes
- `Bootstrap:TeamAccounts` contient toute la liste des comptes (SuperAdmin/Admin/Utilisateur)


## Fichiers a ne pas commit

- `**/bin/`
- `**/obj/`

- secrets reels (mots de passe, chaines de connexion sensibles)

