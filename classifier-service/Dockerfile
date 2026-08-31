FROM python:3.12.14-alpine3.24@sha256:285a71327884a4d50efbea30104473b0fa43ecefa499458899670ca30dae76e5
ARG CLASSIFIER_GIT_SHA=unknown
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1 CLASSIFIER_GIT_SHA=${CLASSIFIER_GIT_SHA}
RUN apk upgrade --no-cache && rm -rf /var/cache/apk/*
WORKDIR /app
COPY classifier_service.py ./
LABEL org.opencontainers.image.revision=${CLASSIFIER_GIT_SHA}
EXPOSE 8081
USER 65532:65532
HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=4 \
    CMD ["python3", "/app/classifier_service.py", "--healthcheck"]
ENTRYPOINT ["python3", "/app/classifier_service.py"]
