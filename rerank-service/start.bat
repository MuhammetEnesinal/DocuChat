@echo off
cd /d "%~dp0"

if not exist venv\Scripts\python.exe goto SETUP
goto RUN

:SETUP
echo [setup] venv olusturuluyor...
python -m venv venv
call venv\Scripts\activate.bat
echo [setup] paketler yukleniyor (ilk seferde ~3-5 dk)...
pip install -r requirements.txt
goto RUN

:RUN
call venv\Scripts\activate.bat
echo [start] BGE Reranker servisi 127.0.0.1:8085 portunda baslatiliyor...
python -m uvicorn main:app --host 127.0.0.1 --port 8085 --workers 1
