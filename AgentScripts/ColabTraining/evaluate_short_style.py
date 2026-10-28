"""Diagnostic: one authored sentence, short tone-only prompt, unchanged HF base."""
import argparse
import importlib.metadata
import json
from pathlib import Path
import time
from evaluate import read, write, sha


def run(bundle, run_dir):
    plan = read(bundle / "ShortStylePlan.json")
    lock = read(run_dir / "experiment-lock.json")
    for package, version in lock["packages"].items():
        if importlib.metadata.version(package) != version:
            raise ValueError("Environment changed: " + package)
    import torch
    from transformers import AutoTokenizer, BitsAndBytesConfig, Gemma3ForConditionalGeneration, set_seed
    if not torch.cuda.is_available():
        raise RuntimeError("CUDA GPU required")
    dtype = torch.bfloat16 if torch.cuda.is_bf16_supported() else torch.float16
    if str(dtype) != read(run_dir / "trial/runtime.json")["computeDtype"]:
        raise ValueError("Compute precision changed")
    output = run_dir / "evaluation" / plan["evaluationId"]
    stamp = {"planSha256": sha(bundle / "ShortStylePlan.json"), "scriptSha256": sha(Path(__file__)), "baseLock": lock}
    if (output / "lock.json").exists() and read(output / "lock.json") != stamp:
        raise ValueError("Experiment conditions changed")
    write(output / "lock.json", stamp)
    tokenizer = AutoTokenizer.from_pretrained(run_dir / "tokenizer")
    model = Gemma3ForConditionalGeneration.from_pretrained(lock["modelId"], revision=lock["modelRevision"],
        quantization_config=BitsAndBytesConfig(load_in_4bit=True, bnb_4bit_quant_type="nf4", bnb_4bit_use_double_quant=True, bnb_4bit_compute_dtype=dtype),
        torch_dtype=dtype, device_map={"": 0}, attn_implementation="eager")
    model.eval()
    rows = []
    for seed in plan["seeds"]:
        set_seed(seed)
        prompt = tokenizer.apply_chat_template(plan["messages"], tokenize=False, add_generation_prompt=True)
        encoded = tokenizer(prompt, add_special_tokens=False, return_tensors="pt").to("cuda")
        count = encoded["input_ids"].shape[1]
        if count + plan["maxNewTokens"] > lock["config"]["maxSequenceLength"]:
            raise ValueError("Context exceeded")
        torch.cuda.synchronize()
        started = time.perf_counter()
        with torch.inference_mode():
            generated = model.generate(**encoded, max_new_tokens=plan["maxNewTokens"], max_time=180, do_sample=True, use_cache=True, **plan["sampling"])
        torch.cuda.synchronize()
        seconds = time.perf_counter() - started
        tokens = generated[0, count:]
        row = {"seed": seed, "raw": tokenizer.decode(tokens, skip_special_tokens=True), "inputTokens": count,
            "outputTokens": len(tokens), "seconds": seconds, "tokenLimitReached": len(tokens) >= plan["maxNewTokens"], "timeBudgetReached": seconds >= 180}
        rows.append(row)
        write(output / (str(seed) + ".json"), row)
    result = {"plan": plan, "requests": rows, "modelId": lock["modelId"], "modelRevision": lock["modelRevision"],
        "computeDtype": str(dtype), "adapterEnabled": False, "trainingExecuted": False,
        "local180SecondTargetMeasured": False, "limitations": ["Plain text one-sentence task differs from multi-card JSON task; success cannot isolate prompt length as cause", "HF NF4 base differs from installed QAT GGUF"]}
    write(output / "ShortStyleResult.json", result)
    print("SHORT_STYLE_RESULT_BEGIN", flush=True)
    print(json.dumps(result, ensure_ascii=False), flush=True)
    print("SHORT_STYLE_RESULT_END", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--run", type=Path, required=True)
    args = parser.parse_args()
    run(args.bundle, args.run)
