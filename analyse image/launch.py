"""Lance uvicorn depuis le bon répertoire avec le .env chargé."""
import os
import sys

# Forcer le CWD vers ce dossier
HERE = os.path.dirname(os.path.abspath(__file__))
os.chdir(HERE)

from dotenv import load_dotenv
load_dotenv(os.path.join(HERE, ".env"))

import uvicorn
uvicorn.run("main:app", host="0.0.0.0", port=8001, reload=False)
