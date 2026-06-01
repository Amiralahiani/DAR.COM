import os
import re
import requests
import urllib.parse
from openai import OpenAI
from dotenv import load_dotenv

load_dotenv()

print("API KEY =", os.getenv("GROQ_API_KEY"))

client = OpenAI(
    api_key=os.getenv("GROQ_API_KEY"),
    base_url="https://api.groq.com/openai/v1"
)

def get_response(message):
    # try to handle shop-related queries via the Shop API (DB-backed)
    try:
        filters = {}
        try:
            filters = _extract_filters(message)
        except Exception:
            filters = {}

        # If message looks like a shop search or filters present, query the shop API first
        if filters or _is_shop_search(message):
            api_base = os.getenv('REAL_ESTATE_BASE', 'http://127.0.0.1:5160')
            api_url = api_base.rstrip('/') + '/api/shop/filter'
            try:
                resp = requests.get(api_url, params=filters, timeout=5)
                if resp.status_code == 200:
                    data = resp.json()
                    count = data.get('count', 0)
                    items = data.get('items', [])
                    if count == 0:
                        return "Je n'ai trouvé aucun bien disponible correspondant à ta recherche dans le shop DAR.COM."

                    if _is_most_expensive_search(message) and items:
                        items = sorted(items, key=lambda it: float(it.get('Prix') or it.get('prix') or 0), reverse=True)
                        item = items[0]
                        title = item.get('Titre') or item.get('titre') or 'Titre inconnu'
                        prix = item.get('Prix') or item.get('prix')
                        bid = item.get('Id') or item.get('id')
                        detail_link = api_base.rstrip('/') + f"/Shop/Details/{bid}" if bid is not None else ''
                        narrative = f"Le bien le plus cher disponible dans le shop DAR.COM est :\n- {title} — {prix} DT — {detail_link}"
                        narrative += f"\nConsulte-le ici : {detail_link}"
                        return narrative

                    if _is_cheapest_search(message) and items:
                        items = sorted(items, key=lambda it: float(it.get('Prix') or it.get('prix') or 0))
                        item = items[0]
                        title = item.get('Titre') or item.get('titre') or 'Titre inconnu'
                        prix = item.get('Prix') or item.get('prix')
                        bid = item.get('Id') or item.get('id')
                        detail_link = api_base.rstrip('/') + f"/Shop/Details/{bid}" if bid is not None else ''
                        narrative = f"Le bien le moins cher disponible dans le shop DAR.COM est :\n- {title} — {prix} DT — {detail_link}"
                        narrative += f"\nConsulte-le ici : {detail_link}"
                        return narrative

                    narrative = f"J'ai trouvé {count} bien(s) disponibles dans le shop DAR.COM."
                    # include up to 3-item preview with details link
                    if items:
                        preview = []
                        for it in items[:3]:
                            title = it.get('Titre') or it.get('titre') or 'Titre inconnu'
                            prix = it.get('Prix') or it.get('prix')
                            bid = it.get('Id') or it.get('id')
                            detail_link = api_base.rstrip('/') + f"/Shop/Details/{bid}" if bid is not None else ''
                            if prix is not None and detail_link:
                                preview.append(f"- {title} — {prix} DT — {detail_link}")
                            elif detail_link:
                                preview.append(f"- {title} — {detail_link}")
                            else:
                                preview.append(f"- {title}")
                        if preview:
                            narrative += "\nAperçu :\n" + "\n".join(preview)

                    link = _build_shop_link(api_base, filters)
                    narrative += f"\nConsulte-les ici : {link}"
                    return narrative
            except Exception:
                # if shop API fails, fall back to LLM below
                pass

    except Exception:
        # non-blocking: continue to LLM fallback
        pass

    # fallback to LLM for general conversation (keep original behavior)
    try:
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

    except Exception as e:
        print("ERROR:", e)
        return "Erreur chatbot IA"

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
            return filters

    # fallback: try to capture a location after prepositions like 'à', 'dans', 'pour'
    m = re.search(r"\b(?:à(?!\s+partir)|a(?!\s+partir)|dans|sur|pour|à\s+la|a\s+la)\s+([a-zà-ÿ\-\s]+)\b", text_l)
    if m:
        loc = m.group(1).strip()
        # take first token as locality
        loc_token = loc.split()[0]
        if len(loc_token) >= 3:
            filters['adresse'] = loc_token.title()
            return filters

    # surface
    m = re.search(r'surface[^\n\r]*entre\s+([0-9\s.,]+)\s+et\s+([0-9\s.,]+)', text_l)
    if m:
        filters['surfaceMin'] = int(_parse_number(m.group(1)))
        filters['surfaceMax'] = int(_parse_number(m.group(2)))
        return filters
    m = re.search(r'surface[^\n\r]*(?:supérieur|plus de|>)\s*([0-9\s.,]+)', text_l)
    if m:
        filters['surfaceMin'] = int(_parse_number(m.group(1)))
        return filters
    m = re.search(r'surface[^\n\r]*(?:inférieur|moins de|<)\s*([0-9\s.,]+)', text_l)
    if m:
        filters['surfaceMax'] = int(_parse_number(m.group(1)))
        return filters

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
        'recherche', 'annonce', 'a la marsa', 'la marsa', 'tunis', 'shop',
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


