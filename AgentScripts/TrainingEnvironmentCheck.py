"""Read-only local training inventory. Uses stdlib; never downloads or trains."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import struct
import subprocess
import sys


def gguf_metadata(path):
    sizes = {0: 1, 1: 1, 2: 2, 3: 2, 4: 4, 5: 4, 6: 4, 7: 1, 10: 8, 11: 8, 12: 8}
    formats = {0: "B", 1: "b", 2: "H", 3: "h", 4: "I", 5: "i", 6: "f", 7: "?", 10: "Q", 11: "q", 12: "d"}
    with path.open("rb") as stream:
        def number(fmt):
            return struct.unpack("<" + fmt, stream.read(struct.calcsize("<" + fmt)))[0]

        def string(keep):
            size = number("Q")
            if keep:
                if size > 32_000_000:
                    raise ValueError("Unexpected GGUF metadata string size")
                return stream.read(size).decode("utf-8")
            stream.seek(size, 1)

        def value(kind, keep=False):
            if kind == 8:
                return string(keep)
            if kind == 9:
                element, count = number("I"), number("Q")
                if element in sizes:
                    stream.seek(count * sizes[element], 1)
                else:
                    for _ in range(count):
                        value(element)
                return {"elementType": element, "count": count} if keep else None
            if kind not in formats:
                raise ValueError(f"Unsupported GGUF metadata type {kind}")
            return number(formats[kind])

        if stream.read(4) != b"GGUF":
            raise ValueError("Not GGUF")
        version = number("I")
        if version not in (2, 3):
            raise ValueError(f"Unsupported GGUF version {version}")
        tensors, count = number("Q"), number("Q")
        selected = {}
        for _ in range(count):
            key = string(True)
            keep = key.startswith("general.") or key in (
                "tokenizer.ggml.model", "tokenizer.chat_template", "gemma3.context_length"
            )
            item = value(number("I"), keep)
            if keep:
                selected[key] = item
        return {"version": version, "tensorCount": tensors, "metadata": selected}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", type=Path, required=True)
    args = parser.parse_args()
    project = args.project.resolve()
    manifest = json.loads((project / "BuildSupport/LocalStory/Gemma/ModelManifest.json").read_text(encoding="utf-8-sig"))
    model = project / "Tests/LocalStoryBenchmark/Models" / manifest["file"]
    package_names = ["torch", "transformers", "peft", "trl", "bitsandbytes", "datasets", "safetensors", "huggingface_hub"]
    packages = {name: importlib.util.find_spec(name) is not None for name in package_names}
    gpu = subprocess.run(
        ["nvidia-smi", "--query-gpu=name,memory.total,memory.free,driver_version,compute_cap", "--format=csv,noheader"],
        capture_output=True, text=True, timeout=15,
    )
    payload = {"path": str(model.relative_to(project)), "exists": model.is_file()}
    if model.is_file():
        digest = hashlib.sha256()
        with model.open("rb") as stream:
            for chunk in iter(lambda: stream.read(8 * 1024 * 1024), b""):
                digest.update(chunk)
        payload.update(bytes=model.stat().st_size, sha256=digest.hexdigest(), gguf=gguf_metadata(model))
        payload["matchesManifest"] = payload["bytes"] == manifest["bytes"] and payload["sha256"] == manifest["sha256"]
    hf_home = Path(os.environ.get("HF_HOME", str(Path.home() / ".cache/huggingface")))
    cache = Path(os.environ.get("HF_HUB_CACHE", str(hf_home / "hub")))
    # Inventory model folders only, never credentials or token files.
    bases = sorted(p.name for p in cache.glob("models--google--gemma-3-4b*") if p.is_dir()) if cache.is_dir() else []
    output = {
        "schemaVersion": 1, "checkedDateSeoul": "2026-10-29", "pythonVersion": sys.version,
        "interpreter": sys.executable, "packagePresenceInThisInterpreter": packages,
        "gpuQuery": {"returnCode": gpu.returncode, "stdout": gpu.stdout.strip(), "stderr": gpu.stderr.strip()},
        "installedInferencePayload": payload, "localHuggingFaceBaseModelFolders": bases,
        "baseModelCacheCoverage": "standard Hugging Face cache only; other locations not ruled out",
        "officialLinkedHalfPrecisionCandidate": "google/gemma-3-4b-it",
        "exactTrainingRevision": None,
        "sources": ["https://huggingface.co/google/gemma-3-4b-it-qat-q4_0-gguf", "https://huggingface.co/google/gemma-3-4b-it", "https://ai.google.dev/gemma/docs/tune"],
        "compatibility": {
            "qatGgufIsTrainingCheckpoint": False,
            "adapterOnQatGgufVerified": False,
            "mergedConversionVerified": False,
            "runtimeLoraApplicationVerified": False,
        },
        "trainingExecuted": False, "downloadExecuted": False, "cudaTrainingSmokeTest": "not run",
        "blockers": [
            "Training packages absent in inspected interpreter" if not packages["torch"] else "Training package compatibility not yet validated",
            "Trainable checkpoint and exact revision not acquired or pinned",
            "Validation/final-scene meaning labels incomplete",
            "6GB peak-memory feasibility requires actual load/backward/save smoke test",
            "QAT baseline versus non-QAT half-precision candidate requires an untrained conversion control before attributing gains to LoRA",
        ],
    }
    target = project / "Tests/LocalStoryBenchmark/TrainingEnvironment.json"
    target.write_text(json.dumps(output, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"output": str(target), "modelMatchesManifest": payload.get("matchesManifest"), "gpu": gpu.stdout.strip(), "packages": packages, "trainingExecuted": False}, ensure_ascii=False))


if __name__ == "__main__":
    main()
