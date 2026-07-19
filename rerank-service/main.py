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

# GPU varsa kullanilir, yoksa CPU'ya dusulur. max_length=512 modelin girdi siniridir;
# daha uzun chunk'lar bu uzunlukta kesilir.
device = "cuda" if torch.cuda.is_available() else "cpu"
print(f"[Reranker] Model yukleniyor (device={device})...")
model = CrossEncoder("BAAI/bge-reranker-v2-m3", max_length=512, device=device)

# Model ilk cagrida tembel yuklendigi icin burada bir kez calistirilir; boylece ilk gercek
# istek model yukleme suresini beklemez.
print("[Reranker] Warm-up calisiyor...")
_ = model.predict([("warm", "up")], show_progress_bar=False)
print(f"[Reranker] HAZIR (device={device}) -- http://127.0.0.1:8085")


# Istek govdesi: soru, siralanacak chunk metinleri ve dondurulecek sonuc adedi.
class RerankRequest(BaseModel):
    query: str
    documents: list[str]
    top_n: int = 5


# index, gelen documents listesindeki sirayi gosterir; cagiran taraf skoru degil bu indeksi
# kullanarak kendi chunk nesnesini bulur.
class RerankResult(BaseModel):
    index: int
    score: float


class RerankResponse(BaseModel):
    results: list[RerankResult]


@app.post("/rerank", response_model=RerankResponse)
def rerank(req: RerankRequest):
    if not req.documents:
        return {"results": []}
    # Cross-encoder her chunk'i soruyla birlikte degerlendirir; bu nedenle (soru, chunk) ciftleri
    # olusturulur. Bi-encoder'dan farkli olarak chunk'lar tek basina vektorlenmez.
    pairs = [(req.query, doc) for doc in req.documents]
    scores = model.predict(pairs, batch_size=8, show_progress_bar=False)
    # Skora gore azalan siralanip en iyi top_n sonuc dondurulur.
    ranked = sorted(
        [{"index": i, "score": float(s)} for i, s in enumerate(scores)],
        key=lambda x: x["score"],
        reverse=True,
    )[: req.top_n]
    return {"results": ranked}


# Container saglik kontrolu ve .NET tarafindaki baglanti dogrulamasi icin kullanilir.
@app.get("/health")
def health():
    return {"status": "ok", "model": "BAAI/bge-reranker-v2-m3", "device": device}
