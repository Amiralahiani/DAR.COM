
from __future__ import annotations
import argparse
import json
from pathlib import Path
import joblib
import numpy as np
import pandas as pd
from catboost import CatBoostRegressor
from sklearn.compose import ColumnTransformer
from sklearn.impute import SimpleImputer
from sklearn.metrics import mean_absolute_error, mean_squared_error, r2_score
from sklearn.model_selection import KFold, train_test_split
from sklearn.pipeline import Pipeline

from common_utils import normalize_zone_columns, infer_simplified_nature, CatImputer

ROOT = Path(__file__).resolve().parent
BEST_PARAMS_PATH = ROOT / "data" / "best_catboost_params.json"


NUMERIC_COLS = [
    "surface_m2", "log_surface", "surface_squared",
    "nb_chambres", "nb_chambres_missing", "surface_per_room", "rooms_density",
    "standing_score",
    "prix_m2_terrain_ref",
    "prix_m2_bien_ref", "markup_bati_vs_terrain",
    "nb_terrains", "nb_terrains_gouv", "nb_biens_zone_type",
    "has_ascenseur", "has_balcon", "has_chauffage_central", "has_climatisation",
    "has_garage", "has_jardin", "has_parking", "has_piscine", "has_terrasse",
]
CAT_COLS = ["delegation", "gouvernorat", "nature_bien_simple", "niveau_fallback_terrain", "niveau_fallback_bien_ref", "etat_estime"]

LEAKY_OR_RECOMPUTED_COLS = [
    "median_prix_m2_bien_zone",
    "median_prix_m2_bien_gouv_type",
    "median_prix_m2_bien_global_type",
    "nb_biens_zone_type",
    "nb_biens_gouv_type",
    "nb_biens_global_type",
    "prix_m2_bien_ref",
    "markup_bati_vs_terrain",
    "niveau_fallback_bien_ref",
]


def evaluate(y_true: pd.Series, y_pred: np.ndarray) -> dict:
    rel = np.abs(y_true - y_pred) / y_true.replace(0, np.nan)
    return {
        "mae": float(mean_absolute_error(y_true, y_pred)),
        "rmse": float(np.sqrt(mean_squared_error(y_true, y_pred))),
        "r2": float(r2_score(y_true, y_pred)),
        "acc10": float((rel <= 0.10).mean() * 100),
        "acc20": float((rel <= 0.20).mean() * 100),
        "acc30": float((rel <= 0.30).mean() * 100),
    }


def build_pipeline(random_state: int) -> Pipeline:
    cat_indices = list(range(len(NUMERIC_COLS), len(NUMERIC_COLS) + len(CAT_COLS)))

    if BEST_PARAMS_PATH.exists():
        params = json.loads(BEST_PARAMS_PATH.read_text(encoding="utf-8"))
        params.pop("random_seed", None)
        print(f"[tuning] Paramètres Optuna chargés depuis {BEST_PARAMS_PATH.name}")
    else:
        params = dict(iterations=1000, learning_rate=0.03, depth=6, l2_leaf_reg=3)

    preprocessor = ColumnTransformer(
        transformers=[
            ("num", SimpleImputer(strategy="median"), NUMERIC_COLS),
            ("cat", CatImputer(), CAT_COLS),
        ]
    )

    model = CatBoostRegressor(
        **params,
        loss_function="RMSE",
        cat_features=cat_indices,
        random_seed=random_state,
        verbose=0,
    )

    return Pipeline([
        ("prep", preprocessor),
        ("model", model),
    ])


def build_bien_reference_tables(df_train: pd.DataFrame) -> dict[str, pd.DataFrame]:
    deleg_type = (
        df_train.groupby(["gouvernorat_norm", "delegation_norm", "nature_bien_simple"], dropna=False)["prix_m2"]
          .agg(median_prix_m2_bien_zone="median", nb_biens_zone_type="size")
          .reset_index()
    )
    gouv_type = (
        df_train.groupby(["gouvernorat_norm", "nature_bien_simple"], dropna=False)["prix_m2"]
          .agg(median_prix_m2_bien_gouv_type="median", nb_biens_gouv_type="size")
          .reset_index()
    )
    global_type = (
        df_train.groupby(["nature_bien_simple"], dropna=False)["prix_m2"]
          .agg(median_prix_m2_bien_global_type="median", nb_biens_global_type="size")
          .reset_index()
    )
    return {
        "deleg_type": deleg_type,
        "gouv_type": gouv_type,
        "global_type": global_type,
    }


def attach_bien_reference_features(df: pd.DataFrame, refs: dict[str, pd.DataFrame]) -> pd.DataFrame:
    out = df.copy()
    out = out.drop(columns=[c for c in LEAKY_OR_RECOMPUTED_COLS if c in out.columns], errors="ignore")

    out = out.merge(
        refs["deleg_type"],
        on=["gouvernorat_norm", "delegation_norm", "nature_bien_simple"],
        how="left",
    )
    out = out.merge(
        refs["gouv_type"],
        on=["gouvernorat_norm", "nature_bien_simple"],
        how="left",
    )
    out = out.merge(
        refs["global_type"],
        on=["nature_bien_simple"],
        how="left",
    )

    out["prix_m2_bien_ref"] = out["median_prix_m2_bien_zone"]
    out["niveau_fallback_bien_ref"] = "delegation_type"

    mask = out["prix_m2_bien_ref"].isna() & out["median_prix_m2_bien_gouv_type"].notna()
    out.loc[mask, "prix_m2_bien_ref"] = out.loc[mask, "median_prix_m2_bien_gouv_type"]
    out.loc[mask, "niveau_fallback_bien_ref"] = "gouvernorat_type"

    mask = out["prix_m2_bien_ref"].isna() & out["median_prix_m2_bien_global_type"].notna()
    out.loc[mask, "prix_m2_bien_ref"] = out.loc[mask, "median_prix_m2_bien_global_type"]
    out.loc[mask, "niveau_fallback_bien_ref"] = "global_type"

    out["nb_biens_zone_type"] = out["nb_biens_zone_type"].fillna(out["nb_biens_gouv_type"]).fillna(out["nb_biens_global_type"])
    out["markup_bati_vs_terrain"] = np.where(
        out["prix_m2_terrain_ref"] > 0,
        out["prix_m2_bien_ref"] / out["prix_m2_terrain_ref"],
        np.nan,
    )
    return out


def ensure_columns(df: pd.DataFrame, columns: list[str]) -> pd.DataFrame:
    out = df.copy()
    for col in columns:
        if col not in out.columns:
            out[col] = np.nan
    return out


def run_cross_validation(df: pd.DataFrame, n_splits: int, random_state: int) -> tuple[pd.DataFrame, dict, dict]:
    kf = KFold(n_splits=n_splits, shuffle=True, random_state=random_state)
    fold_metrics: list[dict] = []

    for fold, (train_idx, val_idx) in enumerate(kf.split(df), start=1):
        fold_train = df.iloc[train_idx].copy()
        fold_val = df.iloc[val_idx].copy()

        refs = build_bien_reference_tables(fold_train)
        fold_train_feat = ensure_columns(attach_bien_reference_features(fold_train, refs), NUMERIC_COLS + CAT_COLS)
        fold_val_feat   = ensure_columns(attach_bien_reference_features(fold_val,   refs), NUMERIC_COLS + CAT_COLS)

        x_train = fold_train_feat[NUMERIC_COLS + CAT_COLS].copy()
        y_train = fold_train_feat["prix_tnd"].copy()
        x_val = fold_val_feat[NUMERIC_COLS + CAT_COLS].copy()
        y_val = fold_val_feat["prix_tnd"].copy()

        pipe = build_pipeline(random_state + fold)
        pipe.fit(x_train, np.log1p(y_train))

        pred_val = np.expm1(np.clip(pipe.predict(x_val), -20, 20))
        metrics = evaluate(y_val, pred_val)
        metrics["fold"] = fold
        fold_metrics.append(metrics)

    cv_df = pd.DataFrame(fold_metrics)
    metric_cols = ["mae", "rmse", "r2", "acc10", "acc20", "acc30"]
    cv_mean = {k: float(cv_df[k].mean()) for k in metric_cols}
    cv_std = {k: float(cv_df[k].std(ddof=0)) for k in metric_cols}
    return cv_df, cv_mean, cv_std


def main():
    parser = argparse.ArgumentParser(description="Entraîne le modèle prix total sur la dataset ML.")
    parser.add_argument("--dataset", required=True)
    parser.add_argument("--output-model", default="model_prix_total.pkl")
    parser.add_argument("--output-preds", default="predictions_test.xlsx")
    parser.add_argument("--test-size", type=float, default=0.20)
    parser.add_argument("--random-state", type=int, default=42)
    parser.add_argument("--cv-folds", type=int, default=0, help="Nombre de folds KFold (0 = désactivé).")
    args = parser.parse_args()

    df = pd.read_excel(args.dataset)
    df = normalize_zone_columns(df)
    if "nature_bien_simple" not in df.columns:
        if "nature_bien" in df.columns:
            df["nature_bien_simple"] = df["nature_bien"].map(infer_simplified_nature)
        else:
            df["nature_bien_simple"] = "autre"

    df["prix_tnd"] = pd.to_numeric(df["prix_tnd"], errors="coerce")
    df["surface_m2"] = pd.to_numeric(df["surface_m2"], errors="coerce")
    if "prix_m2" not in df.columns:
        df["prix_m2"] = np.where(df["surface_m2"] > 0, df["prix_tnd"] / df["surface_m2"], np.nan)
    df["prix_m2"] = pd.to_numeric(df["prix_m2"], errors="coerce")

    df = df[(df["prix_tnd"] > 0) & (df["surface_m2"] > 0) & (df["prix_m2"] > 0)].copy()

    train_df, test_df = train_test_split(
        df, test_size=args.test_size, random_state=args.random_state
    )

    bien_refs = build_bien_reference_tables(train_df)
    train_feat = attach_bien_reference_features(train_df, bien_refs)
    test_feat  = attach_bien_reference_features(test_df,  bien_refs)

    train_feat = ensure_columns(train_feat, NUMERIC_COLS + CAT_COLS)
    test_feat  = ensure_columns(test_feat,  NUMERIC_COLS + CAT_COLS)

    x_train = train_feat[NUMERIC_COLS + CAT_COLS].copy()
    x_test = test_feat[NUMERIC_COLS + CAT_COLS].copy()
    y_train = train_feat["prix_tnd"].copy()
    y_test = test_feat["prix_tnd"].copy()

    pipe = build_pipeline(args.random_state)

    y_train_log = np.log1p(y_train)
    pipe.fit(x_train, y_train_log)

    pred_train = np.expm1(np.clip(pipe.predict(x_train), -20, 20))
    pred_test = np.expm1(np.clip(pipe.predict(x_test), -20, 20))

    train_metrics = evaluate(y_train, pred_train)
    test_metrics = evaluate(y_test, pred_test)
    cv_mean = None
    cv_std = None
    cv_fold_metrics = None
    if args.cv_folds and args.cv_folds > 1:
        cv_df, cv_mean, cv_std = run_cross_validation(df, args.cv_folds, args.random_state)
        cv_fold_metrics = cv_df.to_dict(orient="records")

    print("\nTrain metrics:", train_metrics)
    print("Test metrics :", test_metrics)
    if cv_mean is not None and cv_std is not None:
        print("CV mean      :", cv_mean)
        print("CV std       :", cv_std)

    out = test_feat.copy()
    out["prix_reel"] = y_test.values
    out["prix_predit"] = pred_test
    out["erreur_absolue"] = np.abs(out["prix_reel"] - out["prix_predit"])
    out["erreur_relative_pct"] = out["erreur_absolue"] / out["prix_reel"] * 100
    out.to_excel(args.output_preds, index=False)

    joblib.dump({
        "pipeline": pipe,
        "numeric_cols": NUMERIC_COLS,
        "cat_cols": CAT_COLS,
        "bien_ref_tables": bien_refs,
        "train_metrics": train_metrics,
        "test_metrics": test_metrics,
        "cv_mean_metrics": cv_mean,
        "cv_std_metrics": cv_std,
        "cv_fold_metrics": cv_fold_metrics,
    }, args.output_model)

    print("Modèle sauvegardé :", Path(args.output_model).resolve())
    print("Prédictions test  :", Path(args.output_preds).resolve())


if __name__ == "__main__":
    main()
