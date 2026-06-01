import os
import re
import logging
import time
import requests
import urllib.parse
from openai import OpenAI
from dotenv import load_dotenv

load_dotenv()
logger = logging.getLogger(__name__)

groq_api_key = os.getenv("GROQ_API_KEY")

client = OpenAI(
    api_key=groq_api_key,
    base_url="https://api.groq.com/openai/v1"
) if groq_api_key else None

_CONTEXT_TTL_SECONDS = 15 * 60
_last_filters_by_user = {}
_last_context_ts_by_user = {}


def get_response(message, user_id=None, shop_base_url=None):
    process_question = _is_process_question(message)

    # try to handle shop-related queries via the Shop API (DB-backed)
    try:
        filters = {}
        try:
            filters = _extract_filters(message)
        except Exception:
            filters = {}

        previous_filters = _get_recent_filters(user_id)
        looks_follow_up = _is_follow_up_filter_message(message)
        effective_filters = _merge_filters(previous_filters, filters) if (previous_filters and (filters or looks_follow_up)) else filters

        # If message looks like a shop search or filters present, query the shop API first.
        # Procedure/guidance questions must go to LLM fallback.
        should_query_shop = (
            not process_question
            and (effective_filters or _is_shop_search(message) or (looks_follow_up and previous_filters))
        )

        if should_query_shop:
            api_bases = _shop_api_base_candidates(shop_base_url)
            for api_base in api_bases:
                api_url = api_base.rstrip('/') + '/api/shop/filter'
                try:
                    resp = requests.get(api_url, params=effective_filters, timeout=5)
                    if resp.status_code != 200:
                        continue

                    data = resp.json()
                    count = data.get('count', 0)
                    items = data.get('items', [])
                    _save_filters_context(user_id, effective_filters)

                    if count == 0:
                        return "Je n'ai trouvé aucun bien disponible correspondant à ta recherche dans le shop DAR.COM."

                    if _is_most_expensive_search(message) and items:
                        items = sorted(items, key=lambda it: float(it.get('Prix') or it.get('prix') or 0), reverse=True)
                        item = items[0]
                        title = item.get('Titre') or item.get('titre') or 'Titre inconnu'
                        prix = item.get('Prix') or item.get('prix')
                        bid = item.get('Id') or item.get('id')
                        detail_link = api_base.rstrip('/') + f"/Shop/Details/{bid}" if bid is not None else ''
                        narrative = f"Le bien le plus cher disponible dans le shop DAR.COM est :\n- {title} — {prix} DT"
                        if detail_link:
                            narrative += f" — [Voir ce bien]({detail_link})"
                        return narrative

                    if _is_cheapest_search(message) and items:
                        items = sorted(items, key=lambda it: float(it.get('Prix') or it.get('prix') or 0))
                        item = items[0]
                        title = item.get('Titre') or item.get('titre') or 'Titre inconnu'
                        prix = item.get('Prix') or item.get('prix')
                        bid = item.get('Id') or item.get('id')
                        detail_link = api_base.rstrip('/') + f"/Shop/Details/{bid}" if bid is not None else ''
                        narrative = f"Le bien le moins cher disponible dans le shop DAR.COM est :\n- {title} — {prix} DT"
                        if detail_link:
                            narrative += f" — [Voir ce bien]({detail_link})"
                        return narrative

                    narrative = f"J'ai trouvé {count} bien(s) disponibles dans le shop DAR.COM."
                    if items:
                        preview = []
                        for it in items[:3]:
                            title = it.get('Titre') or it.get('titre') or 'Titre inconnu'
                            prix = it.get('Prix') or it.get('prix')
                            bid = it.get('Id') or it.get('id')
                            detail_link = api_base.rstrip('/') + f"/Shop/Details/{bid}" if bid is not None else ''
                            if prix is not None and detail_link:
                                preview.append(f"- {title} — {prix} DT — [Voir le bien]({detail_link})")
                            elif detail_link:
                                preview.append(f"- {title} — [Voir le bien]({detail_link})")
                            else:
                                preview.append(f"- {title}")
                        if preview:
                            narrative += "\nAperçu :\n" + "\n".join(preview)

                    link = _build_shop_link(api_base, effective_filters)
                    narrative += f"\n[Voir tous les biens correspondants]({link})"
                    return narrative
                except Exception:
                    # try next base candidate
                    continue

    except Exception:
        # non-blocking: continue to LLM fallback
        pass

    # fallback to LLM for general conversation (keep original behavior)
    try:
        if client is None:
            return _fallback_without_llm(message)

        response = client.chat.completions.create(
        model="llama-3.3-70b-versatile",
        messages=[
    {
        "role": "system",
        "content": """
        Tu es DAR.COM AI Assistant, l’assistant officiel de DAR.COM spécialisé dans l’immobilier en Tunisie.

MISSION

* Aider les clients à comprendre les annonces, biens, les procédures d’achat et de vente sur DAR.COM.
* Répondre uniquement aux questions liées à l’immobilier et aux services de la plateforme DAR.COM.
* Répondre toujours en français simple et compréhensible.

RÈGLES DE RÉPONSE

* Répondre de manière courte, claire et professionnelle.
* Répondre comme un conseiller DAR.COM s’adressant directement au client.
* Utiliser un ton accueillant et rassurant.
* Ne jamais inventer d’informations.
* Ne jamais inventer de lois, réglementations ou procédures.
* Ne jamais mentionner d’informations dont tu n’es pas certain.

QUESTIONS SIMPLES
Tu peux répondre aux questions concernant :

* les prix immobiliers
* les locations
* les achats immobiliers
* les ventes immobilières
* les annonces publiées sur la plateforme
* les procédures DAR.COM

QUESTIONS COMPLEXES
Si la question concerne :

* le droit immobilier
* la fiscalité
* les crédits bancaires détaillés
* les investissements complexes
* les copropriétés internationales
* les successions
* les contrats juridiques
* les litiges
* les garanties financières

Réponds uniquement :

"Cette demande nécessite une vérification spécialisée. Un agent immobilier DAR.COM a été assigné à votre dossier afin de vous fournir une réponse fiable."

Ne donne aucune explication supplémentaire.

PROCÉDURE D’ACHAT DAR.COM

Étape 1 — Visite du bien

Vous choisissez et réservez un créneau de visite en ligne.

Notre agent commercial confirme et organise la visite.

Lors de la visite, une analyse du bien est réalisée sur place.

Vous confirmez ensuite votre intérêt ou non pour le bien.

Objectif : découvrir le bien et vérifier qu’il correspond à vos besoins.

Étape 2 — Négociation et accord

Vous demandez un rendez-vous de négociation.

Un agent commercial vous accompagne durant cette étape.

Le prix et les conditions sont discutés.

Une fois l’accord trouvé, la transaction est validée.

Objectif : parvenir à un accord entre vendeur et acheteur.

Étape 3 — Finalisation et paiement

Un dossier officiel est créé.

Vous choisissez votre mode de paiement.

Le système assure le suivi des paiements.

La vente est clôturée et enregistrée.

Objectif : finaliser l’achat de manière sécurisée.

Résumé :
Visite → Négociation → Paiement → Finalisation

PROCÉDURE DE VENTE DAR.COM

Étape 1 — Dépôt de l’annonce

Vous remplissez le formulaire de publication avec les informations du bien.

Étape 2 — Vérification

Un agent commercial vérifie les informations et valide l’annonce.

Étape 3 — Publication

L’annonce est publiée sur DAR.COM et devient visible aux acheteurs.

Résumé :
Dépôt → Vérification → Publication

RÈGLE IMPORTANTE

Si le client demande une étape précise :

* Répondre uniquement avec cette étape.
* Ne pas afficher les autres étapes.
* Ne pas afficher tout le processus.

Si la question n’est pas liée à l’immobilier ou à DAR.COM :

Répondre :

"Je suis spécialisé dans l’immobilier et les services DAR.COM. Je ne peux répondre qu’aux questions liées à ce domaine."

"""
    },
            {"role": "user", "content": message}
        ],
            temperature=0.7
        )

        return response.choices[0].message.content

    except Exception:
        logger.exception("Erreur lors de la génération de réponse chatbot.")
        return "Erreur chatbot IA"


def _fallback_without_llm(message: str) -> str:
    text = (message or "").strip()
    text_l = text.lower()

    if _is_greeting(text_l):
        return (
            "Bonjour ! Je suis l'assistant DAR.COM.\n"
            "Je peux déjà vous aider à chercher des biens du shop en direct.\n"
            "Exemples :\n"
            "- je cherche une maison à sfax\n"
            "- appartement à tunis entre 300k et 500k"
        )

    if _is_shop_search(text_l) or _is_follow_up_filter_message(text_l):
        return (
            "Je n'ai pas pu interroger le shop pour le moment. "
            "Merci de réessayer dans quelques secondes."
        )

    if _is_process_question(text_l):
        return (
            "Pour acheter un bien DAR.COM :\n"
            "1) Réserver une visite du bien.\n"
            "2) Confirmer votre intérêt après la visite.\n"
            "3) Demander un RDV de négociation.\n"
            "4) Finaliser le dossier et le paiement."
        )

    return (
        "Le service IA avancé n'est pas configuré actuellement. "
        "Vous pouvez quand même chercher des biens (ville, budget, surface)."
    )


def _is_greeting(text_l: str) -> bool:
    return any(greet in text_l for greet in [
        "bonjour",
        "salut",
        "bonsoir",
        "hello",
        "hi",
        "salam",
        "slm",
    ])


def _context_key(user_id) -> str:
    raw = str(user_id or "").strip()
    return raw.lower() if raw else "__anonymous__"


def _merge_filters(base: dict, extra: dict) -> dict:
    merged = dict(base or {})
    merged.update(extra or {})
    return merged


def _save_filters_context(user_id, filters: dict):
    if not filters:
        return
    key = _context_key(user_id)
    _last_filters_by_user[key] = dict(filters)
    _last_context_ts_by_user[key] = time.time()


def _get_recent_filters(user_id) -> dict:
    key = _context_key(user_id)
    ts = _last_context_ts_by_user.get(key)
    if ts is None:
        return {}
    if (time.time() - ts) > _CONTEXT_TTL_SECONDS:
        _last_context_ts_by_user.pop(key, None)
        _last_filters_by_user.pop(key, None)
        return {}
    return dict(_last_filters_by_user.get(key, {}))


def _shop_api_base_candidates(shop_base_url=None):
    candidates = []
    if shop_base_url and str(shop_base_url).strip():
        candidates.append(str(shop_base_url).strip())
    env_base = os.getenv('REAL_ESTATE_BASE')
    if env_base and env_base.strip():
        candidates.append(env_base.strip())
    candidates.extend([
        'http://127.0.0.1:5160',
        'http://localhost:5160',
    ])

    normalized = []
    seen = set()
    for c in candidates:
        base = c.rstrip('/')
        if base and base not in seen:
            seen.add(base)
            normalized.append(base)
    return normalized


def _is_follow_up_filter_message(text: str) -> bool:
    text_l = (text or "").lower().strip()
    if not text_l:
        return False

    if re.fullmatch(r"[0-9\s.,kKmM\-–—/dtndinar]+", text_l):
        return True

    if re.search(r"\b\d", text_l) and len(text_l) <= 40:
        return True

    follow_up_markers = [
        "budget",
        "entre",
        "plus de",
        "moins de",
        "a partir",
        "à partir",
        "jusqu",
        "max",
        "min",
        "dt",
        "tnd",
        "dinar",
    ]
    return any(marker in text_l for marker in follow_up_markers)


def _is_process_question(text: str) -> bool:
    text_l = (text or "").lower().strip()
    if not text_l:
        return False

    direct_phrases = [
        "comment je fais pour acheter",
        "comment acheter",
        "comment je peux acheter",
        "comment je peux exprimer mon intérêt",
        "comment je peux exprimer mon interet",
        "comment exprimer mon intérêt",
        "comment exprimer mon interet",
        "comment réserver une visite",
        "comment reserver une visite",
        "comment prendre rdv",
        "comment prendre rendez-vous",
        "comment prendre rendez vous",
        "quelle est la procédure d'achat",
        "quelle est la procedure d'achat",
        "quelles sont les étapes d'achat",
        "quelles sont les etapes d'achat",
    ]
    if any(phrase in text_l for phrase in direct_phrases):
        return True

    asks_how = bool(re.search(r"\b(comment|proc[eé]dure|[eé]tapes?)\b", text_l))
    process_terms = bool(re.search(r"\b(achat|acheter|visite|rdv|rendez[\s-]?vous|int[eé]r[êe]t|n[eé]gociation|paiement|finalisation)\b", text_l))
    return asks_how and process_terms

def _parse_number(s: str) -> float:
    s = s.lower().strip()
    s = s.replace(' ', '').replace('\u00a0', '')
    s = s.replace("٬", "")
    # If dot appears as thousand separator like 100.000 or 1.000.000, remove those dots
    # e.g., '100.000' should be treated as 100000, not 100.0
    if re.search(r"\d+\.(?:\d{3}\.)*\d{3}\b", s):
        s = s.replace('.', '')
    # handle k (thousand), m (million)
    mult = 1
    if s.endswith('k'):
        mult = 1000
        s = s[:-1]
    elif s.endswith('m'):
        mult = 1000000
        s = s[:-1]
    s = s.replace(',', '.')
    try:
        return float(s) * mult
    except Exception:
        # fallback: extract digits
        nums = re.findall(r"[0-9]+(?:[.,][0-9]+)?", s)
        if not nums:
            raise
        return float(nums[0].replace(',', '.')) * mult


def _extract_filters(text: str) -> dict:
    text_l = text.lower()
    filters = {}
    text_l = text_l.replace("–", "-").replace("—", "-")

    # price between
    m = re.search(r'prix[^\n\r]*entre\s+([0-9\s.,kKmM]+)\s+et\s+([0-9\s.,kKmM]+)', text_l)
    if m:
        filters['prixMin'] = int(_parse_number(m.group(1)))
        filters['prixMax'] = int(_parse_number(m.group(2)))
        # continue to allow address/surface detection

    # generic 'entre X et Y' (without explicit 'prix') — treat as price when currency mentioned
    m = re.search(r"entre\s+([0-9\s.,kKmM]+)(?:\s*(?:dt|dinar|tnd))?\s+et\s+([0-9\s.,kKmM]+)(?:\s*(?:dt|dinar|tnd))?", text_l)
    if m and ('dt' in text_l or 'dinar' in text_l or 'tnd' in text_l or re.search(r'\b\d+\s*(?:dt|dinar|tnd)\b', text_l)):
        filters['prixMin'] = int(_parse_number(m.group(1)))
        filters['prixMax'] = int(_parse_number(m.group(2)))
        # continue to allow address detection

    # compact range syntax: "300-400k" / "300k - 400k"
    m = re.search(r"\b([0-9][0-9\s.,kKmM]*)\s*-\s*([0-9][0-9\s.,kKmM]*)\b", text_l)
    if m:
        raw_left = m.group(1).strip().lower()
        raw_right = m.group(2).strip().lower()
        left = _parse_number(raw_left)
        right = _parse_number(raw_right)

        if "k" in raw_right and "k" not in raw_left and left < 10000:
            left *= 1000
        if "m" in raw_right and "m" not in raw_left and left < 10000:
            left *= 1000000
        if "k" in raw_left and "k" not in raw_right and right < 10000:
            right *= 1000
        if "m" in raw_left and "m" not in raw_right and right < 10000:
            right *= 1000000

        left = int(left)
        right = int(right)
        if left > right:
            left, right = right, left
        if any(token in text_l for token in ["surface", "m2", "m²"]):
            filters['surfaceMin'] = left
            filters['surfaceMax'] = right
        else:
            filters['prixMin'] = left
            filters['prixMax'] = right

    # price greater
    m = re.search(r'(?:prix[^\n\r]*(?:supérieur|supérieure|sup|>\s|>\s*|plus de|>=|>))\s*([0-9\s.,kKmM]+)', text_l)
    if m:
        filters['prixMin'] = int(_parse_number(m.group(1)))
        # continue

    m = re.search(r'(?:prix[^\n\r]*(?:inférieur|inférieure|moins de|<=|<))\s*([0-9\s.,kKmM]+)', text_l)
    if m:
        filters['prixMax'] = int(_parse_number(m.group(1)))
        # continue

    m = re.search(r'(?:a|à)\s+partir\s+de\s+([0-9\s.,kKmM]+)(?:\s*(?:dt|dinar|tnd))?', text_l)
    if m:
        filters['prixMin'] = int(_parse_number(m.group(1)))
        # continue

    m = re.search(r'jusqu(?:[\'’]a|[ea]\s+à|\s+à|\s+a|à)\s+([0-9\s.,kKmM]+)(?:\s*(?:dt|dinar|tnd))?', text_l)
    if m:
        filters['prixMax'] = int(_parse_number(m.group(1)))
        # continue

    # generic "plus de 100k" or "moins de 50k" without 'prix'
    m = re.search(r'plus de\s+([0-9\s.,kKmM]+)', text_l)
    if m and 'prix' in text_l:
        filters['prixMin'] = int(_parse_number(m.group(1)))
        # continue
    m = re.search(r'moins de\s+([0-9\s.,kKmM]+)', text_l)
    if m and 'prix' in text_l:
        filters['prixMax'] = int(_parse_number(m.group(1)))
        # continue

    # address / location
    # detect common city/locality names (add more as needed)
    cities = ['la marsa', 'tunis', 'nabeul', 'sfax', 'sousse', 'hammamet', 'ariana', 'gammarth']
    for city in cities:
        if city in text_l:
            # preserve capitalization for display/link
            filters['adresse'] = city.title()

    # fallback: try to capture a location after prepositions like 'à', 'dans', 'sur'
    # (exclude "pour" to avoid false positives such as "pour acheter")
    m = re.search(r"\b(?:à(?!\s+partir)|a(?!\s+partir)|dans|sur|à\s+la|a\s+la)\s+([a-zà-ÿ\-\s]+)\b", text_l)
    if m and 'adresse' not in filters:
        loc = m.group(1).strip()
        # take first token as locality
        loc_token = loc.split()[0]
        invalid_location_tokens = {
            "acheter", "achat", "vendre", "vente", "louer", "location",
            "rdv", "rendez", "vous", "visite", "interet", "intérêt",
            "prix", "budget", "bien", "biens", "maison", "villa",
            "appartement", "terrain", "comment", "faire", "fais"
        }
        if len(loc_token) >= 3 and loc_token not in invalid_location_tokens:
            filters['adresse'] = loc_token.title()

    # surface
    m = re.search(r'surface[^\n\r]*entre\s+([0-9\s.,]+)\s+et\s+([0-9\s.,]+)', text_l)
    if m:
        filters['surfaceMin'] = int(_parse_number(m.group(1)))
        filters['surfaceMax'] = int(_parse_number(m.group(2)))
    m = re.search(r'surface[^\n\r]*(?:supérieur|plus de|>)\s*([0-9\s.,]+)', text_l)
    if m:
        filters['surfaceMin'] = int(_parse_number(m.group(1)))
    m = re.search(r'surface[^\n\r]*(?:inférieur|moins de|<)\s*([0-9\s.,]+)', text_l)
    if m:
        filters['surfaceMax'] = int(_parse_number(m.group(1)))

    return filters


def _build_shop_link(base_url: str, params: dict) -> str:
    # Map our params to the Shop/Index query names (titre/prixMin/prixMax/adresse/surfaceMin/surfaceMax)
    q = {}
    if 'prixMin' in params:
        q['prixMin'] = params['prixMin']
    if 'prixMax' in params:
        q['prixMax'] = params['prixMax']
    if 'adresse' in params:
        q['adresse'] = params['adresse']
    if 'surfaceMin' in params:
        q['surfaceMin'] = params['surfaceMin']
    if 'surfaceMax' in params:
        q['surfaceMax'] = params['surfaceMax']

    query = urllib.parse.urlencode(q)
    if query:
        return base_url.rstrip('/') + '/Shop/Index?' + query
    return base_url.rstrip('/') + '/Shop/Index'


def _is_shop_search(text: str) -> bool:
    text_l = text.lower()
    keywords = [
        'bien', 'biens', 'appartement', 'maison', 'villa', 'terrain',
        'immobilier', 'disponible', 'disponibles', 'à vendre', 'a vendre',
        'recherche', 'cherche', 'je cherche', 'annonce', 'a la marsa', 'la marsa', 'tunis', 'shop',
        'plus cher', 'moins cher', 'le plus cher', 'le moins cher'
    ]
    # include some city names to catch location queries
    keywords.append('nabeul')
    return any(keyword in text_l for keyword in keywords)


def _is_most_expensive_search(text: str) -> bool:
    text_l = text.lower()
    return any(phrase in text_l for phrase in [
        'le bien le plus cher',
        'bien le plus cher',
        'plus cher',
        'le plus cher'
    ])


def _is_cheapest_search(text: str) -> bool:
    text_l = text.lower()
    return any(phrase in text_l for phrase in [
        'le bien le moins cher',
        'bien le moins cher',
        'moins cher',
        'les moins chers',
        'le moins cher'
    ])


