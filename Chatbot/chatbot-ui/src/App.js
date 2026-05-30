import { useState } from "react";
import "./App.css";
import ReactMarkdown from "react-markdown";

function App() {
  const [messages, setMessages] = useState([]);
  const [input, setInput] = useState("");
  const [loading, setLoading] = useState(false);

  const sendMessage = async () => {
    if (!input.trim()) return;

    const userMessage = input;

    setMessages((prev) => [
      ...prev,
      { role: "user", text: userMessage },
    ]);

    setInput("");
    setLoading(true);

    try {
      const res = await fetch("http://127.0.0.1:8000/chat", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ message: userMessage }),
      });

      const data = await res.json();

      setMessages((prev) => [
        ...prev,
        { role: "bot", text: data.response },
      ]);

    } catch (err) {
      setMessages((prev) => [
        ...prev,
        { role: "bot", text: "Erreur serveur ❌" },
      ]);
    }

    setLoading(false);
  };

  return (
    <div className="container">
      <div className="chat-box">

        <div className="header">
        🏡DAR.COM AI Assistant
        <span>Analyse intelligente des biens immobiliers en Tunisie</span>
        </div>

        <div className="messages">

          {messages.length === 0 && (
            <div className="welcome">
              💬 Pose une question immobilière :
              <ul>
                <li>Ce prix est-il correct ?</li>
                <li>Location rentable ou pas ?</li>
                <li>Comment acheter un bien en Tunisie ?</li>
              </ul>
            </div>
          )}

          {messages.map((m, i) => (
            
        <div key={i} className={`msg ${m.role}`}>
            {m.role === "user" ? (
            <>
                👤 {m.text}
            </>
            ) : (
            <>
                🏡 <ReactMarkdown>{m.text}</ReactMarkdown>
            </>
            )}
        </div>
        ))}

          {loading && (
            <div className="msg bot">🏡 Analyse en cours...</div>
          )}

        </div>

        <div className="input-box">
          <input
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && sendMessage()}
            placeholder="Ex: Appartement S+1 à Sousse 150k, bon prix ?"
          />
          <button onClick={sendMessage}>Envoyer</button>
        </div>

      </div>
    </div>
  );
}

export default App;