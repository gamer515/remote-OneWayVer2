"""Package only the observed held-out inputs and paired evaluator; no model or secrets."""
import argparse
import base64
import hashlib
import io
import json
from pathlib import Path
import zipfile


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", type=Path, required=True)
    args = parser.parse_args()
    project = args.project.resolve()
    root = project / "Tests/LocalStoryBenchmark"
    inputs = json.loads((root / "Colab/EvaluationInputs.json").read_text(encoding="utf-8-sig"))
    pairs = [json.loads(line) for line in (root / "TrainingPairs.jsonl").read_text(encoding="utf-8-sig").splitlines() if line.strip()]
    groups = sorted({row["splitGroup"] for row in pairs})
    if any(case["splitGroup"] in groups or case["split"] not in ("validation", "test") for case in inputs["cases"]):
        raise ValueError("Held-out scene grouping invalid")
    payload = {"EvaluationInputs.json": (root / "Colab/EvaluationInputs.json").read_bytes(), "evaluate.py": Path(__file__).with_name("evaluate.py").read_bytes()}
    manifest = {"files": {name: hashlib.sha256(raw).hexdigest() for name, raw in payload.items()}, "trainingGroups": groups,
                "trainingDatasetSha256": hashlib.sha256((root / "TrainingPairs.jsonl").read_bytes()).hexdigest()}
    payload["evaluation-manifest.json"] = json.dumps(manifest, indent=2).encode()
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name, raw in payload.items():
            archive.writestr(name, raw)
    data = stream.getvalue()
    encoded = base64.b64encode(data).decode()
    code = f'''import base64, hashlib, io, json, pathlib, zipfile
EVAL_BUNDLE = pathlib.Path("/content/oneway_evaluation_bundle")
evaluation_payload = base64.b64decode("{encoded}")
assert hashlib.sha256(evaluation_payload).hexdigest() == "{hashlib.sha256(data).hexdigest()}"
EVAL_BUNDLE.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(io.BytesIO(evaluation_payload)) as archive:
    for item in archive.infolist():
        target = (EVAL_BUNDLE / item.filename).resolve()
        if not target.is_relative_to(EVAL_BUNDLE.resolve()):
            raise ValueError("Invalid evaluation path")
        target.write_bytes(archive.read(item))
run_command([sys.executable, str(EVAL_BUNDLE / "evaluate.py"), "--bundle", str(EVAL_BUNDLE), "--run", str(RUN)])
'''
    compile(code, "evaluation-cell", "exec")
    compile(payload["evaluate.py"].decode(), "evaluate.py", "exec")
    output = project / "outputs/AiTraining"
    output.mkdir(parents=True, exist_ok=True)
    (output / "evaluation-cell.py").write_text(code, encoding="utf-8")
    (output / "OneWay_Evaluation_Bundle.zip").write_bytes(data)
    print(json.dumps({"sections": len(inputs["cases"]), "generations": len(inputs["cases"])*len(inputs["plan"]["seeds"])*2,
                      "bytes": len(data), "cellFile": str(output / "evaluation-cell.py")}, ensure_ascii=False))


if __name__ == "__main__":
    main()
