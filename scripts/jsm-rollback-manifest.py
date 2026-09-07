"""Project an old, uninterpolated Compose config into a model-free JSM rollback.

The JSON is itself a Compose manifest. Interpolation happens on the later up,
so no retired service image variable is required even for an old manifest.
"""
import json
import sys

config = json.load(sys.stdin)
service = config["services"]["jsm"]
service["image"] = "${JSM_IMAGE_REFERENCE:?Set the previous JSM image}"
service.get("environment", {}).pop("DeepAnalysis__BaseUrl", None)
for key in ("depends_on", "networks"):
    value = service.get(key, {})
    for name in ("deep-analysis", "ollama", "job-classifier", "classifier"):
        if isinstance(value, dict): value.pop(name, None)
        elif name in value: value.remove(name)
    if not value: service.pop(key, None)
# Compose classifies an uninterpolated ${JSM_LAB_ROOT}/... short mount as
# a named volume. Restore the known application bind mounts before interpolation.
for mount in service.get("volumes", []):
    if isinstance(mount, dict) and mount.get("source", "").startswith("${JSM_LAB_ROOT"):
        mount["type"] = "bind"
        mount.pop("volume", None)
config["services"] = {"jsm": service}
used_networks = service.get("networks", {"default": {}})
config["networks"] = {name: value for name, value in config.get("networks", {}).items() if name in used_networks}
json.dump(config, sys.stdout, indent=2)
print()
