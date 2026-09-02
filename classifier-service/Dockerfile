FROM python:3.12.12-alpine3.23@sha256:ba94b66cc4fee436c27cbc42a6e315ead60729e8d2f109e3606a9d1bdba7bf0e
ARG CLASSIFIER_GIT_SHA=unknown
RUN apk add --no-cache --upgrade \
    libcrypto3=3.5.8-r0 \
    libssl3=3.5.8-r0 \
    musl=1.2.5-r23 \
    musl-utils=1.2.5-r23 \
    sqlite-libs=3.53.4-r0 \
    zlib=1.3.2-r0
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
