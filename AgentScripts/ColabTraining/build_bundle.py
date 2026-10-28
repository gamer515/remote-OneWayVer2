"""Build one uploadable, self-contained Colab notebook from existing approved data."""
import argparse
import base64
import hashlib
import io
import json
from pathlib import Path
import zipfile


def cell(kind, source):
    result = {"cell_type": kind, "metadata": {}, "source": source.splitlines(keepends=True)}
    if kind == "code":
        result.update(execution_count=None, outputs=[])
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", type=Path, required=True)
    args = parser.parse_args()
    project = args.project.resolve()
    data = project / "Tests/LocalStoryBenchmark"
    colab = data / "Colab"
    sources = {name: data / name for name in ("TrainingPairs.jsonl", "CurrentInputCandidates.json", "DatasetSplitPlan.json", "TrainingBaseline.json")}
    sources.update({"train.py": Path(__file__).with_name("train.py"), "experiment.json": colab / "experiment.json",
                    "requirements.txt": colab / "requirements.txt"})
    payload = {name: path.read_bytes() for name, path in sources.items()}
    manifest = {"schemaVersion": 1, "files": {name: hashlib.sha256(raw).hexdigest() for name, raw in payload.items()},
                "includesModels": False, "includesTokens": False, "includesPersonalSaves": False}
    payload["bundle-manifest.json"] = json.dumps(manifest, indent=2).encode()
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name, raw in payload.items():
            info = zipfile.ZipInfo(name, date_time=(2026, 10, 29, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, raw)
    raw_zip = stream.getvalue()
    encoded = base64.b64encode(raw_zip).decode()
    expected = hashlib.sha256(raw_zip).hexdigest()
    cells = [cell("markdown", """# OneWay — Colab 소규모 학습 시험

런타임 → 런타임 유형 변경 → GPU를 선택한 뒤 위에서부터 실행하세요.
이 노트북에 현재 승인·정렬된 6개 입력/정답과 실행 도구가 포함되어 있습니다. 저장소 복제는 필요 없습니다.

발표용 게임 설정은 바꾸지 않습니다. 2단계 시험 → 12단계 소규모 실험 순서입니다.
GPU 실행은 아직 검증 전이며 실패/OOM은 실패로 기록합니다. 여섯 자료의 학습만으로 품질 개선을 주장하지 않습니다.
모델 이용 조건은 본인 계정으로 동의해야 합니다: https://huggingface.co/google/gemma-3-4b-it
Colab 보안 비밀(열쇠 아이콘)에 접근 권한이 있는 읽기용 `HF_TOKEN`을 등록하세요. 토큰을 코드나 출력에 적지 마세요.
"""), cell("code", f'''import base64, hashlib, io, json, pathlib, zipfile
BUNDLE = pathlib.Path("/content/oneway_training_bundle")
payload = base64.b64decode("{encoded}")
assert hashlib.sha256(payload).hexdigest() == "{expected}"
BUNDLE.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(io.BytesIO(payload)) as archive:
    for item in archive.infolist():
        target = (BUNDLE / item.filename).resolve()
        if not target.is_relative_to(BUNDLE.resolve()):
            raise ValueError("Invalid bundle path")
        target.write_bytes(archive.read(item))
print("승인 자료와 도구 준비 완료")
'''), cell("markdown", """## 도구 설치

고정한 도구 버전 후보를 설치합니다. Colab의 CUDA용 PyTorch를 사용하며 실제 버전을 실행 기록에 고정합니다.
이미 다른 학습 패키지를 불러온 세션이라면 새 런타임으로 시작하세요. 설치/모델 다운로드에는 네트워크가 필요합니다.
"""), cell("code", '''import subprocess, sys
def run_command(command):
    process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, bufsize=1)
    for line in process.stdout:
        print(line, end="", flush=True)
    if process.wait():
        raise subprocess.CalledProcessError(process.returncode, command)
run_command([sys.executable, "-m", "pip", "install", "-q", "-r", str(BUNDLE / "requirements.txt")])
# Colab includes unrelated applications with different dependency constraints.
# Check the actual training stack, and retain the global report as diagnostic evidence.
import importlib.metadata as metadata
from packaging.requirements import Requirement
roots = [Requirement(line) for line in (BUNDLE / "requirements.txt").read_text().splitlines()
         if line.strip() and not line.startswith("#")]
for root in roots:
    assert metadata.version(root.name) in root.specifier, str(root)
    for dependency in metadata.requires(root.name) or []:
        requirement = Requirement(dependency)
        if requirement.marker is None or requirement.marker.evaluate({"extra": ""}):
            assert metadata.version(requirement.name) in requirement.specifier, str(requirement)
diagnostic = subprocess.run([sys.executable, "-m", "pip", "check"], capture_output=True, text=True)
(BUNDLE / "global-pip-check.txt").write_text(diagnostic.stdout + diagnostic.stderr)
run_command([sys.executable, "-c", "import torch, transformers, peft, bitsandbytes; from transformers import Gemma3ForConditionalGeneration; print('Training stack imports OK; torch=' + torch.__version__)"])
'''), cell("markdown", """## GPU·계정·중간 저장 위치

Drive 연결은 본인 계정의 동의 창에서 진행합니다. 모든 체크포인트는 아래 RUN 위치에 직접 저장합니다.
같은 시험을 재개할 때 RUN을 유지하고, 새로운 실험은 새 폴더명을 쓰세요.
"""), cell("code", '''import os
from google.colab import drive, userdata
run_command(["nvidia-smi", "--query-gpu=name,memory.total,memory.free", "--format=csv,noheader"])
drive.mount("/content/drive")
try:
    os.environ["HF_TOKEN"] = userdata.get("HF_TOKEN")
except Exception:
    raise RuntimeError("Colab 보안 비밀에 HF_TOKEN을 등록하고 이 노트북의 접근을 허용하세요.") from None
RUN = pathlib.Path("/content/drive/MyDrive/OneWayTraining/experiment-001")
def run_training(mode, *extra):
    run_command([sys.executable, str(BUNDLE / "train.py"), mode,
                 "--bundle", str(BUNDLE), "--run", str(RUN), *map(str, extra)])
run_training("validate")
'''), cell("markdown", """## 기반 모델 고정·입력 길이 확인

최초 실행 때 모델의 정확한 commit을 고정합니다. 현재 실행용 QAT GGUF 자체를 학습하지 않습니다.
템플릿을 적용한 실제 토큰 길이를 확인하며 4096 토큰을 넘으면 자르지 않고 중단합니다.
"""), cell("code", '''run_training("prepare")
print((RUN / "token-lengths.json").read_text())
'''), cell("markdown", """## 먼저 2단계 실행 시험

모델 적재·역전파·유한한 gradient·체크포인트 저장·어댑터 재로딩을 확인합니다.
중단되거나 메모리가 부족하면 여기서 멈추고 `last-failure.json`을 확인하세요. 설정을 몰래 축소하지 않습니다.
"""), cell("code", '''run_training("smoke")
'''), cell("markdown", """## 12단계 소규모 실험

바로 위 시험이 성공해야 실행됩니다. 시험에서 업데이트한 어댑터를 누적하지 않고 같은 원본에서 새로 시작합니다.
성공해도 게임에 적용하지 않습니다. validation/test 의미 기준은 아직 완성되지 않았습니다.
"""), cell("code", '''run_training("train")
'''), cell("markdown", """## 세션이 중단되었을 때: 정확한 상태 재개

새 세션에서는 위의 자료 복원·설치·Drive 연결·prepare까지만 다시 실행하고 아래 셀을 사용하세요.
`RUN`은 원래 경로여야 합니다. 패키지·입력·정밀도와 checkpoint의 optimizer/scheduler/RNG 상태를 확인합니다.
원래 목표 12단계를 넘겨 추가 학습하는 기능은 아닙니다. 다른 GPU에서는 수치가 완전히 동일할 것을 보장하지 않습니다.
"""), cell("code", '''RESUME_INTERRUPTED_TRIAL = False
if RESUME_INTERRUPTED_TRIAL:
    checkpoints = sorted((RUN / "trial").glob("checkpoint-*"), key=lambda p: int(p.name.split("-")[-1]), reverse=True)
    checkpoint = next((p for p in checkpoints if all((p / name).is_file() for name in
        ("trainer_state.json", "optimizer.pt", "scheduler.pt", "adapter_config.json", "adapter_model.safetensors"))
        and list(p.glob("rng_state*.pth"))), None)
    if checkpoint is None:
        raise RuntimeError("재개 가능한 완성된 체크포인트가 없습니다.")
    run_training("resume", "--checkpoint", checkpoint)
'''), cell("markdown", """## 로컬로 가져갈 파일

Drive의 experiment-001 전체를 내려받으면 원본 commit·토큰 길이·어댑터·재개용 체크포인트·메모리 기록이 포함됩니다.
어댑터만 가져와 추가 학습하면 새 실험(warmstart)이며 optimizer를 새로 시작합니다. 로컬 6GB에서의 가능 여부는 별도 시험합니다.
실제 게임 변환·양자화·미학습 장면 평가·3분 생성 검증은 아직 수행하지 않았습니다.
""")]
    notebook = {"nbformat": 4, "nbformat_minor": 5, "metadata": {"colab": {"name": "OneWay_Colab_Training.ipynb"},
                "accelerator": "GPU", "kernelspec": {"display_name": "Python 3", "name": "python3"},
                "language_info": {"name": "python"}}, "cells": cells}
    # Stable cell IDs keep notebook validation and version-control diffs predictable.
    for index, item in enumerate(cells):
        item["id"] = f"oneway-{index:02d}"
    (colab / "OneWay_Colab_Training.ipynb").write_text(json.dumps(notebook, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    output = project / "outputs/AiTraining"
    output.mkdir(parents=True, exist_ok=True)
    (output / "OneWay_Colab_Bundle.zip").write_bytes(raw_zip)
    (colab / "bundle-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"notebook": str(colab / "OneWay_Colab_Training.ipynb"), "bundle": str(output / "OneWay_Colab_Bundle.zip"), "payloadBytes": len(raw_zip)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
