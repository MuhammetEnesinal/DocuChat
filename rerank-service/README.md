# BGE Reranker Sidecar

DocuChat'in chunk reranking için kullandığı yerel Python servisi.

## Kurulum (1 kere)

```powershell
cd rerank-service
.\start.bat
```

İlk çalıştırmada (~3-5 dk):
- `venv` oluşturulur
- `pip install` (sentence-transformers + torch ~2 GB)
- İlk request'te BGE-reranker-v2-m3 indirilir (~570 MB)

Hazır olunca: `[Reranker] HAZIR (device=cpu) -- http://127.0.0.1:8085`

## Günlük kullanım

```powershell
.\start.bat
```
10 sn'de hazır (model cache'ten yüklenir).

## API

### POST /rerank
```json
{ "query": "Maaş ne zaman ödenir?",
  "documents": ["chunk1...", "chunk2...", ...],
  "top_n": 5 }
```
Cevap: `[{"index": 2, "score": 0.94}, ...]`

### GET /health
Servis ve model durumu.

## Durdurma
`Ctrl+C`
