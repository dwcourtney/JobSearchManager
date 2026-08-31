FROM python:3.13.15-slim-bookworm@sha256:b6bd71b0dd3811ddbcbc523ec2965fd1e1bcfdf7a20ab24679273d3bee726129
ARG CLASSIFIER_GIT_SHA=unknown
WORKDIR /app

RUN python -m pip install --no-cache-dir --upgrade \
    setuptools==83.0.0 \
    msgpack==1.2.1

COPY server.py healthcheck.py ./

ENV CLASSIFIER_GIT_SHA=${CLASSIFIER_GIT_SHA} \
    PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1
LABEL org.opencontainers.image.revision=${CLASSIFIER_GIT_SHA}

EXPOSE 8081
USER 65532:65532
HEALTHCHECK --interval=15s --timeout=5s --start-period=5s --retries=4 \
    CMD ["python", "/app/healthcheck.py"]
ENTRYPOINT ["python", "/app/server.py"]
