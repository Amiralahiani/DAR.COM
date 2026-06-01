# DAR.COM

Plateforme immobiliere basee sur ASP.NET Core MVC (.NET 8), MySQL et des microservices IA Python.

Ce repository contient:
- une application web complete (gestion annonces, shop, ventes, dashboard, comptes),
- un chatbot immobilier connecte au shop,
- des services IA pour l'analyse d'image, le profiling client et l'estimation de prix.

## Sommaire

1. [Architecture du projet](#architecture-du-projet)
2. [Fonctionnalites principales](#fonctionnalites-principales)
3. [Prerequis](#prerequis)
4. [Configuration](#configuration)
5. [Lancement rapide](#lancement-rapide)
6. [Lancement complet web + IA](#lancement-complet-web--ia)
7. [Endpoints utiles](#endpoints-utiles)
8. [Comptes de test](#comptes-de-test)
9. [Troubleshooting](#troubleshooting)
10. [Bonnes pratiques git](#bonnes-pratiques-git)

## Architecture du projet

| Composant | Dossier | Tech | Port | Role |
|---|---|---|---|---|
| Application web | `RealEstateAdmin-main/` | ASP.NET Core MVC, EF Core, Identity | `5160` | Back-office, front web, auth, shop, ventes |
| Chatbot API | `Chatbot/` | FastAPI + SDK OpenAI compatible Groq | `8000` | Reponses conversationnelles et recherche de biens |
| Analyse image API | `analyse image/` | FastAPI, CLIP, NudeNet, OpenCV | `8001` | Validation photo (qualite, moderation, doublons) |
| Profiling ML API | `profiling/` | FastAPI, scikit-learn | `8002` | Score lead, budget predit, segmentation client |
| Estimation prix API | `estimation prix/` | FastAPI, model prix | `8003` | Estimation prix, fourchettes, metriques, feedback ventes |

### Vue d'integration

```text
Navigateur
   |
   v
ASP.NET Core MVC (:5160)
   |-- POST /chat ------------------> Chatbot API (:8000)
   |                                     |
   |                                     '--> GET /api/shop/filter (dans l'app web)
   |
   |-- UploadAndVerify --------------> Analyse image API (:8001)
   |
   |-- PredictPrice -----------------> Estimation prix API (:8003)
   |
   '-- MLService --------------------> Profiling ML API (:8002)
```

## Fonctionnalites principales

- Authentification/autorisation avec roles `Utilisateur`, `Admin`, `SuperAdmin`.
- Wizard de creation d'annonce avec upload photo.
- Verification IA des images avant publication.
- Shop des biens publies avec filtres.
- Gestion cycle de vente et statuts metier.
- Chatbot immobilier relie aux donnees du shop.
- Profiling client et recommandations.
- Estimation de prix avec endpoint dedie et collecte de retours reels.

## Prerequis

1. .NET SDK 8.x
2. MySQL Server (local ou distant)
3. Python 3.10+ (3.13 supporte dans cet environnement)
4. Git

## Configuration

### 1) Application web

Fichier: `RealEstateAdmin-main/appsettings.json`

Cles importantes:
- `ConnectionStrings:DefaultConnection`
- `Bootstrap:SeedDefaultAccounts`
- `Bootstrap:ForcePasswordResetOnStartup`
- `Bootstrap:DisablePublicRegistration`
- `MLEngine:BaseUrl` (par defaut `http://localhost:8002`)
- `ImageAnalysisApi:BaseUrl` (par defaut `http://localhost:8001`)
- `PricePredictionApi:BaseUrl` (par defaut `http://localhost:8003`)
- `PricePredictionApi:Enabled`
- `PricePredictionApi:Endpoint` (par defaut `/estimer`)
- `ExternalBienApi:Enabled`

Pour surcharger la connexion MySQL en local:

```powershell
$env:ConnectionStrings__DefaultConnection="Server=localhost;Port=3306;Database=realestate_db;User=root;Password=YOUR_PASSWORD;"
```

### 2) Chatbot

Fichier: `Chatbot/.env` (a creer depuis `Chatbot/.env.example`)

```env
GROQ_API_KEY=YOUR_GROQ_API_KEY
REAL_ESTATE_BASE=http://127.0.0.1:5160
```

Note:
- si `GROQ_API_KEY` est absent, le chatbot tourne en mode fallback (sans LLM complet),
- le proxy web du chatbot utilise `ChatbotApi:BaseUrl` si configure, sinon `http://127.0.0.1:8000`.

## Lancement rapide

Depuis la racine du repo:

```powershell
dotnet restore
dotnet build
dotnet run --project .\RealEstateAdmin-main\RealEstateAdmin.csproj
```

Application web: `http://localhost:5160`

## Lancement complet web + IA

Ouvrir 5 terminaux.

### Terminal 1 - Application web

```powershell
cd .\DAR.COM
dotnet run --project .\RealEstateAdmin-main\RealEstateAdmin.csproj
```

### Terminal 2 - Chatbot API (:8000)

```powershell
cd .\DAR.COM\Chatbot
python -m pip install fastapi uvicorn requests openai python-dotenv
python -m uvicorn main:app --host 0.0.0.0 --port 8000
```

### Terminal 3 - Analyse image API (:8001)

```powershell
cd ".\DAR.COM\analyse image"
python -m pip install -r requirements.txt
python launch.py
```

### Terminal 4 - Profiling ML API (:8002)

```powershell
cd .\DAR.COM\profiling
python -m pip install -r requirements.txt
python main.py
```

### Terminal 5 - Estimation prix API (:8003)

```powershell
cd ".\DAR.COM\estimation prix"
python -m pip install fastapi uvicorn pandas numpy scikit-learn joblib openpyxl catboost
python api.py
```

## Endpoints utiles

### Web ASP.NET

- `GET /Chatbot/Index` - page chatbot
- `POST /chat` - proxy vers service chatbot
- `GET /api/shop/filter` - recherche biens filtres

### Chatbot API (:8000)

- `GET /`
- `POST /chat`

### Analyse image API (:8001)

- `GET /health`
- `POST /analyze`

### Profiling ML API (:8002)

- `GET /api/ml/health`
- `POST /api/ml/update_profile`
- `POST /api/ml/collect`
- `GET /api/ml/metrics`
- `POST /api/ml/retrain`

### Estimation prix API (:8003)

- `GET /health`
- `POST /estimer`
- `POST /confirmer-vente`
- `GET /metriques`

## Comptes de test

Les comptes seed sont dans `RealEstateAdmin-main/appsettings.json` section `Bootstrap:TeamAccounts`.

Exemple:
- email: `superadmin@dar.local`
- mot de passe: `DarTeam123`



