# Monorepo: Railway builds from repo root — app lives in website/
FROM python:3.12-slim-bookworm

WORKDIR /app
ENV PYTHONUNBUFFERED=1
ENV PYTHONPATH=/app

COPY website/requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY website/app ./app
COPY website/templates ./templates
COPY website/static ./static
COPY website/reset_admin.py .

RUN mkdir -p data uploads

# Railway sets PORT; Fly/internal Docker often use 8080
EXPOSE 8080
CMD ["sh", "-c", "uvicorn app.main:app --host 0.0.0.0 --port ${PORT:-8080}"]
