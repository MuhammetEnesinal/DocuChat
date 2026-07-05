FROM python:3.12-slim
WORKDIR /app

# torch CPU-only index'ten kurulur: CUDA'lı varsayılan paket ~2.5 GB, CPU'su ~200 MB.
# (GPU'lu sunucuya geçilirse bu satır kaldırılıp requirements.txt'e dönülür.)
RUN pip install --no-cache-dir torch==2.4.1 --index-url https://download.pytorch.org/whl/cpu \
    && pip install --no-cache-dir fastapi==0.115.0 "uvicorn[standard]==0.32.0" sentence-transformers==3.2.0

COPY main.py .

# HuggingFace model cache → volume'a bağlanır; BAAI/bge-reranker-v2-m3 (~2.2 GB)
# ilk açılışta bir kez iner, sonraki başlatmalar cache'ten yükler.
ENV HF_HOME=/models

EXPOSE 8085
CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8085"]
