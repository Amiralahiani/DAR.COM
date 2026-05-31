"""
API FastAPI — Estimation immobilière.

Endpoints :
  POST /estimer          → prédit le prix d'un bien
  POST /confirmer-vente  → enregistre le vrai prix de vente
  GET  /metriques        → performances du modèle en production
  GET  /health           → statut

Lancement :
  pip install fastapi uvicorn
  python api.py
  → http://localhost:8003/docs  (interface interactive automatique)
"""
from __future__ import annotations

import json
import sqlite3
import subprocess
import sys
import threading
import uuid
import warnings
from contextlib import contextmanager
from datetime import datetime
from pathlib import Path
from typing import Optional

import joblib
import numpy as np
import pandas as pd
import uvicorn
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel

from common_utils import normalize_zone_columns, infer_simplified_nature, normalize_etat

ROOT       = Path(__file__).resolve().parent
DB_PATH    = ROOT / "data" / "predictions_log.db"
MODEL_PATH = ROOT / "model" / "model_prix_total.pkl"
CALIB_PATH = ROOT / "data" / "calibration_segments.json"
REF_DELEG  = ROOT / "refs_terrain" / "ref_terrain_delegation.xlsx"
REF_GOUV   = ROOT / "refs_terrain" / "ref_terrain_gouvernorat.xlsx"
REF_GLOBAL = ROOT / "refs_terrain" / "ref_terrain_global.xlsx"

EQUIP_COLS = [
    "has_ascenseur", "has_balcon", "has_chauffage_central", "has_climatisation",
    "has_garage", "has_jardin", "has_parking", "has_piscine", "has_terrasse",
]

# ---------------------------------------------------------------------------
# Chargement modèle (rechargeable après retrain)
# ---------------------------------------------------------------------------

SEUIL_RETRAIN   = 50    # nb de nouvelles ventes confirmées pour déclencher un retrain
_retrain_running = False


def load_model():
    global pipe, NUMERIC, CAT, bien_refs, calib
    bundle    = joblib.load(MODEL_PATH)
    pipe      = bundle["pipeline"]
    NUMERIC   = bundle["numeric_cols"]
    CAT       = bundle["cat_cols"]
    bien_refs = bundle.get("bien_ref_tables")
    calib     = json.loads(CALIB_PATH.read_text(encoding="utf-8"))


load_model()

ref_deleg  = pd.read_excel(REF_DELEG)
ref_gouv   = pd.read_excel(REF_GOUV)
ref_global = pd.read_excel(REF_GLOBAL)


def _run_retrain():
    global _retrain_running
    try:
        print("[retrain] Démarrage du réentraînement automatique...")
        result = subprocess.run(
            [sys.executable, str(ROOT / "retrain.py"), "--min-nouvelles", str(SEUIL_RETRAIN)],
            cwd=str(ROOT), capture_output=True, text=True,
        )
        print(result.stdout)
        if result.returncode == 0:
            load_model()
            print("[retrain] Modèle rechargé avec succès.")
        else:
            print("[retrain] Erreur :", result.stderr)
    finally:
        _retrain_running = False

# ---------------------------------------------------------------------------
# Base de données SQLite (log des prédictions)
# ---------------------------------------------------------------------------

@contextmanager
def get_db():
    con = sqlite3.connect(DB_PATH)
    try:
        yield con
        con.commit()
    finally:
        con.close()


def init_db() -> None:
    with get_db() as con:
        con.execute("""
            CREATE TABLE IF NOT EXISTS predictions_log (
                id                    TEXT PRIMARY KEY,
                timestamp             TEXT,
                nature_bien           TEXT,
                gouvernorat           TEXT,
                delegation            TEXT,
                surface_m2            REAL,
                nb_chambres           REAL,
                has_ascenseur         INTEGER DEFAULT 0,
                has_balcon            INTEGER DEFAULT 0,
                has_chauffage_central INTEGER DEFAULT 0,
                has_climatisation     INTEGER DEFAULT 0,
                has_garage            INTEGER DEFAULT 0,
                has_jardin            INTEGER DEFAULT 0,
                has_parking           INTEGER DEFAULT 0,
                has_piscine           INTEGER DEFAULT 0,
                has_terrasse          INTEGER DEFAULT 0,
                etat                  TEXT,
                prix_predit_tnd       REAL,
                prix_fourchette_basse REAL,
                prix_fourchette_haute REAL,
                confiance             TEXT,
                prix_reel_tnd         REAL
            )
        """)
        con.execute("ALTER TABLE predictions_log ADD COLUMN etat TEXT"
                    ) if "etat" not in [
            r[1] for r in con.execute("PRAGMA table_info(predictions_log)").fetchall()
        ] else None


init_db()

# ---------------------------------------------------------------------------
# Préparation des features (même logique que le script de prédiction)
# ---------------------------------------------------------------------------

def prepare_features(d: dict) -> pd.DataFrame:
    df = pd.DataFrame([d])
    df = normalize_zone_columns(df)
    df["nature_bien_simple"] = df["nature_bien"].map(infer_simplified_nature)
    df["surface_m2"]  = pd.to_numeric(df["surface_m2"], errors="coerce")
    df["nb_chambres"] = pd.to_numeric(df.get("nb_chambres"), errors="coerce")
    df["nb_chambres_missing"] = df["nb_chambres"].isna().astype(int)
    df["nb_chambres"] = df["nb_chambres"].fillna(0)

    for col in EQUIP_COLS:
        df[col] = pd.to_numeric(df.get(col, 0), errors="coerce").fillna(0).clip(0, 1)
    df["standing_score"] = df[EQUIP_COLS].sum(axis=1)

    # État du bien — catégorielle passée directement à CatBoost
    df["etat_estime"] = normalize_etat(d.get("etat"))

    df = df.merge(
        ref_deleg[["gouvernorat_norm", "delegation_norm", "nb_terrains", "median_prix_m2_terrain"]],
        on=["gouvernorat_norm", "delegation_norm"], how="left",
    ).merge(
        ref_gouv[["gouvernorat_norm", "nb_terrains_gouv", "median_prix_m2_terrain_gouv"]],
        on="gouvernorat_norm", how="left",
    )

    global_med = float(ref_global.iloc[0]["median_prix_m2_terrain_global"])
    df["prix_m2_terrain_ref"] = df["median_prix_m2_terrain"]
    df["niveau_fallback_terrain"] = "delegation"
    m = df["prix_m2_terrain_ref"].isna() & df["median_prix_m2_terrain_gouv"].notna()
    df.loc[m, "prix_m2_terrain_ref"] = df.loc[m, "median_prix_m2_terrain_gouv"]
    df.loc[m, "niveau_fallback_terrain"] = "gouvernorat"
    m = df["prix_m2_terrain_ref"].isna()
    df.loc[m, "prix_m2_terrain_ref"] = global_med
    df.loc[m, "niveau_fallback_terrain"] = "global"

    df["log_surface"]      = np.log1p(df["surface_m2"])
    df["surface_squared"]  = df["surface_m2"] ** 2
    df["surface_per_room"] = np.where(df["nb_chambres"] > 0, df["surface_m2"] / df["nb_chambres"], df["surface_m2"])
    df["rooms_density"]    = np.where(df["surface_m2"] > 0, df["nb_chambres"] / df["surface_m2"], 0)
    df["ratio_prix_m2_vs_terrain_ref"] = np.nan
    df["ecart_pct_vs_terrain_ref"]     = np.nan
    df["ratio_prix_m2_vs_bien_ref"]    = np.nan
    df["ecart_pct_vs_bien_ref"]        = np.nan

    if bien_refs:
        for ref_key, merge_cols in [
            ("deleg_type", ["gouvernorat_norm", "delegation_norm", "nature_bien_simple"]),
            ("gouv_type",  ["gouvernorat_norm", "nature_bien_simple"]),
            ("global_type", ["nature_bien_simple"]),
        ]:
            t = bien_refs.get(ref_key)
            if t is not None:
                df = df.merge(t, on=merge_cols, how="left")

        df["prix_m2_bien_ref"] = df.get("median_prix_m2_bien_zone", np.nan)
        df["niveau_fallback_bien_ref"] = "delegation_type"
        for src_col, fallback_level in [
            ("median_prix_m2_bien_gouv_type", "gouvernorat_type"),
            ("median_prix_m2_bien_global_type", "global_type"),
        ]:
            if src_col in df.columns:
                m = df["prix_m2_bien_ref"].isna() & df[src_col].notna()
                df.loc[m, "prix_m2_bien_ref"] = df.loc[m, src_col]
                df.loc[m, "niveau_fallback_bien_ref"] = fallback_level

        nb_z  = df.get("nb_biens_zone_type",   pd.Series([np.nan] * len(df)))
        nb_g  = df.get("nb_biens_gouv_type",   pd.Series([np.nan] * len(df)))
        nb_gl = df.get("nb_biens_global_type", pd.Series([np.nan] * len(df)))
        df["nb_biens_zone_type"] = nb_z.fillna(nb_g).fillna(nb_gl)
        df["markup_bati_vs_terrain"] = np.where(
            df["prix_m2_terrain_ref"] > 0,
            df["prix_m2_bien_ref"] / df["prix_m2_terrain_ref"],
            np.nan,
        )
    else:
        df["prix_m2_bien_ref"]        = np.nan
        df["markup_bati_vs_terrain"]  = np.nan
        df["nb_biens_zone_type"]      = np.nan
        df["niveau_fallback_bien_ref"] = "unknown"

    return df

# ---------------------------------------------------------------------------
# Modèles Pydantic (validation des entrées)
# ---------------------------------------------------------------------------

class BienInput(BaseModel):
    nature_bien:           str
    gouvernorat:           str
    delegation:            str
    surface_m2:            float
    nb_chambres:           Optional[float] = None
    has_ascenseur:         int = 0
    has_balcon:            int = 0
    has_chauffage_central: int = 0
    has_climatisation:     int = 0
    has_garage:            int = 0
    has_jardin:            int = 0
    has_parking:           int = 0
    has_piscine:           int = 0
    has_terrasse:          int = 0
    etat:                  Optional[str] = None  # "bon_etat" | "etat_moyen" | "a_renover"


class ConfirmationVente(BaseModel):
    prediction_id: str
    prix_reel_tnd: float

# ---------------------------------------------------------------------------
# Application
# ---------------------------------------------------------------------------

app = FastAPI(
    title="API Estimation Immobilière",
    description="Prédit le prix d'un bien résidentiel avec fourchette et niveau de confiance.",
    version="2.0",
)


@app.get("/health")
def health():
    with get_db() as con:
        n_total     = con.execute("SELECT COUNT(*) FROM predictions_log").fetchone()[0]
        n_confirmes = con.execute("SELECT COUNT(*) FROM predictions_log WHERE prix_reel_tnd IS NOT NULL").fetchone()[0]
    return {
        "status": "ok",
        "modele": MODEL_PATH.name,
        "total_predictions": n_total,
        "ventes_confirmees": n_confirmes,
    }


@app.post("/estimer")
def estimer(bien: BienInput):
    nature_simple = infer_simplified_nature(bien.nature_bien)
    feat = prepare_features(bien.model_dump())
    for col in NUMERIC + CAT:
        if col not in feat.columns:
            feat[col] = np.nan

    pred = float(np.expm1(np.clip(pipe.predict(feat[NUMERIC + CAT]), -20, 20))[0])

    seg = calib["segments"].get(nature_simple, calib["global"])
    fourchette_basse = round(pred * seg["ratio_p25"])
    fourchette_haute = round(pred * seg["ratio_p75"])
    seuil_bas = calib.get("seuil_prix_bas_tnd", 100_000)
    if pred < seuil_bas:
        confiance = "faible"
        message   = calib.get("message_prix_bas", seg["message"])
    else:
        confiance = seg["confiance"]
        message   = seg["message"]

    pred_id = str(uuid.uuid4())
    with get_db() as con:
        con.execute("""
            INSERT INTO predictions_log (
                id, timestamp, nature_bien, gouvernorat, delegation,
                surface_m2, nb_chambres,
                has_ascenseur, has_balcon, has_chauffage_central, has_climatisation,
                has_garage, has_jardin, has_parking, has_piscine, has_terrasse,
                etat, prix_predit_tnd, prix_fourchette_basse, prix_fourchette_haute, confiance
            ) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)
        """, (
            pred_id, datetime.now().isoformat(),
            bien.nature_bien, bien.gouvernorat, bien.delegation,
            bien.surface_m2, bien.nb_chambres,
            bien.has_ascenseur, bien.has_balcon, bien.has_chauffage_central,
            bien.has_climatisation, bien.has_garage, bien.has_jardin,
            bien.has_parking, bien.has_piscine, bien.has_terrasse,
            bien.etat, round(pred), fourchette_basse, fourchette_haute, confiance,
        ))

    return {
        "prediction_id":       pred_id,
        "prix_predit_tnd":     round(pred),
        "prix_fourchette_basse": fourchette_basse,
        "prix_fourchette_haute": fourchette_haute,
        "confiance":           confiance,
        "message":             message,
    }


@app.post("/confirmer-vente")
def confirmer_vente(conf: ConfirmationVente):
    global _retrain_running
    with get_db() as con:
        cur = con.execute(
            "UPDATE predictions_log SET prix_reel_tnd=? WHERE id=?",
            (conf.prix_reel_tnd, conf.prediction_id),
        )
        if cur.rowcount == 0:
            raise HTTPException(status_code=404, detail="Prédiction introuvable.")
        n_nouvelles = con.execute(
            "SELECT COUNT(*) FROM predictions_log WHERE prix_reel_tnd IS NOT NULL"
        ).fetchone()[0]

    retrain_declenche = False
    if n_nouvelles >= SEUIL_RETRAIN and not _retrain_running:
        _retrain_running = True
        threading.Thread(target=_run_retrain, daemon=True).start()
        retrain_declenche = True

    return {
        "status": "ok",
        "message": "Prix réel enregistré. Ce bien alimentera le prochain réentraînement.",
        "prediction_id": conf.prediction_id,
        "retrain_declenche": retrain_declenche,
    }


@app.get("/metriques")
def metriques():
    with get_db() as con:
        n_total  = con.execute("SELECT COUNT(*) FROM predictions_log").fetchone()[0]
        df_label = pd.read_sql(
            "SELECT prix_reel_tnd, prix_predit_tnd, nature_bien FROM predictions_log WHERE prix_reel_tnd IS NOT NULL",
            con,
        )

    live: dict = {}
    if len(df_label) >= 10:
        err = np.abs(df_label["prix_reel_tnd"] - df_label["prix_predit_tnd"]) / df_label["prix_reel_tnd"]
        by_type: dict = {}
        for nature, grp in df_label.groupby("nature_bien"):
            e = np.abs(grp["prix_reel_tnd"] - grp["prix_predit_tnd"]) / grp["prix_reel_tnd"]
            by_type[str(nature)] = {
                "n":     int(len(grp)),
                "mape":  round(float(e.mean() * 100), 1),
                "acc20": round(float((e <= 0.20).mean() * 100), 1),
            }
        live = {
            "n_transactions_confirmees": len(df_label),
            "mape_global":  round(float(err.mean() * 100), 1),
            "acc20_global": round(float((err <= 0.20).mean() * 100), 1),
            "par_type": by_type,
        }

    return {
        "n_total_predictions":     n_total,
        "metriques_entrainement":  bundle.get("test_metrics", {}),
        "cv_mean":                 bundle.get("cv_mean_metrics", {}),
        "metriques_live":          live,
    }


if __name__ == "__main__":
    uvicorn.run("api:app", host="0.0.0.0", port=8003, reload=True)
