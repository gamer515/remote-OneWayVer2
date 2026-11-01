"""Build a public correction-only experiment; never alters the game training bundle."""
import base64
import hashlib
import io
import json
from pathlib import Path
import zipfile

root = Path(__file__).resolve().parents[2]
config_dir = root / "Tests/LocalStoryBenchmark/Colab"
data_dir = root / "outputs/AiTraining/PublicDataReview/KoLLA-16908784"
manifest = json.loads((config_dir / "PublicCorrectionSplitManifest.json").read_text(encoding="utf-8"))
raw = (data_dir / "reviewed-split-candidates.jsonl").read_bytes()
assert hashlib.sha256(raw).hexdigest() == manifest["reviewedDatasetSha256"]
records = [json.loads(line) for line in raw.decode().splitlines()]
instruction = "다음 문장의 뜻과 말투를 유지하면서 맞춤법·띄어쓰기·문법 오류만 고쳐줘. 사실을 추가하지 말고 교정한 문장만 출력해.\n문장: "
rows = [{"id": row["id"], "splitGroup": row["splitGroup"], "messages": [{"role": "user", "content": instruction + row["sourceText"]}, {"role": "assistant", "content": row["targetText"]}]} for row in records if row["split"] == "train"]
config = json.loads((config_dir / "experiment.json").read_text(encoding="utf-8"))
config.update(experimentId="public-korean-correction-v1", modelRevision="093f9f388b31de276ce2de164bdc2081324b9767", purpose="One-epoch public spelling/grammar pilot, isolated from game training and deployment", maxSequenceLength=1024, trialSteps=33, saveSteps=11)
lock = {**manifest, "instruction": instruction, "records": records,
    "curationAuthorization": "User requested public-data selection and execution with whole-process N/5 progress on 2026-10-29",
    "approvalKind": "Published human correction consensus plus Codex meaning review; no claim of per-pair user approval",
    "scope": "Research pilot only; no game deployment or model distribution"}
payload = {"TrainingPairs.jsonl": ("".join(json.dumps(row, ensure_ascii=False) + "\n" for row in rows)).encode(),
    "experiment.json": json.dumps(config, ensure_ascii=False, indent=2).encode(), "PublicCorpusLock.json": json.dumps(lock, ensure_ascii=False, indent=2).encode(),
    "LICENSE-KoLLA.txt": (data_dir / "LICENSE").read_bytes(), "README-KoLLA.txt": (data_dir / "README").read_bytes(),
    "NOTICE.txt": ("Source: https://zenodo.org/records/16908784 and https://cl.indiana.edu/~kolla/Resources/kolla.tgz\nCreators: Sun-Hee Lee, Markus Dickinson, Ross Israel; KoLLA v2: Jungyeul Park, Jayoung Song, KyungTae Lim.\nChanges: reconstructed M2 references, normalized punctuation spacing, consensus/meaning filtering, matched original essays, grouped split.\nLicense: GPL-3.0-or-later per Zenodo metadata; original GPLv3 included. No original learner metadata uploaded.\n").encode()}
for name in ("train.py", "public_train.py"):
    payload[name] = Path(__file__).with_name(name).read_bytes()
payload["bundle-manifest.json"] = json.dumps({"files": {name: hashlib.sha256(value).hexdigest() for name, value in payload.items()}}, indent=2).encode()
stream = io.BytesIO()
with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_DEFLATED) as archive:
    for name, value in payload.items():
        archive.writestr(name, value)
data = stream.getvalue()
output = root / "outputs/AiTraining"
(output / "OneWay_PublicCorrection_Bundle.zip").write_bytes(data)
code = f'''# Whole process 2/5: public correction-only pilot; no game application
import base64, hashlib, io, json, pathlib, sys, zipfile
PUBLIC_BUNDLE=pathlib.Path("/content/oneway_public_correction")
public_payload=base64.b64decode("{base64.b64encode(data).decode()}")
assert hashlib.sha256(public_payload).hexdigest()=="{hashlib.sha256(data).hexdigest()}"
PUBLIC_BUNDLE.mkdir(parents=True,exist_ok=True)
with zipfile.ZipFile(io.BytesIO(public_payload)) as archive:
    for item in archive.infolist():
        target=(PUBLIC_BUNDLE/item.filename).resolve()
        if not target.is_relative_to(PUBLIC_BUNDLE.resolve()): raise ValueError("Invalid bundle path")
        target.write_bytes(archive.read(item))
PUBLIC_RUN=RUN.parent / "public-korean-correction-v1"
for public_mode in ("validate", "prepare", "smoke", "train"):
    stage="smoke" if public_mode=="smoke" else "trial"
    completed=PUBLIC_RUN/stage/"completed.json"
    if public_mode in ("smoke","train") and completed.exists():
        proof=json.loads(completed.read_text())
        bundle_files=json.loads((PUBLIC_BUNDLE/"bundle-manifest.json").read_text())["files"]
        if proof["bundleFiles"]!=bundle_files: raise ValueError("Existing run mismatch")
        continue
    run_command([sys.executable,str(PUBLIC_BUNDLE/"public_train.py"),public_mode,"--bundle",str(PUBLIC_BUNDLE),"--run",str(PUBLIC_RUN)])
print("PUBLIC_TRAIN_RESULT_BEGIN")
print((PUBLIC_RUN/"trial/completed.json").read_text())
print("PUBLIC_TRAIN_RESULT_END")
'''
compile(code, "public-training-cell", "exec")
(output / "public-training-cell.py").write_text(code, encoding="utf-8")
print(json.dumps({"train": len(rows), "validation": manifest["splitCounts"]["validation"], "test": manifest["splitCounts"]["test"], "trialSteps": config["trialSteps"], "bytes": len(data)}))
