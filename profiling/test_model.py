"""
Test du modèle DAR.COM ML Engine
Lancer d'abord le serveur : python main.py
Puis dans un autre terminal : python test_model.py
"""

import requests

BASE_URL = "http://localhost:8000"


def test_health():
    r = requests.get(f"{BASE_URL}/api/ml/health")
    print("=== HEALTH ===")
    print(r.json())
    print()


def test_metrics():
    r = requests.get(f"{BASE_URL}/api/ml/metrics")
    data = r.json()
    print("=== MÉTRIQUES DU MODÈLE ===")
    m = data.get("metrics", {})

    clf = m.get("classifier", {})
    print(f"  Lead Scorer   → CV AUC : {clf.get('cv_auc_mean')} ± {clf.get('cv_auc_std')}")
    print(f"                  Hold-out AUC : {clf.get('holdout_auc')}  F1 : {clf.get('holdout_f1')}")

    reg = m.get("regressor", {})
    print(f"  Budget Pred   → CV R²  : {reg.get('cv_r2_mean')} ± {reg.get('cv_r2_std')}")
    print(f"                  Hold-out R² : {reg.get('holdout_r2')}  MAE : {reg.get('holdout_mae')} DT")

    clust = m.get("clustering", {})
    print(f"  Clustering    → Silhouette : {clust.get('silhouette')}  k={data.get('n_clusters')} clusters")

    print()
    print("  Clusters détectés :")
    for cid, prof in (data.get("cluster_profiles") or {}).items():
        print(f"    [{cid}] {prof['label']:40s} — {prof['size']} utilisateurs")
    print()


def test_profile(label, user_data):
    r = requests.post(f"{BASE_URL}/api/ml/update_profile", json=user_data)
    result = r.json()
    print(f"=== TEST : {label} ===")
    print(f"  Budget estimé  : {result['predicted_budget']:,.0f} DT")
    print(f"  Lead score     : {result['lead_score_pct']} %")
    print(f"  Intention      : {result['predicted_intent']}")
    print(f"  Persona        : {result['persona_segment']}")
    print()


if __name__ == "__main__":
    test_health()
    test_metrics()

    # ── Cas de test représentatifs ─────────────────────────────

    test_profile("Client curieux / peu engagé", {
        "user_id": 1,
        "avg_price_viewed": 280000,
        "property_views_count": 30,
        "favorites_count": 1,
        "contact_agent_clicks": 0,
        "avg_surface_viewed": 75,
        "session_duration_mins": 5,
    })

    test_profile("Locataire actif", {
        "user_id": 2,
        "avg_price_viewed": 200000,
        "property_views_count": 18,
        "favorites_count": 6,
        "contact_agent_clicks": 1,
        "avg_surface_viewed": 90,
        "session_duration_mins": 18,
    })

    test_profile("Acheteur sérieux", {
        "user_id": 3,
        "avg_price_viewed": 650000,
        "property_views_count": 10,
        "favorites_count": 6,
        "contact_agent_clicks": 2,
        "avg_surface_viewed": 160,
        "session_duration_mins": 28,
    })

    test_profile("Investisseur / grande surface", {
        "user_id": 4,
        "avg_price_viewed": 900000,
        "property_views_count": 7,
        "favorites_count": 4,
        "contact_agent_clicks": 3,
        "avg_surface_viewed": 320,
        "session_duration_mins": 22,
    })
