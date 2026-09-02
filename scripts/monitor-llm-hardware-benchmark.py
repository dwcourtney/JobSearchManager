#!/usr/bin/env python3
"""Observe an isolated JSM hardware benchmark without touching its inputs."""

import argparse
import json
import os
import re
import subprocess
import time
from datetime import datetime, timezone


MODEL_DIGEST = "sha256:0edcdef34593eac1aa2be9c7d06c432dcf81945adca5eca2f27662c18f168ba0"


def command(*arguments):
    return subprocess.run(arguments, check=True, capture_output=True, text=True).stdout.strip()


def memory_bytes(value):
    number, unit = re.match(r"\s*([0-9.]+)\s*([KMGTP]?i?B)", value).groups()
    factors = {"B": 1, "kB": 1000, "KB": 1000, "KiB": 1024,
               "MB": 1000**2, "MiB": 1024**2, "GB": 1000**3,
               "GiB": 1024**3, "TB": 1000**4, "TiB": 1024**4}
    return int(float(number) * factors[unit])


def container_memories(*names):
    output = command("docker", "stats", "--no-stream", "--format",
                     "{{.Name}} {{.MemUsage}}", *names)
    result = {}
    for line in output.splitlines():
        name, usage = line.split(maxsplit=1)
        result[name] = memory_bytes(usage.split("/")[0])
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("output")
    parser.add_argument("--ollama", default="jsm-benchmark-ollama")
    parser.add_argument("--adapter", default="jsm-benchmark-deep-analysis")
    parser.add_argument("--interval", type=float, default=1.0)
    parser.add_argument("--stop-file", required=True)
    arguments = parser.parse_args()

    driver = command("nvidia-smi", "--query-gpu=driver_version", "--format=csv,noheader").splitlines()[0]
    gpu_name = command("nvidia-smi", "--query-gpu=name", "--format=csv,noheader").splitlines()[0]
    docker = command("docker", "version", "--format", "{{.Server.Version}}")
    toolkit = command("nvidia-ctk", "--version").splitlines()[0]
    banner = command("nvidia-smi")
    cuda_match = re.search(r"CUDA Version:\s*([0-9.]+)", banner)
    cuda = cuda_match.group(1) if cuda_match else "unknown"
    samples = []
    previous_time = None
    previous_power = None
    energy_watt_seconds = 0.0

    while not os.path.exists(arguments.stop_file):
        started = time.monotonic()
        fields = command("nvidia-smi",
            "--query-gpu=utilization.gpu,memory.used,power.draw",
            "--format=csv,noheader,nounits").splitlines()[0].split(",")
        now = time.monotonic()
        utilization, memory_mib, power = (float(value.strip()) for value in fields)
        if previous_time is not None:
            energy_watt_seconds += (previous_power + power) / 2 * (now - previous_time)
        previous_time, previous_power = now, power
        memory = container_memories(arguments.ollama, arguments.adapter)
        samples.append({
            "gpu": utilization,
            "memory": int(memory_mib * 1024 * 1024),
            "power": power,
            "ollama": memory[arguments.ollama],
            "adapter": memory[arguments.adapter],
        })
        time.sleep(max(0.0, arguments.interval - (time.monotonic() - started)))

    if not samples:
        raise RuntimeError("No hardware observations were collected.")
    report = {
        "modelDigest": MODEL_DIGEST,
        "gpuName": gpu_name,
        "driverVersion": driver,
        "cudaVersion": cuda,
        "dockerVersion": docker,
        "nvidiaContainerToolkitVersion": toolkit,
        "observedUtc": datetime.now(timezone.utc).isoformat(),
        "sampleCount": len(samples),
        "averageGpuUtilizationPercent": sum(row["gpu"] for row in samples) / len(samples),
        "peakGpuMemoryUsedBytes": max(row["memory"] for row in samples),
        "peakOllamaContainerRamBytes": max(row["ollama"] for row in samples),
        "peakAdapterContainerRamBytes": max(row["adapter"] for row in samples),
        "averageGpuPowerWatts": sum(row["power"] for row in samples) / len(samples),
        "peakGpuPowerWatts": max(row["power"] for row in samples),
        "approximateGpuEnergyWattHours": energy_watt_seconds / 3600,
        "source": "Repeated nvidia-smi host-GPU and single-pass docker stats container-memory samples requested at one-second intervals; actual monotonic intervals are used for trapezoidal board-power integration."
    }
    destination = os.path.abspath(arguments.output)
    temporary = destination + ".tmp"
    with open(temporary, "w", encoding="utf-8") as stream:
        json.dump(report, stream, indent=2)
        stream.write("\n")
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, destination)


if __name__ == "__main__":
    main()
