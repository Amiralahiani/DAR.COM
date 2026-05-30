"""
Dar.com — Microservice Analyse Image v1.3
=========================================
Rôle unique : valider les photos de biens immobiliers.
  POST /analyze            → CLIP + NudeNet + pHash → decision approved/review/rejected
  GET  /health             → état des modèles
  GET  /                   → interface de test HTML

Description generation → gérée côté C# (AnnonceController.GenerateDescription)
"""

import io
import json
import logging
import os
import tempfile
import traceback
from typing import Optional

import cv2
import imagehash
import numpy as np
import torch
from fastapi import FastAPI, File, Form, UploadFile
from fastapi.responses import HTMLResponse
from PIL import Image
from transformers import CLIPModel, CLIPProcessor
from nudenet import NudeDetector

try:
    __import__("pillow_avif")
except Exception:
    pass

# ── Chargement du .env depuis le dossier de ce script ──
def _load_env() -> None:
    env_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), ".env")
    if not os.path.exists(env_path):
        return
    with open(env_path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            key, _, val = line.partition("=")
            os.environ[key.strip()] = val.strip()

_load_env()

# ── Logging ──
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# ── App ──
app = FastAPI(title="Dar.com — Microservice Analyse Image")
APP_VERSION = "1.3-local-only"
BLUR_THRESHOLD = 220.0

# ── Chargement des modèles ──
logger.info("Chargement CLIP et NudeNet...")

clip_processor = None
clip_model = None
try:
    clip_processor = CLIPProcessor.from_pretrained("openai/clip-vit-base-patch32")
    clip_model = CLIPModel.from_pretrained("openai/clip-vit-base-patch32")
    logger.info("CLIP chargé.")
except Exception as e:
    logger.error("Erreur CLIP: %s", e)

nude_detector = None
try:
    nude_detector = NudeDetector()
    logger.info("NudeNet chargé.")
except Exception as e:
    logger.error("Erreur NudeNet: %s", e)

# ── Catégories CLIP ──
PROPERTY_CATEGORIES = {
    "salon":        "a photo of a living room",
    "chambre":      "a photo of a bedroom",
    "cuisine":      "a photo of a kitchen",
    "salle_de_bain":"a photo of a bathroom",
    "facade":       "a photo of a house exterior or building facade",
    "jardin":       "a photo of a garden or backyard",
    "terrasse":     "a photo of a terrace or balcony",
    "garage":       "a photo of a garage",
}

REJECTION_CATEGORIES = {
    "selfie":            "a selfie photo of a person",
    "document":          "a photo of a document or text",
    "voiture":           "a photo of a car",
    "animal":            "a photo of an animal or pet",
    "nourriture":        "a photo of food",
    "paysage_generique": "a photo of a nature landscape with no house",
}

ALL_LABELS = list(PROPERTY_CATEGORIES.values()) + list(REJECTION_CATEGORIES.values())


# ═══════════════════════════════ ENDPOINTS ════════════════════════════════

@app.get("/health")
def health_check():
    return {
        "status": "ok",
        "version": APP_VERSION,
        "models": {
            "clip_loaded":   clip_model is not None and clip_processor is not None,
            "nudenet_loaded": nude_detector is not None,
        },
    }


@app.get("/", response_class=HTMLResponse)
def index():
    return """<!doctype html>
<html lang="fr">
<head>
  <meta charset="utf-8"/>
  <title>Dar.com — Test analyse image</title>
  <style>
    body{font-family:Arial,sans-serif;background:#0f1115;color:#f3f4f6;padding:24px}
    .box{background:#171a21;border:1px solid #2b303b;border-radius:12px;padding:16px;margin-bottom:16px}
    input[type=file]{color:#f3f4f6}
    button{background:#2563eb;color:#fff;border:0;border-radius:8px;padding:8px 16px;cursor:pointer;font-weight:700}
    pre{background:#0b1020;color:#cbd5e1;border-radius:8px;padding:12px;overflow:auto}
    .ok{color:#86efac} .warn{color:#fbbf24} .err{color:#f87171}
  </style>
</head>
<body>
  <h2>Dar.com — Microservice Analyse Image v1.3</h2>
  <div class="box">
    <b>Test /analyze</b><br/><br/>
    <input type="file" id="img" accept="image/*"/>
    <button onclick="analyze()">Analyser</button>
    <pre id="out">{}</pre>
  </div>
  <script>
    async function analyze(){
      const f=document.getElementById('img').files[0];
      if(!f){alert('Choisissez une image');return;}
      const fd=new FormData(); fd.append('image',f);
      const r=await fetch('/analyze',{method:'POST',body:fd});
      const d=await r.json();
      document.getElementById('out').textContent=JSON.stringify(d,null,2);
    }
  </script>
</body>
</html>"""


@app.post("/generate-description")
async def generate_description_stub():
    """
    Endpoint désactivé.
    La génération de description est gérée côté C# (AnnonceController) à partir des données Étape 1.

    ── PLACEHOLDER pour ton propre modèle ──────────────────────────────────
    Quand ton modèle de génération sera prêt, décommente et adapte :

    @app.post("/generate-description")
    async def generate_description(data: BienFeatures):
        description = ton_modele.predict(data)
        return {"description": description}
    ────────────────────────────────────────────────────────────────────────
    """
    return {
        "error": "Endpoint désactivé. La description est générée côté C#.",
        "info": "Voir AnnonceController.GenerateDescription dans le projet ASP.NET."
    }


@app.post("/analyze")
async def analyze_image(
    image: UploadFile = File(...),
    known_hashes: Optional[str] = Form(None),
):
    if not image.content_type or not image.content_type.startswith("image/"):
        return {"decision": "rejected", "rejection_reasons": ["Le fichier fourni n'est pas une image."]}

    image_bytes = await image.read()
    if not image_bytes:
        return {"decision": "rejected", "rejection_reasons": ["Le fichier image est vide."]}

    quality_res  = {"score": 0.0, "is_blurry": False, "is_too_dark": False, "is_too_bright": False, "blur_score": 0.0, "brightness": 0.0}
    classif_res  = {"is_property": False, "category": "unknown", "confidence": 0.0, "all_scores": {}}
    mod_res      = {"is_safe": True, "reason": None}
    dup_res      = {"is_duplicate": False, "hash": None}
    rejection_reasons = []

    # ── Lecture image ──
    try:
        pil_img    = Image.open(io.BytesIO(image_bytes)).convert("RGB")
        cv_img_np  = np.frombuffer(image_bytes, np.uint8)
        cv_img     = cv2.imdecode(cv_img_np, cv2.IMREAD_COLOR)
        if cv_img is None:
            cv_img = cv2.cvtColor(np.array(pil_img), cv2.COLOR_RGB2BGR)
    except Exception:
        return {"decision": "rejected", "rejection_reasons": ["Fichier image invalide ou corrompu."]}

    # ── 1. Qualité (OpenCV) ──
    try:
        gray        = cv2.cvtColor(cv_img, cv2.COLOR_BGR2GRAY)
        blur_score  = cv2.Laplacian(gray, cv2.CV_64F).var()
        brightness  = float(np.mean(gray))
        is_blurry   = blur_score < BLUR_THRESHOLD
        is_too_dark = brightness < 50
        is_too_bright = brightness > 220
        score = 10.0 - (4 if is_blurry else 0) - (3 if is_too_dark or is_too_bright else 0)
        quality_res.update({
            "score": round(max(0.0, score), 1),
            "is_blurry": bool(is_blurry),
            "is_too_dark": bool(is_too_dark),
            "is_too_bright": bool(is_too_bright),
            "blur_score": round(float(blur_score), 2),
            "brightness": round(brightness, 2),
        })
    except Exception:
        logger.error("Qualité: %s", traceback.format_exc())
        rejection_reasons.append("Erreur lors de l'analyse de qualité.")

    # ── 2. Modération (NudeNet) ──
    tmp_path = None
    try:
        if nude_detector is None:
            raise RuntimeError("NudeNet indisponible")
        with tempfile.NamedTemporaryFile(delete=False, suffix=".jpg") as tmp:
            pil_img.save(tmp, format="JPEG")
            tmp_path = tmp.name
        detections = nude_detector.detect(tmp_path)
        unsafe = {'EXPOSED_ANUS','EXPOSED_BUTTOCKS','EXPOSED_BREAST_F',
                  'EXPOSED_GENITALIA_F','EXPOSED_GENITALIA_M','EXPOSED_BREAST_M'}
        for det in detections:
            if det["class"] in unsafe and det["score"] > 0.5:
                mod_res.update({"is_safe": False, "reason": f"Contenu adulte détecté ({det['class']})"})
                break
    except Exception:
        logger.error("Modération: %s", traceback.format_exc())
    finally:
        if tmp_path and os.path.exists(tmp_path):
            os.remove(tmp_path)

    # ── 3. Classification (CLIP) — uniquement si safe ──
    if mod_res["is_safe"]:
        try:
            if clip_model is None or clip_processor is None:
                raise RuntimeError("CLIP indisponible")
            inputs = clip_processor(text=ALL_LABELS, images=pil_img, return_tensors="pt", padding=True)
            with torch.no_grad():
                outputs = clip_model(**inputs)
            probs = outputs.logits_per_image.softmax(dim=1).detach().numpy()[0]
            scores_en = {label: float(p) for label, p in zip(ALL_LABELS, probs)}

            scores_fr = {}
            for fr, en in PROPERTY_CATEGORIES.items():
                scores_fr[fr] = scores_en[en]
            for fr, en in REJECTION_CATEGORIES.items():
                scores_fr[fr] = scores_en[en]

            best      = max(scores_fr, key=scores_fr.get)
            conf      = scores_fr[best]
            is_prop   = best in PROPERTY_CATEGORIES
            classif_res.update({
                "is_property": is_prop,
                "category":    best if is_prop else "hors-sujet",
                "confidence":  round(conf, 3),
                "all_scores":  {k: round(v, 3) for k, v in scores_fr.items()},
            })
        except Exception:
            logger.error("Classification: %s", traceback.format_exc())
            rejection_reasons.append("Erreur lors de la classification.")

    # ── 4. Doublons (pHash) ──
    try:
        img_hash = imagehash.phash(pil_img)
        is_dup   = False
        if known_hashes:
            for kh in json.loads(known_hashes):
                if img_hash - imagehash.hex_to_hash(kh) <= 5:
                    is_dup = True
                    break
        dup_res.update({"is_duplicate": is_dup, "hash": str(img_hash)})
    except Exception:
        logger.error("pHash: %s", traceback.format_exc())

    # ── Décision ──
    decision = "approved"

    if not mod_res["is_safe"]:
        decision = "rejected"
        rejection_reasons.append(mod_res["reason"])

    clip_ok = clip_model is not None and clip_processor is not None
    if not classif_res["is_property"] and clip_ok:
        decision = "rejected"
        rejection_reasons.append(
            f"Image hors-sujet: probable {classif_res['category']} ({classif_res['confidence']})"
        )
    elif not clip_ok and decision != "rejected":
        decision = "review"
        rejection_reasons.append("CLIP non chargé — revue manuelle.")

    if dup_res["is_duplicate"]:
        decision = "rejected"
        rejection_reasons.append("Image en doublon.")

    if decision != "rejected":
        if quality_res["is_blurry"]:
            decision = "review"
            rejection_reasons.append("Image floue — revue manuelle.")
        elif quality_res["score"] < 5:
            decision = "review"
            rejection_reasons.append("Qualité insuffisante.")
        elif classif_res["confidence"] < 0.6:
            decision = "review"
            rejection_reasons.append("Confiance CLIP insuffisante.")

    return {
        "quality":          quality_res,
        "classification":   classif_res,
        "moderation":       mod_res,
        "duplicate":        dup_res,
        "decision":         decision,
        "rejection_reasons": rejection_reasons,
    }
