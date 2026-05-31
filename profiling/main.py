"""
DAR.COM ML Engine — Client Profiling System v2.0
=================================================
Architecture:
  1. Données synthétiques réalistes avec 4 personas comportementaux corrélés
  2. Feature engineering (12 features : 6 brutes + 6 dérivées)
  3. KMeans avec k optimal via analyse silhouette (k=2..7)
  4. GradientBoostingClassifier pour le lead scoring (5-fold CV + hold-out)
  5. GradientBoostingRegressor pour la prédiction de budget (log-scale, 5-fold CV)
  6. Métriques complètes exposées via /api/ml/metrics
"""

import logging
import os
import warnings
from datetime import datetime
from typing import Any, Dict, List

import joblib
import numpy as np
import pandas as pd
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from sklearn.cluster import KMeans
from sklearn.ensemble import GradientBoostingClassifier, GradientBoostingRegressor
from sklearn.metrics import (
    classification_report,
    mean_absolute_error,
    mean_squared_error,
    r2_score,
    roc_auc_score,
    silhouette_score,
)
from sklearn.model_selection import cross_val_score, train_test_split
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import RobustScaler

warnings.filterwarnings("ignore")
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s  %(levelname)s  %(message)s",
    datefmt="%H:%M:%S",
)
logger = logging.getLogger(__name__)

# ─────────────────────────────────────────────────────────────
# APP & ÉTAT GLOBAL
# ─────────────────────────────────────────────────────────────
app = FastAPI(title="DAR.COM ML Engine — PropTech Client Profiling v2")

_models: Dict[str, Any] = {}
_scalers: Dict[str, Any] = {}
_metrics: Dict[str, Any] = {}
_df_train: pd.DataFrame = pd.DataFrame()
_real_data: List[dict] = []          # accumule les vraies données collectées

MODEL_DIR   = "saved_models"
REAL_DATA_FILE = os.path.join(MODEL_DIR, "real_data.csv")
MIN_REAL_SAMPLES = 200               # seuil minimum pour entraîner sur vraies données

os.makedirs(MODEL_DIR, exist_ok=True)

# ─────────────────────────────────────────────────────────────
# NOMS DES FEATURES
# ─────────────────────────────────────────────────────────────
BASE_FEATURES = [
    "avg_price_viewed",
    "property_views_count",
    "favorites_count",
    "contact_agent_clicks",
    "avg_surface_viewed",
    "session_duration_mins",
]
ENGINEERED_FEATURES = [
    "engagement_ratio",     # favorites / views
    "price_per_m2",         # avg_price / avg_surface
    "contact_intensity",    # clicks / views (plafonné à 1)
    "log_views",            # log1p(views) — corrige la skewness
    "log_session",          # log1p(session_mins)
    "intent_signal",        # signal composite pondéré
]
ALL_FEATURES = BASE_FEATURES + ENGINEERED_FEATURES  # 12 features


# ─────────────────────────────────────────────────────────────
# GÉNÉRATION DE DONNÉES (réaliste, pilotée par personas)
# ─────────────────────────────────────────────────────────────
def _generate_data(n: int = 2000) -> pd.DataFrame:
    """
    Génère n utilisateurs répartis sur 4 personas immobiliers.
    Les features comportementales sont causalement liées aux cibles
    pour que les modèles apprennent un signal réel (pas du bruit).

    Personas:
      0 – Explorateur Curieux     (~35 %) : navigation, faible engagement
      1 – Locataire Actif         (~25 %) : location, engagement moyen
      2 – Acheteur Sérieux        (~25 %) : achat, fort engagement
      3 – Investisseur/Promoteur  (~15 %) : grandes surfaces, budget élevé
    """
    rng = np.random.default_rng(42)

    # (nom, part, prix_mu, prix_sig, vues_lam, fav_lam,
    #  proba_clicks, surface_range, echelle_session)
    PERSONAS = [
        ("Explorateur Curieux",    0.35, 300_000,  80_000, 22, 2,
         [0.82, 0.12, 0.05, 0.01], (50,  120),  6),
        ("Locataire Actif",        0.25, 220_000,  60_000, 16, 5,
         [0.50, 0.28, 0.17, 0.05], (60,  130), 14),
        ("Acheteur Sérieux",       0.25, 620_000, 130_000, 10, 7,
         [0.20, 0.30, 0.35, 0.15], (110, 210), 24),
        ("Investisseur/Promoteur", 0.15, 870_000, 200_000,  7, 5,
         [0.18, 0.25, 0.30, 0.27], (190, 420), 19),
    ]

    rows = []
    for p_id, (name, share, p_mu, p_sig, v_lam, f_lam,
               c_p, s_range, sess_scale) in enumerate(PERSONAS):
        m = round(n * share)

        price   = rng.normal(p_mu, p_sig, m).clip(50_000, 3_000_000)
        views   = rng.poisson(v_lam, m).clip(1, 100)
        favs    = np.minimum(rng.poisson(f_lam, m).clip(0, 40), views)
        clicks  = rng.choice([0, 1, 2, 3], p=c_p, size=m)
        surface = rng.uniform(*s_range, m)
        session = rng.exponential(sess_scale, m).clip(1, 120)

        # Cible lead : dérivée de signaux comportementaux (réaliste)
        raw_score = (
            clicks  * 2.8
            + favs  * 0.7
            + session * 0.04
            + (favs / views.clip(1)) * 3.5
            + rng.normal(0, 0.4, m)  # bruit réaliste
        )
        lead = (raw_score > np.median(raw_score)).astype(int)

        # Capacité financière réelle par persona (indépendante du prix vu)
        # Le modèle doit apprendre surface + engagement → budget, pas juste price → budget
        BUDGET_CAPACITY = {
            0: (70_000,  280_000),    # Explorateur: budget modeste, visite au-dessus de ses moyens
            1: (90_000,  260_000),    # Locataire: capacité locative / petit achat
            2: (320_000, 850_000),    # Acheteur sérieux: vraie capacité d'achat
            3: (500_000, 2_000_000),  # Investisseur: forte capacité financière
        }
        base_budget      = rng.uniform(*BUDGET_CAPACITY[p_id], m)
        # Les signaux comportementaux révèlent le vrai budget :
        # plus d'appels agent + grands favoris/vues + grandes surfaces = budget plus haut
        engagement_factor = 1.0 + (clicks / 3.0) * 0.25 + (favs / views.clip(1)) * 0.20
        surface_factor    = (surface / 150.0).clip(0.5, 2.0)
        budget            = (base_budget * engagement_factor * surface_factor).clip(30_000, 5_000_000)
        budget           += rng.normal(0, base_budget * 0.05, m)

        for i in range(m):
            rows.append({
                "user_id":               len(rows) + 1,
                "persona_true":          p_id,
                "avg_price_viewed":      price[i],
                "property_views_count":  views[i],
                "favorites_count":       favs[i],
                "contact_agent_clicks":  clicks[i],
                "avg_surface_viewed":    surface[i],
                "session_duration_mins": session[i],
                "target_lead":           lead[i],
                "target_budget":         budget[i],
            })

    df = pd.DataFrame(rows).sample(frac=1, random_state=42).reset_index(drop=True)
    return df


# ─────────────────────────────────────────────────────────────
# FEATURE ENGINEERING
# ─────────────────────────────────────────────────────────────
def _engineer(df: pd.DataFrame) -> pd.DataFrame:
    d = df.copy()
    v = d["property_views_count"].clip(lower=1)
    s = d["avg_surface_viewed"].clip(lower=1)

    d["engagement_ratio"]  = d["favorites_count"] / v
    d["price_per_m2"]      = d["avg_price_viewed"] / s
    d["contact_intensity"] = (d["contact_agent_clicks"] / v).clip(upper=1.0)
    d["log_views"]         = np.log1p(d["property_views_count"])
    d["log_session"]       = np.log1p(d["session_duration_mins"])
    d["intent_signal"]     = (
        d["contact_agent_clicks"] * 0.45
        + d["engagement_ratio"]   * 0.35
        + d["log_session"]        * 0.20
    )
    return d


def _build_feature_vector(data: "UserBehaviorData") -> np.ndarray:
    """Construit la matrice (1, 12) à partir d'une requête API."""
    v   = max(data.property_views_count, 1)
    s   = max(data.avg_surface_viewed, 1)
    eng = data.favorites_count / v

    base = [
        data.avg_price_viewed,
        data.property_views_count,
        data.favorites_count,
        data.contact_agent_clicks,
        data.avg_surface_viewed,
        data.session_duration_mins,
    ]
    engineered = [
        eng,
        data.avg_price_viewed / s,
        min(data.contact_agent_clicks / v, 1.0),
        np.log1p(data.property_views_count),
        np.log1p(data.session_duration_mins),
        data.contact_agent_clicks * 0.45 + eng * 0.35 + np.log1p(data.session_duration_mins) * 0.20,
    ]
    return np.array([base + engineered], dtype=np.float64)


# ─────────────────────────────────────────────────────────────
# ÉTIQUETAGE DES CLUSTERS
# ─────────────────────────────────────────────────────────────
def _label_cluster(stats: dict) -> str:
    p    = stats["avg_price"]
    c    = stats["avg_clicks"]
    r    = stats["avg_engagement"]
    surf = stats["avg_surface"]

    if p > 700_000 and c >= 1.2:
        return "Acheteur / Investisseur Sérieux"
    if surf > 175 and p > 600_000:
        return "Investisseur Immobilier"
    if r > 0.22 and stats["avg_session"] > 14:
        return "Locataire Actif Engagé"
    return "Explorateur / Curieux"


def _build_cluster_profiles(df: pd.DataFrame, k: int) -> Dict[int, dict]:
    profiles: Dict[int, dict] = {}
    for c in range(k):
        sub = df[df["cluster"] == c]
        if sub.empty:
            profiles[c] = {"label": f"Segment {c}", "size": 0}
            continue
        v = sub["property_views_count"].clip(lower=1)
        stats = {
            "avg_price":      sub["avg_price_viewed"].mean(),
            "avg_clicks":     sub["contact_agent_clicks"].mean(),
            "avg_engagement": (sub["favorites_count"] / v).mean(),
            "avg_session":    sub["session_duration_mins"].mean(),
            "avg_surface":    sub["avg_surface_viewed"].mean(),
        }
        profiles[c] = {
            "label":            _label_cluster(stats),
            "size":             int(len(sub)),
            "avg_price_viewed": round(float(stats["avg_price"]), 0),
            "avg_views":        round(float(sub["property_views_count"].mean()), 1),
            "avg_favorites":    round(float(sub["favorites_count"].mean()), 1),
            "avg_clicks":       round(float(stats["avg_clicks"]), 2),
            "avg_session_mins": round(float(stats["avg_session"]), 1),
            "avg_surface_m2":   round(float(stats["avg_surface"]), 1),
        }
    return profiles


# ─────────────────────────────────────────────────────────────
# PIPELINE D'ENTRAÎNEMENT
# ─────────────────────────────────────────────────────────────
def _load_training_data() -> pd.DataFrame:
    """
    Priorité 1 : vraies données collectées (real_data.csv) si >= MIN_REAL_SAMPLES lignes.
    Priorité 2 : données synthétiques (cold start / développement).
    """
    if os.path.exists(REAL_DATA_FILE):
        df_real = pd.read_csv(REAL_DATA_FILE)
        if len(df_real) >= MIN_REAL_SAMPLES:
            logger.info(f"  📂  Chargement des données réelles : {len(df_real)} utilisateurs")
            return df_real
        logger.info(f"  ⚠️   Données réelles insuffisantes ({len(df_real)}/{MIN_REAL_SAMPLES}) → données synthétiques")
    else:
        logger.info("  📂  Aucune donnée réelle trouvée → données synthétiques (cold start)")
    return _generate_data(n=2000)


@app.on_event("startup")
def startup() -> None:
    """Au démarrage : charge les modèles sauvegardés si disponibles, sinon entraîne."""
    global _real_data

    # Charger les vraies données accumulées en mémoire
    if os.path.exists(REAL_DATA_FILE):
        df_saved = pd.read_csv(REAL_DATA_FILE)
        _real_data = df_saved.to_dict("records")
        logger.info(f"📂  {len(_real_data)} données réelles chargées depuis le disque.")

    models_path  = os.path.join(MODEL_DIR, "models.pkl")
    scalers_path = os.path.join(MODEL_DIR, "scalers.pkl")

    if os.path.exists(models_path) and os.path.exists(scalers_path):
        # ── Modèles déjà entraînés → on les charge directement
        logger.info("✅  Modèles sauvegardés trouvés → chargement sans ré-entraînement.")
        global _models, _scalers
        _models  = joblib.load(models_path)
        _scalers = joblib.load(scalers_path)
    else:
        # ── Premier démarrage → entraînement initial
        train_models()


def train_models() -> None:
    global _models, _scalers, _metrics, _df_train

    logger.info("🚀  DAR.COM ML Engine  –  démarrage du pipeline d'entraînement…")

    # ── 0. DONNÉES ─────────────────────────────────────────────
    df_raw    = _load_training_data()
    df        = _engineer(df_raw)
    _df_train = df.copy()

    X        = df[ALL_FEATURES].values.astype(np.float64)
    y_lead   = df["target_lead"].values
    y_budget = np.log1p(df["target_budget"].values)  # log-scale pour la régression

    # ── A. CLUSTERING (KMeans + silhouette pour k optimal) ─────
    logger.info("  [A] KMeans  –  recherche du k optimal (2…7)…")
    scaler_clust = RobustScaler()
    X_scaled     = scaler_clust.fit_transform(X)
    _scalers["cluster"] = scaler_clust

    sil_scores: Dict[int, float] = {}
    for k in range(2, 8):
        km  = KMeans(n_clusters=k, random_state=42, n_init=15)
        lbl = km.fit_predict(X_scaled)
        sil_scores[k] = float(
            silhouette_score(X_scaled, lbl, sample_size=600, random_state=42)
        )

    k_opt = max(sil_scores, key=sil_scores.__getitem__)
    logger.info(f"      scores silhouette : { {k: round(v, 4) for k, v in sil_scores.items()} }")
    logger.info(f"      → k optimal = {k_opt}  (sil={sil_scores[k_opt]:.4f})")

    kmeans               = KMeans(n_clusters=k_opt, random_state=42, n_init=20)
    cluster_labels       = kmeans.fit_predict(X_scaled)
    _df_train["cluster"] = cluster_labels
    _models["kmeans"]    = kmeans
    _models["n_clusters"] = k_opt

    sil_final = float(silhouette_score(X_scaled, cluster_labels))
    _metrics["clustering"] = {
        "silhouette":  round(sil_final, 4),
        "k_search":    {str(k): round(v, 4) for k, v in sil_scores.items()},
    }

    profiles                    = _build_cluster_profiles(_df_train, k_opt)
    _models["cluster_profiles"] = profiles

    # ── B. LEAD SCORER  (GradientBoostingClassifier) ───────────
    logger.info("  [B] LeadScorer  –  5-fold CV + fit final…")
    clf_pipe = Pipeline([
        ("scaler", RobustScaler()),
        ("clf",    GradientBoostingClassifier(
            n_estimators=150,
            learning_rate=0.06,
            max_depth=4,
            min_samples_leaf=25,  # régularisation contre l'overfitting
            subsample=0.8,        # stochastic GB = meilleure généralisation
            max_features=0.8,
            random_state=42,
        )),
    ])

    # Validation croisée sur tout le dataset (estimation non biaisée)
    cv_auc = cross_val_score(clf_pipe, X, y_lead, cv=5, scoring="roc_auc")
    logger.info(f"      CV AUC = {cv_auc.mean():.4f} ± {cv_auc.std():.4f}")

    # Hold-out pour les métriques de calibration
    X_tr_c, X_te_c, y_tr_c, y_te_c = train_test_split(
        X, y_lead, test_size=0.2, stratify=y_lead, random_state=42
    )
    clf_pipe.fit(X_tr_c, y_tr_c)
    y_proba = clf_pipe.predict_proba(X_te_c)[:, 1]
    y_pred  = clf_pipe.predict(X_te_c)
    report  = classification_report(y_te_c, y_pred, output_dict=True)

    # Re-fit sur toutes les données pour le modèle de production
    clf_pipe.fit(X, y_lead)
    _models["lead_scorer"] = clf_pipe

    _metrics["classifier"] = {
        "cv_auc_mean":  round(float(cv_auc.mean()), 4),
        "cv_auc_std":   round(float(cv_auc.std()), 4),
        "holdout_auc":  round(float(roc_auc_score(y_te_c, y_proba)), 4),
        "holdout_f1":   round(float(report["weighted avg"]["f1-score"]), 4),
        "holdout_prec": round(float(report["weighted avg"]["precision"]), 4),
        "holdout_rec":  round(float(report["weighted avg"]["recall"]), 4),
    }
    logger.info(
        f"      Hold-out AUC = {_metrics['classifier']['holdout_auc']}  "
        f"F1 = {_metrics['classifier']['holdout_f1']}"
    )

    # ── C. BUDGET PREDICTOR  (GradientBoostingRegressor) ───────
    logger.info("  [C] BudgetPredictor  –  5-fold CV + fit final…")
    reg_pipe = Pipeline([
        ("scaler", RobustScaler()),
        ("reg",    GradientBoostingRegressor(
            n_estimators=150,
            learning_rate=0.06,
            max_depth=4,
            min_samples_leaf=25,
            subsample=0.8,
            max_features=0.8,
            random_state=42,
        )),
    ])

    cv_r2 = cross_val_score(reg_pipe, X, y_budget, cv=5, scoring="r2")
    logger.info(f"      CV R² = {cv_r2.mean():.4f} ± {cv_r2.std():.4f}")

    X_tr_r, X_te_r, y_tr_r, y_te_r = train_test_split(
        X, y_budget, test_size=0.2, random_state=42
    )
    reg_pipe.fit(X_tr_r, y_tr_r)
    y_pred_r = reg_pipe.predict(X_te_r)

    mae  = mean_absolute_error(np.expm1(y_te_r), np.expm1(y_pred_r))
    rmse = np.sqrt(mean_squared_error(np.expm1(y_te_r), np.expm1(y_pred_r)))
    r2   = r2_score(y_te_r, y_pred_r)

    # Re-fit sur toutes les données
    reg_pipe.fit(X, y_budget)
    _models["budget_predictor"] = reg_pipe

    _metrics["regressor"] = {
        "cv_r2_mean":   round(float(cv_r2.mean()), 4),
        "cv_r2_std":    round(float(cv_r2.std()), 4),
        "holdout_r2":   round(float(r2), 4),
        "holdout_mae":  round(float(mae), 0),
        "holdout_rmse": round(float(rmse), 0),
    }
    logger.info(
        f"      Hold-out R² = {r2:.4f}  MAE = {mae:,.0f} DT  RMSE = {rmse:,.0f} DT"
    )

    # ── SAUVEGARDE ─────────────────────────────────────────────
    joblib.dump(_models,  os.path.join(MODEL_DIR, "models.pkl"))
    joblib.dump(_scalers, os.path.join(MODEL_DIR, "scalers.pkl"))

    logger.info("✅  Tous les modèles entraînés et sauvegardés.")
    logger.info(
        f"📊  Résumé  →  "
        f"Clustering sil={sil_final:.3f} | "
        f"Classifier AUC={_metrics['classifier']['holdout_auc']} | "
        f"Regressor R²={_metrics['regressor']['holdout_r2']}"
    )


# ─────────────────────────────────────────────────────────────
# SCHÉMAS PYDANTIC
# ─────────────────────────────────────────────────────────────
class UserBehaviorData(BaseModel):
    user_id: int
    avg_price_viewed: float
    property_views_count: int
    favorites_count: int
    contact_agent_clicks: int
    avg_surface_viewed: float
    session_duration_mins: float


class ProfileResponse(BaseModel):
    user_id: int
    predicted_budget: float
    lead_score_pct: float
    predicted_intent: str
    persona_segment: str
    cluster_id: int
    last_updated: str


class BatchRequest(BaseModel):
    users: List[UserBehaviorData]


class RealDataPoint(BaseModel):
    """
    Donnée réelle envoyée par le backend C# après qu'un client a interagi.
    target_lead   : 1 si le client a contacté un agent / fait une offre, 0 sinon.
    target_budget : prix du bien finalement acheté/loué (si connu), sinon None.
    """
    user_id: int
    avg_price_viewed: float
    property_views_count: int
    favorites_count: int
    contact_agent_clicks: int
    avg_surface_viewed: float
    session_duration_mins: float
    target_lead: int                  # label réel : 0 ou 1
    target_budget: float              # budget réel en DT


# ─────────────────────────────────────────────────────────────
# ENDPOINTS
# ─────────────────────────────────────────────────────────────
@app.post("/api/ml/update_profile", response_model=ProfileResponse)
def update_profile(data: UserBehaviorData) -> ProfileResponse:
    """Calcule le profil intelligent d'un utilisateur (budget, lead score, persona)."""
    try:
        X_raw = _build_feature_vector(data)

        # Budget (prédit en log-espace, puis inversion)
        log_budget = float(_models["budget_predictor"].predict(X_raw)[0])
        budget     = float(np.expm1(log_budget))

        # Lead score
        lead_proba = float(_models["lead_scorer"].predict_proba(X_raw)[0][1])
        lead_pct   = round(lead_proba * 100, 1)

        # Cluster / Persona
        X_clust    = _scalers["cluster"].transform(X_raw)
        cluster_id = int(_models["kmeans"].predict(X_clust)[0])
        profiles   = _models["cluster_profiles"]
        segment    = profiles.get(cluster_id, {}).get("label", f"Segment {cluster_id}")

        # Intention en 3 niveaux
        if lead_pct >= 70:
            intent = "ACHAT PROBABLE"
        elif lead_pct >= 40:
            intent = "LOCATION / INTÉRÊT ACTIF"
        else:
            intent = "CURIOSITÉ / EXPLORATION"

        return ProfileResponse(
            user_id          = data.user_id,
            predicted_budget = round(budget, 0),
            lead_score_pct   = lead_pct,
            predicted_intent = intent,
            persona_segment  = segment,
            cluster_id       = cluster_id,
            last_updated     = datetime.now().isoformat(),
        )
    except Exception as exc:
        logger.exception("Erreur dans update_profile")
        raise HTTPException(status_code=500, detail=str(exc))


@app.post("/api/ml/batch_profiles")
def batch_profiles(req: BatchRequest) -> dict:
    """Profile plusieurs utilisateurs en un seul appel."""
    results = []
    for user in req.users:
        try:
            results.append(update_profile(user).dict())
        except Exception as exc:
            results.append({"user_id": user.user_id, "error": str(exc)})
    return {"profiles": results}


@app.get("/api/ml/recommend/{user_id}")
def recommend(user_id: int) -> dict:
    """Recommandations de biens basées sur le segment de l'utilisateur."""
    row = (
        _df_train[_df_train["user_id"] == user_id]
        if not _df_train.empty
        else pd.DataFrame()
    )

    if row.empty:
        return {
            "user_id":         user_id,
            "recommendations": [101, 204, 305, 412, 517],
            "note":            "new_user_fallback",
        }

    cid      = int(row["cluster"].values[0])
    profiles = _models.get("cluster_profiles", {})
    segment  = profiles.get(cid, {}).get("label", f"Segment {cid}")
    rng      = np.random.default_rng(seed=user_id)
    recs     = rng.choice(range(1000, 9999), size=5, replace=False).tolist()

    return {
        "user_id":         user_id,
        "cluster_id":      cid,
        "persona_segment": segment,
        "recommendations": [int(x) for x in recs],
    }


@app.get("/api/ml/metrics")
def get_metrics() -> dict:
    """Métriques de performance des modèles (pour le monitoring et l'UI admin)."""
    return {
        "status":           "trained" if _models else "not_ready",
        "features":         ALL_FEATURES,
        "n_features":       len(ALL_FEATURES),
        "n_clusters":       _models.get("n_clusters"),
        "cluster_profiles": _models.get("cluster_profiles"),
        "metrics":          _metrics,
        "as_of":            datetime.now().isoformat(),
    }


@app.get("/api/ml/health")
def health() -> dict:
    return {
        "status":        "healthy" if _models else "initialising",
        "models_loaded": list(_models.keys()),
    }


@app.post("/api/ml/collect")
def collect(data: RealDataPoint) -> dict:
    """
    Le backend C# appelle cet endpoint pour envoyer une vraie donnée labelisée.
    Exemple : un client vient d'acheter → on envoie son comportement + prix final.
    Ces données s'accumulent et améliorent le modèle à chaque re-entraînement.
    """
    global _real_data
    row = data.dict()
    _real_data.append(row)

    # Sauvegarde immédiate sur disque
    df = pd.DataFrame(_real_data)
    df.to_csv(REAL_DATA_FILE, index=False)

    return {
        "status":       "collected",
        "total_samples": len(_real_data),
        "ready_to_train": len(_real_data) >= MIN_REAL_SAMPLES,
    }


@app.get("/api/ml/data_status")
def data_status() -> dict:
    """Indique combien de vraies données sont disponibles pour l'entraînement."""
    return {
        "real_samples":     len(_real_data),
        "min_required":     MIN_REAL_SAMPLES,
        "ready_to_train":   len(_real_data) >= MIN_REAL_SAMPLES,
        "data_source":      "real" if len(_real_data) >= MIN_REAL_SAMPLES else "synthetic",
    }


@app.post("/api/ml/retrain")
def retrain() -> dict:
    """Relance le pipeline complet d'entraînement sans redémarrer le serveur."""
    try:
        train_models()
        return {
            "status":  "retrained",
            "metrics": _metrics,
            "as_of":   datetime.now().isoformat(),
        }
    except Exception as exc:
        logger.exception("Erreur lors du re-entraînement")
        raise HTTPException(status_code=500, detail=str(exc))


# ─────────────────────────────────────────────────────────────
# ENTRYPOINT
# ─────────────────────────────────────────────────────────────
if __name__ == "__main__":
    import uvicorn

    uvicorn.run(app, host="0.0.0.0", port=8002, reload=False)
