"""
Construit la table de calibration par segment à partir des prédictions test.
Pour chaque type de bien : percentiles du ratio (prix_reel / prix_predit),
erreur médiane, niveau de confiance.
Produit : data/calibration_segments.json
"""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np
import pandas as pd

ROOT = Path(__file__).resolve().parent

CONFIANCE_SEUILS = [
    (20, "haute"),
    (35, "moyenne"),
]


def confiance_label(median_err_pct: float) -> str:
    for seuil, label in CONFIANCE_SEUILS:
        if median_err_pct < seuil:
            return label
    return "faible"


MESSAGES = {
    "haute":   "Estimation fiable — le modèle couvre bien ce type de bien dans cette zone.",
    "moyenne": "Estimation correcte — fourchette un peu plus large, zone modérément couverte.",
    "faible":  "Estimation approximative — données insuffisantes pour ce type ou cette zone.",
}


def segment_stats(grp: pd.DataFrame) -> dict:
    ratio = grp["prix_reel"] / grp["prix_predit"]
    abs_err = np.abs(grp["prix_reel"] - grp["prix_predit"]) / grp["prix_reel"] * 100
    return {
        "n": int(len(grp)),
        "ratio_p10": float(np.percentile(ratio, 10)),
        "ratio_p25": float(np.percentile(ratio, 25)),
        "ratio_p50": float(np.percentile(ratio, 50)),
        "ratio_p75": float(np.percentile(ratio, 75)),
        "ratio_p90": float(np.percentile(ratio, 90)),
        "median_abs_err_pct": float(np.median(abs_err)),
        "mean_abs_err_pct":   float(np.mean(abs_err)),
    }


def main() -> None:
    preds_path = ROOT / "data" / "predictions_test.xlsx"
    df = pd.read_excel(preds_path)
    df = df[(df["prix_reel"] > 0) & (df["prix_predit"] > 0)].copy()

    segments: dict[str, dict] = {}
    for nature, grp in df.groupby("nature_bien_simple"):
        if len(grp) < 5:
            continue
        stats = segment_stats(grp)
        stats["confiance"] = confiance_label(stats["median_abs_err_pct"])
        stats["message"] = MESSAGES[stats["confiance"]]
        segments[str(nature)] = stats

    global_stats = segment_stats(df)
    global_stats["confiance"] = "faible"
    global_stats["message"] = MESSAGES["faible"]

    calib = {
        "segments": segments,
        "global": global_stats,
        "seuil_prix_bas_tnd": 100_000,
        "message_prix_bas": (
            "Prix estimé inférieur à 100 000 TND — "
            "le modèle est peu fiable dans cette zone de prix."
        ),
    }

    out_path = ROOT / "data" / "calibration_segments.json"
    out_path.write_text(json.dumps(calib, indent=2, ensure_ascii=False), encoding="utf-8")

    print("=== Calibration par segment ===")
    print(f"{'Type':<22} {'N':>5}  {'Err médiane':>12}  {'Ratio P25–P75':>16}  Confiance")
    print("-" * 72)
    for name, s in sorted(segments.items(), key=lambda x: x[1]["median_abs_err_pct"]):
        rng = f"{s['ratio_p25']:.2f} – {s['ratio_p75']:.2f}"
        print(f"{name:<22} {s['n']:>5}  {s['median_abs_err_pct']:>11.1f}%  {rng:>16}  {s['confiance']}")
    print(f"\nSauvegardé : {out_path}")


if __name__ == "__main__":
    main()
