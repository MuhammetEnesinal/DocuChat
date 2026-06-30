"""
DocuChat Yerel AI Sidecar (FastAPI)
===================================
.NET API'nin cagirdigi yerel servis. Iki gorev:

1) RERANK  — chunk reranking (BAAI/bge-reranker-v2-m3 cross-encoder, multilingual)
2) GORSEL EMBEDDING (CLIP) — resim ve metni AYNI vektor uzayina koyar; boylece
   "bir soruya gorsel olarak en yakin resimler" cosine ile bulunabilir.
   - Resim encoder : clip-ViT-B-32            (512-dim)
   - Metin encoder : clip-ViT-B-32-multilingual-v1 (50+ dil, Turkce dahil, ayni uzay)

Port: 8085 (appsettings.json Reranker:BaseUrl / ImageEmbedding:BaseUrl ile eslesir)

Endpoint:
  POST /rerank        { query, documents[], top_n }      -> [{index, score}]
  POST /embed-image   { images_base64[] }                -> { vectors[][] }  (512-dim)
  POST /embed-text    { texts[] }                        -> { vectors[][] }  (512-dim)
  GET  /health        -> { status, models, device }
"""
import base64
import io

from fastapi import FastAPI
from pydantic import BaseModel
from sentence_transformers import CrossEncoder, SentenceTransformer
from PIL import Image
import torch

app = FastAPI(title="DocuChat AI Sidecar", version="2.0.0")

device = "cuda" if torch.cuda.is_available() else "cpu"

print(f"[Sidecar] Modeller yukleniyor (device={device})...")

# 1) Reranker (mevcut)
reranker = CrossEncoder("BAAI/bge-reranker-v2-m3", max_length=512, device=device)

# 2) CLIP — resim ve metin AYNI 512-dim uzayda.
#    Resim ve metin icin iki ayri model yuklenir; ciktilar ayni uzaya hizalidir,
#    bu yuzden bir resmin gorsel vektoru ile bir metnin vektoru cosine ile karsilastirilabilir.
clip_image = SentenceTransformer("clip-ViT-B-32", device=device)
clip_text = SentenceTransformer("clip-ViT-B-32-multilingual-v1", device=device)

# Warm-up — ilk gercek istek beklemesin
print("[Sidecar] Warm-up...")
_ = reranker.predict([("warm", "up")], show_progress_bar=False)
_ = clip_text.encode(["isinma"], show_progress_bar=False)
_warm_img = Image.new("RGB", (32, 32), (127, 127, 127))
_ = clip_image.encode([_warm_img], show_progress_bar=False)
print(f"[Sidecar] HAZIR (device={device}) -- http://127.0.0.1:8085")


# ───────────────────────── Rerank ─────────────────────────
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
    scores = reranker.predict(pairs, batch_size=8, show_progress_bar=False)
    ranked = sorted(
        [{"index": i, "score": float(s)} for i, s in enumerate(scores)],
        key=lambda x: x["score"],
        reverse=True,
    )[: req.top_n]
    return {"results": ranked}


# ───────────────────── Gorsel Embedding (CLIP) ─────────────────────
class EmbedImageRequest(BaseModel):
    # Her eleman bir resmin base64'u (data URI prefix'i olabilir veya olmayabilir).
    images_base64: list[str]


class EmbedTextRequest(BaseModel):
    texts: list[str]


class EmbedResponse(BaseModel):
    # Girdi ile AYNI sirada 512-dim vektorler. Decode edilemeyen resim icin bos liste.
    vectors: list[list[float]]


def _decode_image(b64: str) -> Image.Image | None:
    try:
        # "data:image/png;base64,...." prefix'ini temizle
        comma = b64.find(",")
        if b64.startswith("data:") and comma != -1:
            b64 = b64[comma + 1:]
        raw = base64.b64decode(b64)
        if len(raw) < 64:
            return None
        return Image.open(io.BytesIO(raw)).convert("RGB")
    except Exception as ex:
        print(f"[CLIP] image decode hatasi: {ex}")
        return None


@app.post("/embed-image", response_model=EmbedResponse)
def embed_image(req: EmbedImageRequest):
    if not req.images_base64:
        return {"vectors": []}

    images: list[Image.Image | None] = [_decode_image(b) for b in req.images_base64]
    valid = [(i, img) for i, img in enumerate(images) if img is not None]

    # Cikti girdiyle ayni uzunlukta; decode edilemeyenler bos vektor.
    out: list[list[float]] = [[] for _ in images]
    if valid:
        encoded = clip_image.encode(
            [img for _, img in valid],
            batch_size=16,
            normalize_embeddings=True,  # cosine icin normalize
            show_progress_bar=False,
        )
        for (idx, _), vec in zip(valid, encoded):
            out[idx] = vec.tolist()
    return {"vectors": out}


@app.post("/embed-text", response_model=EmbedResponse)
def embed_text(req: EmbedTextRequest):
    if not req.texts:
        return {"vectors": []}
    encoded = clip_text.encode(
        req.texts,
        batch_size=32,
        normalize_embeddings=True,
        show_progress_bar=False,
    )
    return {"vectors": [v.tolist() for v in encoded]}


@app.get("/health")
def health():
    return {
        "status": "ok",
        "models": {
            "reranker": "BAAI/bge-reranker-v2-m3",
            "clip_image": "clip-ViT-B-32",
            "clip_text": "clip-ViT-B-32-multilingual-v1",
        },
        "device": device,
    }
