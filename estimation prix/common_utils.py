
from __future__ import annotations
import re
from pathlib import Path
from typing import Optional, Iterable

import numpy as np
import pandas as pd


def slug_text(s: str) -> str:
    if pd.isna(s):
        return ""
    s = str(s).lower().strip()
    s = s.replace("é", "e").replace("è", "e").replace("ê", "e").replace("à", "a").replace("â", "a")
    s = s.replace("î", "i").replace("ï", "i").replace("ô", "o").replace("ù", "u").replace("û", "u")
    s = re.sub(r"[^a-z0-9]+", " ", s)
    return re.sub(r"\s+", " ", s).strip()


def normalize_zone_columns(df: pd.DataFrame) -> pd.DataFrame:
    df = df.copy()
    for col in ["delegation", "gouvernorat"]:
        if col in df.columns:
            df[col] = df[col].astype(str).str.strip()
            df[f"{col}_norm"] = df[col].map(slug_text)
        else:
            df[col] = ""
            df[f"{col}_norm"] = ""
    return df


def add_prix_m2(df: pd.DataFrame) -> pd.DataFrame:
    df = df.copy()
    df["prix_tnd"] = pd.to_numeric(df["prix_tnd"], errors="coerce")
    df["surface_m2"] = pd.to_numeric(df["surface_m2"], errors="coerce")
    df["prix_m2"] = np.where(
        (df["prix_tnd"].notna()) & (df["surface_m2"].notna()) & (df["surface_m2"] > 0),
        df["prix_tnd"] / df["surface_m2"],
        np.nan,
    )
    return df


def safe_numeric(df: pd.DataFrame, cols: Iterable[str], fill_value: float = 0.0) -> pd.DataFrame:
    df = df.copy()
    for col in cols:
        if col in df.columns:
            df[col] = pd.to_numeric(df[col], errors="coerce").fillna(fill_value)
        else:
            df[col] = fill_value
    return df


ETAT_MAPPING = {"bon_etat": 2, "etat_moyen": 1, "retape": 1, "a_renover": 0}

# Normalisation des valeurs brutes vers les catégories standard
ETAT_NORMALISATION = {
    "nouveau":      "bon_etat",
    "bon etat":     "bon_etat",
    "bon_etat":     "bon_etat",
    "retape":       "retape",
    "retapé":       "retape",
    "etat moyen":   "etat_moyen",
    "etat_moyen":   "etat_moyen",
    "moyen":        "etat_moyen",
    "a renover":    "a_renover",
    "a_renover":    "a_renover",
}


def normalize_etat(value) -> str:
    if pd.isna(value):
        return "etat_moyen"
    return ETAT_NORMALISATION.get(slug_text(str(value)), "etat_moyen")




from sklearn.base import BaseEstimator, TransformerMixin


class CatImputer(BaseEstimator, TransformerMixin):
    """Remplace les NaN catégoriels par 'unknown' — requis par CatBoost."""
    def fit(self, X, y=None):
        return self
    def transform(self, X):
        return np.where(pd.isnull(X), "unknown", np.array(X, dtype=object)).astype(str)


def add_engineered_features(df: pd.DataFrame) -> pd.DataFrame:
    """Crée de nouvelles features à partir des colonnes existantes."""
    out = df.copy()

    # Valeur estimée du terrain dans la zone (prix ref terrain × surface)
    out["valeur_terrain_estimee"] = out["prix_m2_terrain_ref"] * out["surface_m2"]

    # Valeur estimée du bien selon la zone (prix ref bien × surface)
    out["valeur_bien_ref_zone"] = out["prix_m2_bien_ref"] * out["surface_m2"]

    # Interaction équipements × surface (grand bien bien équipé)
    out["equip_x_surface"] = out["standing_score"] * out["surface_m2"]

    # Densité des équipements par m²
    out["equip_par_m2"] = out["standing_score"] / out["surface_m2"].replace(0, np.nan)

    # Densité de données dans la zone (log pour réduire l'effet des grandes zones)
    out["log_nb_biens_zone"] = np.log1p(out["nb_biens_zone_type"].fillna(0))

    # Log du markup bâti/terrain (plus stable que le ratio brut)
    out["log_markup"] = np.log1p(out["markup_bati_vs_terrain"].clip(lower=0))

    return out


def infer_simplified_nature(value: str) -> str:
    s = slug_text(value)
    if any(x in s for x in ["appartement", "appart"]):
        return "appartement"
    if any(x in s for x in ["villa", "maison"]):
        return "villa_maison"
    if any(x in s for x in ["duplex", "triplex", "quadruplex"]):
        return "duplex_triplex"
    if "immeuble" in s:
        return "immeuble"
    if any(x in s for x in ["local", "commerce", "commercial", "magasin"]):
        return "local_commercial"
    if "bureau" in s:
        return "bureau"
    if "studio" in s:
        return "studio"
    if "ferme" in s:
        return "ferme"
    return "autre"
