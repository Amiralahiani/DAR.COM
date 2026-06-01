from fastapi import FastAPI
from pydantic import BaseModel
from service import get_response
from fastapi.middleware.cors import CORSMiddleware
from typing import Optional


app = FastAPI()


app.add_middleware(
    CORSMiddleware,
    allow_origins=["http://localhost:3000", "http://localhost:5160", "http://127.0.0.1:5160"],
    allow_credentials=False,
    allow_methods=["*"],
    allow_headers=["*"],
)

class ChatRequest(BaseModel):
    message: str
    user_id: Optional[str] = None
    shop_base_url: Optional[str] = None

@app.get("/")
def home():
    return {"message": "Chatbot API running 🚀"}

@app.post("/chat")
def chat(request: ChatRequest):
    reply = get_response(request.message, request.user_id, request.shop_base_url)
    return {"response": reply}
