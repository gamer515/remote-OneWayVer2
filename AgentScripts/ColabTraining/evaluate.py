"""Read-only paired generation on exported unseen game inputs. Never trains or applies to game."""
import argparse
from contextlib import nullcontext
import hashlib
import html
import importlib.metadata
import json
from pathlib import Path
import time


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def write(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".partial")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def structure(raw, case):
    try:
        output = json.loads(raw)
        if not isinstance(output, dict) or set(output) != {"cards"} or not isinstance(output["cards"], list):
            raise ValueError("Expected one cards object")
        cards = output["cards"]
        if len(cards) != case["count"]:
            raise ValueError("Card count mismatch")
        for index, (card, original) in enumerate(zip(cards, case["sourceCards"])):
            if set(card) != {"index", "text", "options"} or type(card["index"]) is not int or card["index"] != index:
                raise ValueError("Card fields/index mismatch")
            if not isinstance(card["text"], str) or not card["text"].strip() or len(card["text"]) > case["limits"]["textMaxChars"]:
                raise ValueError("Text type/length mismatch")
            if not isinstance(card["options"], list) or len(card["options"]) != len(original["options"]):
                raise ValueError("Option count mismatch")
            if any(not isinstance(option, str) or not option.strip() or len(option) > case["limits"]["optionMaxChars"] for option in card["options"]):
                raise ValueError("Option type/length mismatch")
        return output, None
    except (ValueError, TypeError, KeyError) as error:
        return None, str(error)


def report(result, target):
    escaped = lambda value: html.escape(str(value))
    def cards_view(cards):
        return "".join("<div class=card><b>" + escaped(card["index"] + 1) + "</b> " + escaped(card["text"]) +
            ("<ol>" + "".join("<li>" + escaped(option) + "</li>" for option in card["options"]) + "</ol>" if card["options"] else "") + "</div>" for card in cards)
    requests = {(r["caseId"], r["seed"], r["arm"]): r for r in result["requests"]}
    sections = []
    for case in result["inputs"]["cases"]:
        for seed in result["inputs"]["plan"]["seeds"]:
            columns = ["<article><h3>기존 원문 · 의미 근거</h3>" + cards_view(case["sourceCards"]) +
                       "<p class=warn>Explanation 세부 의미: " + ("있음" if case["meaningCoverageComplete"] else "미작성 항목 있음 · 원문 보조 참조") + "</p></article>"]
            for arm, title in (("base", "학습 전 원본"), ("adapter", "학습 후 LoRA")):
                row = requests.get((case["id"], seed, arm))
                if row is None:
                    content = "미실행"
                else:
                    content = "<p>클라우드 생성 " + f"{row['seconds']:.2f}" + "초 · 출력 " + str(row["outputTokens"]) + "토큰</p>"
                    content += "<p>JSON 형식: " + ("통과" if row["structureError"] is None else escaped(row["structureError"])) + "</p>"
                    content += cards_view(row["parsed"]["cards"]) if row["parsed"] else "<pre>" + escaped(row["raw"]) + "</pre>"
                    content += "<details><summary>실제 원시 출력</summary><pre>" + escaped(row["raw"]) + "</pre></details>"
                columns.append("<article><h3>" + title + "</h3>" + content + "</article>")
            sections.append("<section><h2>" + escaped(case["id"]) + " · seed " + str(seed) + "</h2><div class=grid>" + "".join(columns) + "</div></section>")
    document = """<!doctype html><html lang=ko><meta charset=utf-8><meta name=viewport content="width=device-width,initial-scale=1">
<title>OneWay 미학습 장면 비교</title><style>body{font:16px/1.7 system-ui,sans-serif;background:#f5f6f8;color:#202631;margin:24px auto;max-width:1500px;padding:0 20px}h1,h2,h3{line-height:1.4}.grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:16px}article{background:white;padding:20px;border:1px solid #dce1e7;border-radius:12px}.card{border-top:1px solid #e6e9ef;padding:14px 0}.warn{background:#fff1cd;padding:14px}pre{white-space:pre-wrap;overflow-wrap:anywhere;font-size:13px}section{margin:32px 0}@media(max-width:1000px){.grid{grid-template-columns:1fr}}</style>
<h1>미학습 장면 · 학습 전후 비교</h1><p class=warn>같은 HF 원본·NF4·토크나이저·입력·시드에서 LoRA 유무만 비교합니다. 게임 QAT GGUF와의 최종 비교 및 로컬 3분 성능 검사가 아닙니다. 두 장면의 세부 Explanation 의미는 미작성 항목이 있어 원문을 보조 근거로 표시합니다. 자동 형식 통과는 의미 품질 승인이 아닙니다.</p>"""
    document += "<p>모델: " + escaped(result["modelId"]) + " / " + escaped(result["modelRevision"]) + "</p>" + "".join(sections) + "</html>"
    Path(target).write_text(document, encoding="utf-8")


def run(bundle, run_dir):
    manifest = read(bundle / "evaluation-manifest.json")
    for name, digest in manifest["files"].items():
        path = (bundle / name).resolve()
        if not path.is_relative_to(bundle.resolve()) or sha(path) != digest:
            raise ValueError("Evaluation bundle changed: " + name)
    inputs = read(bundle / "EvaluationInputs.json")
    lock = read(run_dir / "experiment-lock.json")
    provenance = read(run_dir / "trial/adapter/provenance.json")
    if any(case["split"] not in ("validation", "test") or case["splitGroup"] in manifest["trainingGroups"] for case in inputs["cases"]):
        raise ValueError("Training scene used as unseen evaluation")
    if lock["bundleFiles"]["TrainingPairs.jsonl"] != manifest["trainingDatasetSha256"]:
        raise ValueError("Different training dataset")
    if provenance["modelId"] != lock["modelId"] or provenance["modelRevision"] != lock["modelRevision"]:
        raise ValueError("Adapter/base mismatch")
    for package, version in lock["packages"].items():
        if importlib.metadata.version(package) != version:
            raise ValueError("Training/evaluation environment mismatch: " + package)
    output_dir = run_dir / "evaluation" / inputs["plan"]["evaluationId"]
    eval_lock = {"manifest": manifest, "modelRevision": lock["modelRevision"], "adapterProvenance": provenance}
    lock_file = output_dir / "evaluation-lock.json"
    if lock_file.exists() and read(lock_file) != eval_lock:
        raise ValueError("Existing evaluation has different conditions; use a new evaluationId")
    write(lock_file, eval_lock)
    import torch
    from transformers import AutoTokenizer, BitsAndBytesConfig, Gemma3ForConditionalGeneration, set_seed
    from peft import PeftModel
    if not torch.cuda.is_available():
        raise RuntimeError("No CUDA GPU; stop without CPU fallback")
    dtype = torch.bfloat16 if torch.cuda.is_bf16_supported() else torch.float16
    if str(dtype) != read(run_dir / "trial/runtime.json")["computeDtype"]:
        raise ValueError("Evaluation compute precision differs from training")
    tokenizer = AutoTokenizer.from_pretrained(run_dir / "tokenizer")
    quant = BitsAndBytesConfig(load_in_4bit=True, bnb_4bit_quant_type="nf4", bnb_4bit_use_double_quant=True, bnb_4bit_compute_dtype=dtype)
    base = Gemma3ForConditionalGeneration.from_pretrained(lock["modelId"], revision=lock["modelRevision"],
        quantization_config=quant, torch_dtype=dtype, device_map={"": 0}, attn_implementation="eager")
    model = PeftModel.from_pretrained(base, run_dir / "trial/adapter", is_trainable=False)
    model.eval()
    requests = []
    plan = inputs["plan"]
    for case_index, case in enumerate(inputs["cases"]):
        prompt = tokenizer.apply_chat_template(case["messages"], tokenize=False, add_generation_prompt=True)
        for seed_index, seed in enumerate(plan["seeds"]):
            order = ("base", "adapter") if (case_index + seed_index) % 2 == 0 else ("adapter", "base")
            for arm in order:
                request_file = output_dir / "requests" / (case["id"].replace("/", "_") + f"-{seed}-{arm}.json")
                if request_file.exists():
                    requests.append(read(request_file))
                    continue
                set_seed(seed)
                torch.cuda.reset_peak_memory_stats()
                torch.cuda.synchronize()
                started = time.perf_counter()
                encoded = tokenizer(prompt, add_special_tokens=False, return_tensors="pt").to("cuda")
                input_tokens = encoded["input_ids"].shape[1]
                if input_tokens + case["maxNewTokens"] > lock["config"]["maxSequenceLength"]:
                    raise ValueError("Input/output budget exceeds frozen context; do not truncate")
                with torch.inference_mode(), model.disable_adapter() if arm == "base" else nullcontext():
                    generated = model.generate(**encoded, max_new_tokens=case["maxNewTokens"],
                        max_time=plan["maxGenerationSecondsPerRequest"], do_sample=True,
                        temperature=plan["sampling"]["temperature"], top_p=plan["sampling"]["top_p"],
                        top_k=plan["sampling"]["top_k"], min_p=plan["sampling"]["min_p"], use_cache=True)
                torch.cuda.synchronize()
                seconds = time.perf_counter() - started
                tokens = generated[0, input_tokens:]
                raw = tokenizer.decode(tokens, skip_special_tokens=True)
                parsed, error = structure(raw, case)
                row = {"caseId": case["id"], "seed": seed, "arm": arm, "raw": raw, "parsed": parsed,
                    "structureError": error, "inputTokens": input_tokens, "outputTokens": len(tokens), "seconds": seconds,
                    "tokenLimitReached": len(tokens) >= case["maxNewTokens"],
                    "timeBudgetReached": seconds >= plan["maxGenerationSecondsPerRequest"],
                    "peakAllocatedBytes": torch.cuda.max_memory_allocated(), "humanQualityAccepted": None}
                write(request_file, row)
                requests.append(row)
                del generated, encoded, tokens
                print(json.dumps({k: row[k] for k in ("caseId", "seed", "arm", "seconds", "outputTokens", "structureError")}, ensure_ascii=False), flush=True)
    result = {"modelId": lock["modelId"], "modelRevision": lock["modelRevision"], "inputs": inputs,
              "requests": requests, "trainingExecuted": False, "local180SecondTargetMeasured": False,
              "humanQualityAccepted": None}
    write(output_dir / "Comparison.json", result)
    report(result, output_dir / "Comparison.html")
    print(json.dumps({"completed": len(requests), "outputDirectory": str(output_dir), "qualityAccepted": None}, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", type=Path)
    parser.add_argument("--run", type=Path)
    parser.add_argument("--report-only", type=Path)
    parser.add_argument("--html", type=Path)
    args = parser.parse_args()
    if args.report_only:
        if not args.html:
            parser.error("--report-only requires --html")
        report(read(args.report_only), args.html)
    else:
        if not args.bundle or not args.run:
            parser.error("--bundle and --run required")
        run(args.bundle, args.run)
