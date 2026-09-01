FROM nvidia/cuda:12.6.3-cudnn-runtime-ubuntu24.04@sha256:8aef630a54bc5c5146ae5ce68e6af5caa3df0fb690bb91544175c91f307e4356
ARG CLASSIFIER_GIT_SHA=unknown
ENV DEBIAN_FRONTEND=noninteractive PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1 HF_HUB_DISABLE_TELEMETRY=1 DO_NOT_TRACK=1 \
    CLASSIFIER_GIT_SHA=${CLASSIFIER_GIT_SHA}
RUN apt-get update && apt-get upgrade --yes \
    && apt-get install --yes --no-install-recommends ca-certificates python3 python3-pip \
       cuda-cupti-12-6 libcusparselt0-cuda-12 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
RUN python3 -m pip install --break-system-packages --no-cache-dir --no-deps \
      --index-url https://download.pytorch.org/whl/cu126 torch==2.6.0+cu126
COPY classifier-service/requirements.lock ./
RUN python3 -m pip install --break-system-packages --no-cache-dir --requirement requirements.lock
COPY classifier-service/classifier_service.py ./
COPY JobConceptCatalog.json ./
ENV LD_LIBRARY_PATH=/usr/lib/x86_64-linux-gnu/libcusparseLt/12:/usr/local/cuda-12.6/targets/x86_64-linux/lib:${LD_LIBRARY_PATH}
LABEL org.opencontainers.image.revision=${CLASSIFIER_GIT_SHA}
EXPOSE 8081
USER 65532:65532
HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=4 \
    CMD ["python3", "/app/classifier_service.py", "--healthcheck"]
ENTRYPOINT ["python3", "/app/classifier_service.py"]
