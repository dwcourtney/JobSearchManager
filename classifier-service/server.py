#!/usr/bin/env python3
"""Minimal, model-free HTTP contract and NVIDIA visibility probe for JSM."""

from __future__ import annotations

import json
import logging
import os
import subprocess
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any

SERVICE_VERSION = "0.1.0"
PROTOCOL_VERSION = "1"
MAX_BODY_BYTES = 8 * 1024 * 1024
NVIDIA_QUERY = "name,memory.total,memory.used,driver_version"

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
LOGGER = logging.getLogger("jsm-classifier")


def gpu_diagnostic() -> dict[str, Any]:
    """Return a stable schema whether or not NVIDIA tooling/devices are present."""
    result: dict[str, Any] = {
        "gpuAvailable": False,
        "deviceCount": 0,
        "deviceName": None,
        "vramTotalMiB": None,
        "vramUsedMiB": None,
        "driverVersion": None,
    }
    try:
        completed = subprocess.run(
            ["nvidia-smi", f"--query-gpu={NVIDIA_QUERY}", "--format=csv,noheader,nounits"],
            check=True,
            capture_output=True,
            text=True,
            timeout=3,
        )
        rows = [line.strip() for line in completed.stdout.splitlines() if line.strip()]
        if not rows:
            return result
        name, total, used, driver = [value.strip() for value in rows[0].split(",", 3)]
        result.update(
            gpuAvailable=True,
            deviceCount=len(rows),
            deviceName=name,
            vramTotalMiB=int(total),
            vramUsedMiB=int(used),
            driverVersion=driver,
        )
    except (FileNotFoundError, subprocess.SubprocessError, ValueError, OSError):
        pass
    return result


def service_metadata() -> dict[str, Any]:
    return {
        "serviceVersion": SERVICE_VERSION,
        "protocolVersion": PROTOCOL_VERSION,
        "revision": os.environ.get("CLASSIFIER_GIT_SHA", "unknown"),
    }


class Handler(BaseHTTPRequestHandler):
    server_version = "JsmClassifier"
    sys_version = ""

    def do_GET(self) -> None:  # noqa: N802
        if self.path != "/healthz":
            self._json(404, {"error": "not found"})
            return
        self._json(200, {"status": "healthy", **service_metadata(), **gpu_diagnostic()})

    def do_POST(self) -> None:  # noqa: N802
        if self.path != "/classify":
            self._json(404, {"error": "not found"})
            return
        started = time.perf_counter()
        payload = self._request_json()
        if payload is None:
            return
        errors = self._validate(payload)
        if errors:
            self._json(400, {"error": "invalid request", "fields": errors})
            return
        job_id = payload["jobId"]
        title = payload["title"]
        description = payload["description"]
        response = {
            "received": True,
            "jobId": job_id,
            "title": title,
            "descriptionLength": len(description),
            **service_metadata(),
            **gpu_diagnostic(),
        }
        elapsed_ms = (time.perf_counter() - started) * 1000
        LOGGER.info("classified jobId=%s characters=%d durationMs=%.3f gpuAvailable=%s",
                    self._log_value(job_id), len(description), elapsed_ms,
                    response["gpuAvailable"])
        self._json(200, response)

    def _request_json(self) -> dict[str, Any] | None:
        content_type = self.headers.get("Content-Type", "").split(";", 1)[0].strip().lower()
        if content_type != "application/json":
            self._json(415, {"error": "content type must be application/json"})
            return None
        try:
            length = int(self.headers.get("Content-Length", "-1"))
        except ValueError:
            length = -1
        if length < 0 or length > MAX_BODY_BYTES:
            self._json(413 if length > MAX_BODY_BYTES else 400, {"error": "invalid content length"})
            return None
        try:
            payload = json.loads(self.rfile.read(length))
        except (json.JSONDecodeError, UnicodeDecodeError):
            self._json(400, {"error": "malformed JSON"})
            return None
        if not isinstance(payload, dict):
            self._json(400, {"error": "request must be a JSON object"})
            return None
        return payload

    @staticmethod
    def _validate(payload: dict[str, Any]) -> list[str]:
        errors = []
        for field in ("jobId", "title", "description"):
            value = payload.get(field)
            if not isinstance(value, str) or (field != "description" and not value.strip()):
                errors.append(field)
        return errors

    def _json(self, status: int, payload: dict[str, Any]) -> None:
        body = json.dumps(payload, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    @staticmethod
    def _log_value(value: str) -> str:
        return value.replace("\r", " ").replace("\n", " ")[:160]

    def log_message(self, format_string: str, *args: Any) -> None:
        LOGGER.debug(format_string, *args)


if __name__ == "__main__":
    port = int(os.environ.get("CLASSIFIER_PORT", "8081"))
    ThreadingHTTPServer(("0.0.0.0", port), Handler).serve_forever()
