"""Paired held-out public-correction evaluation. Final test remains unopened by default."""
import argparse
import importlib.metadata
import json
from pathlib import Path
import time
import train as engine


def distance(a, b):
    previous = list(range(len(b) + 1))
    for i, left in enumerate(a, 1):
        current = [i]
        for j, right in enumerate(b, 1):
            current.append(min(current[-1] + 1, previous[j] + 1, previous[j-1] + (left != right)))
        previous = current
    return previous[-1]


def run(bundle, run_dir):
    engine.verify_bundle(bundle)
    lock = engine.read_json(run_dir / "experiment-lock.json")
    for package, version in lock["packages"].items():
        if importlib.metadata.version(package) != version:
            raise ValueError("Environment changed: " + package)
    data = engine.read_json(bundle / "PublicCorpusLock.json")
    cases = [row for row in data["records"] if row["split"] == "validation"]
    assert len(cases) == data["splitCounts"]["validation"]
    folder = run_dir / "evaluation/validation-v1"
    stamp = {"scriptSha256": engine.sha(Path(__file__)), "experimentLock": lock, "split": "validation", "doSample": False, "maxNewTokens": 128}
    if (folder / "lock.json").exists() and engine.read_json(folder / "lock.json") != stamp:
        raise ValueError("Different evaluation already exists")
    engine.write_json(folder / "lock.json", stamp)
    import torch
    from transformers import AutoModelForCausalLM, AutoTokenizer, BitsAndBytesConfig, Gemma3ForConditionalGeneration, set_seed
    from peft import PeftModel
    if not torch.cuda.is_available():
        raise RuntimeError("CUDA GPU required")
    dtype = torch.bfloat16 if torch.cuda.is_bf16_supported() else torch.float16
    assert str(dtype) == engine.read_json(run_dir / "trial/runtime.json")["computeDtype"]
    tokenizer = AutoTokenizer.from_pretrained(run_dir / "tokenizer")
    loaders = {"Gemma3ForConditionalGeneration": Gemma3ForConditionalGeneration, "AutoModelForCausalLM": AutoModelForCausalLM}
    loader = loaders[lock["config"].get("modelLoader", "Gemma3ForConditionalGeneration")]
    base = loader.from_pretrained(lock["modelId"], revision=lock["modelRevision"], torch_dtype=dtype,
        quantization_config=BitsAndBytesConfig(load_in_4bit=True, bnb_4bit_quant_type="nf4", bnb_4bit_use_double_quant=True, bnb_4bit_compute_dtype=dtype), device_map={"":0}, attn_implementation="eager")
    model = PeftModel.from_pretrained(base, run_dir / "trial/adapter", is_trainable=False)
    model.eval()
    rows = []
    from contextlib import nullcontext
    for index, case in enumerate(cases):
        for arm in (("base", "adapter") if index % 2 == 0 else ("adapter", "base")):
            path = folder / "requests" / (case["id"] + "-" + arm + ".json")
            if path.exists():
                rows.append(engine.read_json(path))
                continue
            set_seed(12345)
            prompt = tokenizer.apply_chat_template([{"role":"user", "content":data["instruction"] + case["sourceText"]}], tokenize=False, add_generation_prompt=True)
            encoded = tokenizer(prompt, add_special_tokens=False, return_tensors="pt").to("cuda")
            count = encoded["input_ids"].shape[1]
            assert count + 128 <= lock["config"]["maxSequenceLength"]
            torch.cuda.synchronize()
            started = time.perf_counter()
            with torch.inference_mode(), model.disable_adapter() if arm == "base" else nullcontext():
                generated = model.generate(**encoded, max_new_tokens=128, max_time=180, do_sample=False, use_cache=True)
            torch.cuda.synchronize()
            seconds = time.perf_counter() - started
            tokens = generated[0, count:]
            raw = tokenizer.decode(tokens, skip_special_tokens=True)
            prediction = raw.strip()
            row = {"id":case["id"], "splitGroup":case["splitGroup"], "arm":arm, "source":case["sourceText"], "target":case["targetText"], "raw":raw,
                "exactTarget":prediction == case["targetText"], "targetEditDistance":distance(prediction, case["targetText"]),
                "inputTargetEditDistance":distance(case["sourceText"], case["targetText"]), "seconds":seconds,
                "inputTokens":count, "outputTokens":len(tokens), "tokenLimitReached":len(tokens)>=128, "timeBudgetReached":seconds>=180}
            engine.write_json(path, row)
            rows.append(row)
            print(json.dumps({"id":row["id"],"arm":arm,"exactTarget":row["exactTarget"],"seconds":seconds}), flush=True)
            del encoded, generated, tokens
    metrics = {arm: {"count":sum(row["arm"]==arm for row in rows), "exactTargetCount":sum(row["exactTarget"] for row in rows if row["arm"]==arm),
        "totalTargetEditDistance":sum(row["targetEditDistance"] for row in rows if row["arm"]==arm)} for arm in ("base","adapter")}
    result = {"stage":"2/5", "split":"validation", "metrics":metrics, "requests":rows, "finalTestEvaluated":False,
        "meaningQualityAccepted":None, "gameApplied":False, "local180SecondTargetMeasured":False,
        "limitations":["Exact target and character distance are proxies; other correct rewrites may differ", "One small learner-correction pilot does not establish general Korean or game improvement"]}
    engine.write_json(folder / "PublicComparison.json", result)
    print("PUBLIC_EVAL_RESULT_BEGIN", flush=True)
    print(json.dumps(result, ensure_ascii=False), flush=True)
    print("PUBLIC_EVAL_RESULT_END", flush=True)


if __name__ == "__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("--bundle",type=Path,required=True)
    parser.add_argument("--run",type=Path,required=True)
    args=parser.parse_args()
    run(args.bundle,args.run)
