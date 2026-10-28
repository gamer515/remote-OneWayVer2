"""Compare current/separated inputs on one unchanged NF4 base, without training."""
import argparse
import importlib.metadata
import json
from pathlib import Path
import time
from evaluate import read, write, sha, structure


def run(bundle, run_dir):
    manifest = read(bundle / "input-ab-manifest.json")
    for name, digest in manifest["files"].items():
        path = (bundle / name).resolve()
        if not path.is_relative_to(bundle.resolve()) or sha(path) != digest:
            raise ValueError("Input AB bundle changed: " + name)
    inputs = read(bundle / "InputSeparationInputs.json")
    if any(case["split"] != "validation" for case in inputs["cases"]):
        raise ValueError("Only validation scenes allowed")
    lock = read(run_dir / "experiment-lock.json")
    for package, version in lock["packages"].items():
        if importlib.metadata.version(package) != version:
            raise ValueError("Environment changed: " + package)
    output_dir = run_dir / "evaluation" / inputs["plan"]["evaluationId"]
    ab_lock = {"manifest": manifest, "baseLock": lock, "adapterEnabled": False}
    if (output_dir / "input-ab-lock.json").exists() and read(output_dir / "input-ab-lock.json") != ab_lock:
        raise ValueError("Different experiment already exists")
    write(output_dir / "input-ab-lock.json", ab_lock)
    import torch
    from transformers import AutoTokenizer, BitsAndBytesConfig, Gemma3ForConditionalGeneration, set_seed
    if not torch.cuda.is_available():
        raise RuntimeError("CUDA GPU required; no CPU fallback")
    dtype = torch.bfloat16 if torch.cuda.is_bf16_supported() else torch.float16
    if str(dtype) != read(run_dir / "trial/runtime.json")["computeDtype"]:
        raise ValueError("Compute dtype changed")
    tokenizer = AutoTokenizer.from_pretrained(run_dir / "tokenizer")
    quant = BitsAndBytesConfig(load_in_4bit=True, bnb_4bit_quant_type="nf4", bnb_4bit_use_double_quant=True, bnb_4bit_compute_dtype=dtype)
    model = Gemma3ForConditionalGeneration.from_pretrained(lock["modelId"], revision=lock["modelRevision"], quantization_config=quant, torch_dtype=dtype, device_map={"": 0}, attn_implementation="eager")
    model.eval()
    rows = []
    plan = inputs["plan"]
    for case_index, case in enumerate(inputs["cases"]):
        for seed_index, seed in enumerate(plan["seeds"]):
            arms = ("current", "separated") if (case_index + seed_index) % 2 == 0 else ("separated", "current")
            for arm in arms:
                target = output_dir / "requests" / (case["id"].replace("/", "_") + f"-{seed}-{arm}.json")
                if target.exists():
                    rows.append(read(target))
                    continue
                set_seed(seed)
                prompt = tokenizer.apply_chat_template(case["messagesByArm"][arm], tokenize=False, add_generation_prompt=True)
                torch.cuda.synchronize()
                started = time.perf_counter()
                encoded = tokenizer(prompt, add_special_tokens=False, return_tensors="pt").to("cuda")
                count = encoded["input_ids"].shape[1]
                if count + case["maxNewTokens"] > lock["config"]["maxSequenceLength"]:
                    raise ValueError("Context budget exceeded; no truncation")
                with torch.inference_mode():
                    generated = model.generate(**encoded, max_new_tokens=case["maxNewTokens"], max_time=plan["maxGenerationSecondsPerRequest"], do_sample=True, use_cache=True, **plan["sampling"])
                torch.cuda.synchronize()
                seconds = time.perf_counter() - started
                tokens = generated[0, count:]
                raw = tokenizer.decode(tokens, skip_special_tokens=True)
                parsed, error = structure(raw, case)
                row = {"caseId": case["id"], "seed": seed, "arm": arm, "raw": raw, "parsed": parsed, "structureError": error,
                    "inputTokens": count, "outputTokens": len(tokens), "seconds": seconds, "tokenLimitReached": len(tokens) >= case["maxNewTokens"],
                    "timeBudgetReached": seconds >= plan["maxGenerationSecondsPerRequest"], "humanQualityAccepted": None}
                write(target, row)
                rows.append(row)
                print(json.dumps({k: row[k] for k in ("caseId", "seed", "arm", "seconds", "structureError")}), flush=True)
                del generated, encoded, tokens
    result = {"inputs": inputs, "requests": rows, "trainingExecuted": False, "adapterEnabled": False,
        "local180SecondTargetMeasured": False, "humanQualityAccepted": None}
    write(output_dir / "InputComparison.json", result)
    print(json.dumps({"completed": len(rows), "outputDirectory": str(output_dir)}), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--run", type=Path, required=True)
    args = parser.parse_args()
    run(args.bundle, args.run)
