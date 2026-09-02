FROM python:3.12.11-slim-bookworm@sha256:c00fc7b44d844b6da22861ec24af43968a5200eac4ec607b4725d585165d6b49
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
