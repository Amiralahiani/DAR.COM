from fastapi import FastAPI
from pydantic import BaseModel
from service import get_response
from fastapi.middleware.cors import CORSMiddleware


app = FastAPI()


app.add_middleware(
    CORSMiddleware,
    allow_origins=["http://localhost:3000"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

class ChatRequest(BaseModel):
    message: str

@app.get("/")
def home():
    return {"message": "Chatbot API running 🚀"}

@app.post("/chat")
def chat(request: ChatRequest):
    reply = get_response(request.message)
    return {"response": reply}