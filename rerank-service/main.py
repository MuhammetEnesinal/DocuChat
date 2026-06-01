"""
BGE Reranker FastAPI Sidecar
============================
DocuChat .NET API'nin chunk reranking icin cagirdigi yerel servis.
BAAI/bge-reranker-v2-m3 cross-encoder modeli (multilingual, Turkce optimal).

Port: 8085 (appsettings.json Reranker:BaseUrl ile eslesir)

Endpoint:
  POST /rerank   { query, documents[], top_n } -> [{index, score}]
  GET  /health   -> {status, model, device}
"""
from fastapi import FastAPI
from pydantic import BaseModel
from sentence_transformers import CrossEncoder
import torch

app = FastAPI(title="BGE Reranker Service", version="1.0.0")

device = "cuda" if torch.cuda.is_available() else "cpu"
print(f"[Reranker] Model yukleniyor (device={device})...")
model = CrossEncoder("BAAI/bge-reranker-v2-m3", max_length=512, device=device)

# Warm-up — ilk gercek istek beklemesin
print("[Reranker] Warm-up calisiyor...")
_ = model.predict([("warm", "up")], show_progress_bar=False)
print(f"[Reranker] HAZIR (device={device}) -- http://127.0.0.1:8085")


class RerankRequest(BaseModel):
    query: str
    documents: list[str]
    top_n: int = 5


class RerankResult(BaseModel):
    index: int
    score: float


class RerankResponse(BaseModel):
    results: list[RerankResult]


@app.post("/rerank", response_model=RerankResponse)
def rerank(req: RerankRequest):
    if not req.documents:
        return {"results": []}
    pairs = [(req.query, doc) for doc in req.documents]
    scores = model.predict(pairs, batch_size=8, show_progress_bar=False)
    ranked = sorted(
        [{"index": i, "score": float(s)} for i, s in enumerate(scores)],
        key=lambda x: x["score"],
        reverse=True,
    )[: req.top_n]
    return {"results": ranked}


@app.get("/health")
def health():
    return {"status": "ok", "model": "BAAI/bge-reranker-v2-m3", "device": device}
