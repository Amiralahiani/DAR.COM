"""
Réentraîne le modèle sur les nouvelles ventes confirmées depuis le site.
Lance ce script périodiquement (chaque semaine ou mois) via une tâche planifiée.

Logique :
  1. Charge les prédictions confirmées (prix_reel_tnd renseigné) depuis SQLite
  2. Fusionne avec la base d'entraînement existante
  3. Réentraîne le modèle
  4. Remplace model_prix_total.pkl + calibration SEULEMENT si c'est mieux
  5. Garde un backup de l'ancien modèle

Usage :
  python retrain.py
  python retrain.py --min-nouvelles 30   (seuil minimum de nouvelles lignes)
"""
from __future__ import annotations

import argparse
import shutil
import sqlite3
import subprocess
import sys
from datetime import datetime
from pathlib import Path

import joblib
import numpy as np
import pandas as pd

from common_utils import normalize_zone_columns, infer_simplified_nature, normalize_etat

ROOT       = Path(__file__).resolve().parent
DB_PATH    = ROOT / "data" / "predictions_log.db"
ML_DATA    = ROOT / "data" / "biens_dataset_ml.xlsx"
MODEL_PATH = ROOT / "model" / "model_prix_total.pkl"
CALIB_PATH = ROOT / "data" / "calibration_segments.json"
REF_DELEG  = ROOT / "refs_terrain" / "ref_terrain_delegation.xlsx"
REF_GOUV   = ROOT / "refs_terrain" / "ref_terrain_gouvernorat.xlsx"
REF_GLOBAL = ROOT / "refs_terrain" / "ref_terrain_global.xlsx"

NON_SUPP = {"bureau", "local_commercial", "autre"}

# ---------------------------------------------------------------------------
# Étape 1 — Charger les nouvelles données confirmées
# ---------------------------------------------------------------------------

def load_confirmed(min_rows: int) -> pd.DataFrame:
    if not DB_PATH.exists():
        print("  [!] Pas de base de données predictions_log.db trouvée.")
        return pd.DataFrame()

    with sqlite3.connect(DB_PATH) as con:
        df = pd.read_sql(
            "SELECT * FROM predictions_log WHERE prix_reel_tnd IS NOT NULL AND prix_reel_tnd > 0",
            con,
        )

    print(f"  Nouvelles ventes confirmées : {len(df)}")
    if len(df) < min_rows:
        print(f"  Seuil minimum ({min_rows}) non atteint — réentraînement annulé.")
        return pd.DataFrame()

    # Mettre au format biens_dataset_ml
    df = df.rename(columns={"prix_reel_tnd": "prix_tnd"})
    df["prix_m2"] = np.where(df["surface_m2"] > 0, df["prix_tnd"] / df["surface_m2"], np.nan)
    return df


# ---------------------------------------------------------------------------
# Étape 2 — Préparer les features ML pour les nouvelles lignes
# ---------------------------------------------------------------------------

def prepare_new_rows(df_new: pd.DataFrame) -> pd.DataFrame:
    df = df_new.copy()
    df = normalize_zone_columns(df)
    df["nature_bien_simple"] = df["nature_bien"].map(infer_simplified_nature)

    # Exclure types non supportés
    df = df[~df["nature_bien_simple"].isin(NON_SUPP)].copy()

    ref_deleg  = pd.read_excel(REF_DELEG)
    ref_gouv   = pd.read_excel(REF_GOUV)
    ref_global = pd.read_excel(REF_GLOBAL)

    df = df.merge(
        ref_deleg[["gouvernorat_norm", "delegation_norm", "nb_terrains", "median_prix_m2_terrain"]],
        on=["gouvernorat_norm", "delegation_norm"], how="left",
    ).merge(
        ref_gouv[["gouvernorat_norm", "nb_terrains_gouv", "median_prix_m2_terrain_gouv"]],
        on="gouvernorat_norm", how="left",
    )

    global_med = float(ref_global.iloc[0]["median_prix_m2_terrain_global"])
    df["prix_m2_terrain_ref"]    = df["median_prix_m2_terrain"]
    df["niveau_fallback_terrain"] = "delegation"
    m = df["prix_m2_terrain_ref"].isna() & df["median_prix_m2_terrain_gouv"].notna()
    df.loc[m, "prix_m2_terrain_ref"]    = df.loc[m, "median_prix_m2_terrain_gouv"]
    df.loc[m, "niveau_fallback_terrain"] = "gouvernorat"
    m = df["prix_m2_terrain_ref"].isna()
    df.loc[m, "prix_m2_terrain_ref"]    = global_med
    df.loc[m, "niveau_fallback_terrain"] = "global"

    df["nb_chambres"] = pd.to_numeric(df.get("nb_chambres"), errors="coerce")
    df["nb_chambres_missing"] = df["nb_chambres"].isna().astype(int)
    df["nb_chambres"] = df["nb_chambres"].fillna(df["nb_chambres"].median())

    df["log_surface"]     = np.log1p(df["surface_m2"])
    df["surface_squared"] = df["surface_m2"] ** 2
    df["surface_per_room"] = np.where(df["nb_chambres"] > 0, df["surface_m2"] / df["nb_chambres"], df["surface_m2"])
    df["rooms_density"]   = np.where(df["surface_m2"] > 0, df["nb_chambres"] / df["surface_m2"], 0)

    equip_cols = [
        "has_ascenseur", "has_balcon", "has_chauffage_central", "has_climatisation",
        "has_garage", "has_jardin", "has_parking", "has_piscine", "has_terrasse",
    ]
    for col in equip_cols:
        df[col] = pd.to_numeric(df.get(col, 0), errors="coerce").fillna(0).clip(0, 1)
    df["standing_score"] = df[equip_cols].sum(axis=1)

    # État du bien — catégorielle normalisée
    etat_col = "etat" if "etat" in df.columns else None
    df["etat_estime"] = df[etat_col].map(normalize_etat) if etat_col else "etat_moyen"

    # Features bien_ref : seront recalculées proprement par 04_train_price_model.py
    for col in ["prix_m2_bien_ref", "markup_bati_vs_terrain", "nb_biens_zone_type",
                "niveau_fallback_bien_ref", "ratio_prix_m2_vs_terrain_ref",
                "ecart_pct_vs_terrain_ref", "ratio_prix_m2_vs_bien_ref", "ecart_pct_vs_bien_ref"]:
        if col not in df.columns:
            df[col] = np.nan

    return df


# ---------------------------------------------------------------------------
# Étape 3 — Fusionner avec la base existante
# ---------------------------------------------------------------------------

def merge_datasets(df_new: pd.DataFrame, df_existing: pd.DataFrame) -> pd.DataFrame:
    common_cols = [c for c in df_existing.columns if c in df_new.columns]
    combined = pd.concat([df_existing, df_new[common_cols]], ignore_index=True)

    n_before = len(combined)
    combined = combined.drop_duplicates(
        subset=["gouvernorat", "delegation", "nature_bien_simple", "surface_m2", "prix_tnd"],
        keep="first",
    ).reset_index(drop=True)

    print(f"  Fusion : {len(df_existing)} existantes + {len(df_new)} nouvelles = {n_before} → {len(combined)} après dédup")
    return combined


# ---------------------------------------------------------------------------
# Étape 4 — Réentraîner via le script existant (subprocess)
# ---------------------------------------------------------------------------

def retrain_model(dataset_path: Path) -> tuple[Path, dict]:
    tmp_model  = ROOT / "model" / "model_prix_total_candidat.pkl"
    tmp_preds  = ROOT / "data" / "predictions_test_candidat.xlsx"

    result = subprocess.run(
        [
            sys.executable, str(ROOT / "04_train_price_model.py"),
            "--dataset",      str(dataset_path),
            "--output-model", str(tmp_model),
            "--output-preds", str(tmp_preds),
            "--cv-folds",     "5",
        ],
        capture_output=True, text=True, cwd=str(ROOT),
    )

    if result.returncode != 0:
        raise RuntimeError(f"Erreur d'entraînement :\n{result.stderr}")

    print(result.stdout)
    new_bundle  = joblib.load(tmp_model)
    new_metrics = new_bundle.get("test_metrics", {})
    return tmp_model, tmp_preds, new_metrics


# ---------------------------------------------------------------------------
# Étape 5 — Comparer et remplacer si meilleur
# ---------------------------------------------------------------------------

def maybe_replace(tmp_model: Path, tmp_preds: Path, new_metrics: dict, combined: pd.DataFrame) -> None:
    old_bundle  = joblib.load(MODEL_PATH)
    old_metrics = old_bundle.get("test_metrics", {})

    old_r2  = old_metrics.get("r2",    0.0)
    new_r2  = new_metrics.get("r2",    0.0)
    old_acc = old_metrics.get("acc20", 0.0)
    new_acc = new_metrics.get("acc20", 0.0)

    print(f"\n  Ancien modèle : R²={old_r2:.3f}  acc20={old_acc:.1f}%")
    print(f"  Nouveau modèle: R²={new_r2:.3f}  acc20={new_acc:.1f}%")

    improved = (new_r2 > old_r2 + 0.005) or (new_acc > old_acc + 0.5)

    if improved:
        # Backup
        ts = datetime.now().strftime("%Y%m%d_%H%M")
        backup = MODEL_PATH.parent / f"model_prix_total_backup_{ts}.pkl"
        shutil.copy(MODEL_PATH, backup)
        print(f"\n  ✓ Meilleur → modèle remplacé  (backup : {backup.name})")

        # Remplacer modèle
        shutil.move(tmp_model, MODEL_PATH)

        # Mettre à jour le dataset ML
        combined.to_excel(ML_DATA, index=False)

        # Reconstruire la calibration
        shutil.copy(tmp_preds, ROOT / "data" / "predictions_test.xlsx")
        subprocess.run(
            [sys.executable, str(ROOT / "build_calibration.py")],
            cwd=str(ROOT),
        )
        print("  ✓ Calibration mise à jour.")
    else:
        print("\n  ✗ Pas d'amélioration significative — ancien modèle conservé.")
        tmp_model.unlink(missing_ok=True)

    tmp_preds.unlink(missing_ok=True)


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main() -> None:
    parser = argparse.ArgumentParser(description="Réentraîne le modèle sur les nouvelles ventes confirmées.")
    parser.add_argument("--min-nouvelles", type=int, default=50,
                        help="Nombre minimum de nouvelles ventes confirmées pour lancer le réentraînement (défaut: 50).")
    args = parser.parse_args()

    print(f"{'='*60}")
    print(f"  Réentraînement  —  {datetime.now():%Y-%m-%d %H:%M}")
    print(f"{'='*60}")

    # 1. Nouvelles données
    df_new = load_confirmed(args.min_nouvelles)
    if df_new.empty:
        return

    # 2. Préparer features
    df_new_prep = prepare_new_rows(df_new)
    print(f"  Après filtrage  : {len(df_new_prep)} lignes valides")

    # 3. Fusionner
    df_existing = pd.read_excel(ML_DATA)
    combined    = merge_datasets(df_new_prep, df_existing)

    # 4. Sauvegarder dataset temporaire et réentraîner
    tmp_dataset = ROOT / "data" / "biens_dataset_ml_tmp.xlsx"
    combined.to_excel(tmp_dataset, index=False)

    print("\n  Lancement de l'entraînement...")
    try:
        tmp_model, tmp_preds, new_metrics = retrain_model(tmp_dataset)
    finally:
        tmp_dataset.unlink(missing_ok=True)

    # 5. Remplacer si meilleur
    maybe_replace(tmp_model, tmp_preds, new_metrics, combined)

    print(f"\n{'='*60}")
    print(f"  Terminé  —  {datetime.now():%Y-%m-%d %H:%M}")
    print(f"{'='*60}")


if __name__ == "__main__":
    main()
