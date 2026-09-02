FROM python:3.12.12-slim-bookworm@sha256:2986c55feb36e6cae00fa1fefb454283e4b33f35e75ff8bdd123b134130be301
ARG CLASSIFIER_GIT_SHA=unknown
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1 DO_NOT_TRACK=1 \
    CLASSIFIER_GIT_SHA=${CLASSIFIER_GIT_SHA}
WORKDIR /app
COPY classifier-service/classifier_service.py ./
COPY JobConceptCatalog.json ./
LABEL org.opencontainers.image.revision=${CLASSIFIER_GIT_SHA}
EXPOSE 8081
USER 65532:65532
HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=4 \
    CMD ["python3", "/app/classifier_service.py", "--healthcheck"]
ENTRYPOINT ["python3", "/app/classifier_service.py"]
