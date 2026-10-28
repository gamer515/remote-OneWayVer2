"""Portable Colab/local feasibility runner; NPC facts come only from exported data."""
import argparse
from datetime import datetime, timezone
import hashlib
import importlib.metadata
import json
import math
from pathlib import Path
import re
import sys
import time


PACKAGES = ("torch", "transformers", "peft", "accelerate", "bitsandbytes", "huggingface-hub", "safetensors")


def read_json(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".partial")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def verify_bundle(folder):
    manifest = read_json(folder / "bundle-manifest.json")
    for name, expected in manifest["files"].items():
        path = (folder / name).resolve()
        if not path.is_relative_to(folder.resolve()) or not path.is_file() or sha(path) != expected:
            raise ValueError(f"Missing or changed bundle file: {name}")
    return manifest


def validate(folder):
    manifest = verify_bundle(folder)
    config = read_json(folder / "experiment.json")
    baseline = read_json(folder / config["baseline"])
    candidates = read_json(folder / config["candidates"])
    split = read_json(folder / config["splitPlan"])
    groups = {g["id"]: g for g in split["groups"]}
    entries = {e["id"]: e for e in candidates["entries"]}
    rows = [json.loads(line) for line in (folder / config["data"]).read_text(encoding="utf-8-sig").splitlines() if line.strip()]
    if not rows or len({r["id"] for r in rows}) != len(rows):
        raise ValueError("Empty or duplicate training records")
    if config["baselineId"] != baseline["baselineId"] or split["baselineId"] != baseline["baselineId"]:
        raise ValueError("Baseline identity mismatch")
    for row in rows:
        entry = entries[row["id"]]
        if not entry["trainingEligible"] or not entry["answerApprovedByUser"]:
            raise ValueError("Unapproved or excluded training record")
        if entry["semanticReview"]["status"] != "consistent_with_current_input_by_agent":
            raise ValueError("Unresolved meaning review")
        if not all(entry["validation"][k] for k in ("tryBuild", "sameBindings", "situation", "lengths")):
            raise ValueError("Game-format validation failed")
        if groups[row["splitGroup"]]["role"] != "train" or row["splitGroup"] != entry["splitGroup"]:
            raise ValueError("Held-out scene leaked into training")
        if row["messages"][:-1] != entry["messages"] or row["messages"][-1]["role"] != "assistant":
            raise ValueError("Input messages changed")
        answer = json.loads(row["messages"][-1]["content"])
        if answer != entry["expectedOutput"]:
            raise ValueError("Answer changed after source review")
        cards = answer["cards"]
        if [c["index"] for c in cards] != list(range(len(cards))):
            raise ValueError("Output card indices changed")
    return config, rows, manifest


def package_versions():
    return {name: importlib.metadata.version(name) for name in PACKAGES}


def prepare(folder, run, revision_override=None):
    config, rows, manifest = validate(folder)
    from huggingface_hub import HfApi
    from transformers import AutoTokenizer

    run.mkdir(parents=True, exist_ok=True)
    lock_path = run / "experiment-lock.json"
    if lock_path.exists():
        lock = read_json(lock_path)
        if lock["bundleFiles"] != manifest["files"] or lock["config"] != config:
            raise ValueError("Existing experiment has different inputs. Use a new run directory.")
    else:
        revision = HfApi().model_info(config["modelId"], revision=config["modelRevision"] or revision_override or "main").sha
        if not re.fullmatch(r"[0-9a-f]{40}", revision):
            raise ValueError("Exact base-model revision unavailable")
        lock = {"config": config, "bundleFiles": manifest["files"], "modelId": config["modelId"],
                "modelRevision": revision, "packages": package_versions(), "createdUtc": datetime.now(timezone.utc).isoformat()}
        # Write before downloads so a retry never silently switches to a newer main.
        write_json(lock_path, lock)
    if lock["packages"] != package_versions():
        raise ValueError("Package versions differ from the experiment lock. Restore the recorded environment or create a new experiment.")
    tokenizer = AutoTokenizer.from_pretrained(lock["modelId"], revision=lock["modelRevision"])
    encoded, lengths = [], []
    for row in rows:
        prompt = tokenizer.apply_chat_template(row["messages"][:-1], tokenize=False, add_generation_prompt=True)
        full = tokenizer.apply_chat_template(row["messages"], tokenize=False, add_generation_prompt=False)
        prompt_ids = tokenizer(prompt, add_special_tokens=False)["input_ids"]
        ids = tokenizer(full, add_special_tokens=False)["input_ids"]
        if ids[:len(prompt_ids)] != prompt_ids or len(ids) <= len(prompt_ids):
            raise ValueError("Chat-template prefix differs: assistant-only labels cannot be established")
        if len(ids) > config["maxSequenceLength"]:
            raise ValueError(f"{row['id']}: {len(ids)} tokens exceeds {config['maxSequenceLength']}; do not truncate facts or answers")
        labels = [-100] * len(prompt_ids) + ids[len(prompt_ids):]
        encoded.append({"input_ids": ids, "attention_mask": [1] * len(ids), "labels": labels})
        lengths.append({"id": row["id"], "inputTokens": len(prompt_ids), "answerTokens": len(ids)-len(prompt_ids), "totalTokens": len(ids)})
    write_json(run / "token-lengths.json", lengths)
    tokenizer.save_pretrained(run / "tokenizer")
    return config, lock, tokenizer, encoded


def train(folder, run, mode, checkpoint=None, adapter=None):
    parent_revision = read_json(Path(adapter) / "provenance.json")["modelRevision"] if adapter else None
    config, lock, tokenizer, encoded = prepare(folder, run, revision_override=parent_revision)
    import torch
    from transformers import BitsAndBytesConfig, Gemma3ForConditionalGeneration, Trainer, TrainerCallback, TrainingArguments, set_seed
    from peft import LoraConfig, PeftModel, get_peft_model, prepare_model_for_kbit_training

    if not torch.cuda.is_available():
        raise RuntimeError("CUDA GPU unavailable. Stop; do not silently fall back to CPU training.")
    if tuple(map(int, torch.__version__.split("+")[0].split(".")[:2])) < (2, 6):
        raise RuntimeError("torch >=2.6 required for this checkpoint workflow")
    dtype = torch.bfloat16 if torch.cuda.is_bf16_supported() else torch.float16
    stage = "smoke" if mode == "smoke" else "trial"
    output = run / stage
    if mode == "resume":
        if not checkpoint:
            raise ValueError("Exact resume requires --checkpoint; do not substitute adapter-only loading")
        checkpoint = Path(checkpoint).resolve()
        if checkpoint.parent != output.resolve():
            raise ValueError("Resume checkpoint must belong to this experiment's trial directory")
        for name in ("trainer_state.json", "optimizer.pt", "scheduler.pt", "adapter_config.json", "adapter_model.safetensors"):
            if not (checkpoint / name).is_file():
                raise ValueError(f"Incomplete resume checkpoint: {name}")
        if not list(checkpoint.glob("rng_state*.pth")):
            raise ValueError("RNG state missing from resume checkpoint")
    elif output.exists() and any(output.iterdir()):
        raise ValueError("Output exists; explicitly resume or use a new experiment directory")
    if mode not in ("smoke", "resume"):
        proof = read_json(run / "smoke" / "completed.json")
        if not proof["adapterReloaded"] or proof["bundleFiles"] != lock["bundleFiles"]:
            raise ValueError("Complete this bundle's smoke test first")
    if adapter:
        if mode != "warmstart":
            raise ValueError("Adapter-only continuation is a warmstart, not an exact resume")
        parent = read_json(Path(adapter) / "provenance.json")
        if parent["modelId"] != lock["modelId"] or parent["modelRevision"] != lock["modelRevision"]:
            raise ValueError("Warmstart adapter base revision mismatch")
        if parent["loraConfig"] != {k: config[k] for k in ("loraRank", "loraAlpha", "loraDropout", "targetModulePattern")}:
            raise ValueError("Warmstart adapter LoRA settings mismatch")
    stamp = {"packages": package_versions(), "computeDtype": str(dtype), "gpu": torch.cuda.get_device_name(0),
             "cuda": torch.version.cuda, "torch": torch.__version__}
    if mode == "resume":
        prior = read_json(output / "runtime.json")
        if prior["packages"] != stamp["packages"] or prior["computeDtype"] != stamp["computeDtype"]:
            raise ValueError("Exact-resume precision/packages differ; use a separate warmstart experiment")
    set_seed(config["seed"])
    torch.cuda.reset_peak_memory_stats()
    started = time.perf_counter()
    quant = BitsAndBytesConfig(load_in_4bit=True, bnb_4bit_quant_type="nf4", bnb_4bit_use_double_quant=True, bnb_4bit_compute_dtype=dtype)
    model = Gemma3ForConditionalGeneration.from_pretrained(lock["modelId"], revision=lock["modelRevision"],
        quantization_config=quant, torch_dtype=dtype, device_map={"": 0}, attn_implementation="eager")
    model.config.use_cache = False
    model = prepare_model_for_kbit_training(model, use_gradient_checkpointing=True,
        gradient_checkpointing_kwargs={"use_reentrant": False})
    matches = [name for name, _ in model.named_modules() if re.fullmatch(config["targetModulePattern"], name)]
    if not matches or any("vision" in name for name in matches):
        raise ValueError("Language-only LoRA targets not found; do not guess another architecture")
    lora = LoraConfig(r=config["loraRank"], lora_alpha=config["loraAlpha"], lora_dropout=config["loraDropout"],
                      target_modules=config["targetModulePattern"], bias="none", task_type="CAUSAL_LM")
    model = PeftModel.from_pretrained(model, adapter, is_trainable=True) if adapter else get_peft_model(model, lora)
    if not any(p.requires_grad for p in model.parameters()):
        raise ValueError("No trainable adapter weights")
    output.mkdir(parents=True, exist_ok=True)
    write_json(output / "runtime.json", stamp)
    write_json(output / "lora-targets.json", matches)

    class FiniteGradient(TrainerCallback):
        def on_pre_optimizer_step(self, args, state, control, model=None, **kwargs):
            grads = [p.grad for p in model.parameters() if p.requires_grad and p.grad is not None]
            if not grads or any(not torch.isfinite(g).all().item() for g in grads):
                raise FloatingPointError("Missing or non-finite adapter gradients")

        def on_log(self, args, state, control, logs=None, **kwargs):
            if logs and any(not math.isfinite(float(logs[k])) for k in ("loss", "grad_norm") if k in logs):
                raise FloatingPointError("Non-finite training metric")

    class Records(torch.utils.data.Dataset):
        def __len__(self):
            return len(encoded)

        def __getitem__(self, index):
            return encoded[index]

    def collate(records):
        length = max(len(row["input_ids"]) for row in records)
        pad = tokenizer.pad_token_id if tokenizer.pad_token_id is not None else tokenizer.eos_token_id
        return {key: torch.tensor([row[key] + [padding] * (length-len(row[key])) for row in records], dtype=torch.long)
                for key, padding in (("input_ids", pad), ("attention_mask", 0), ("labels", -100))}

    steps = config["smokeSteps"] if mode == "smoke" else config["trialSteps"]
    if checkpoint and read_json(checkpoint / "trainer_state.json")["global_step"] >= steps:
        raise ValueError("Checkpoint already reached this experiment's step budget. Use a new warmstart experiment for additional training.")
    args = TrainingArguments(output_dir=str(output), max_steps=steps,
        per_device_train_batch_size=config["batchSize"], gradient_accumulation_steps=config["gradientAccumulationSteps"],
        learning_rate=config["learningRate"], lr_scheduler_type="constant", warmup_steps=0,
        gradient_checkpointing=True, gradient_checkpointing_kwargs={"use_reentrant": False},
        bf16=dtype == torch.bfloat16, fp16=dtype == torch.float16, optim="adamw_torch",
        save_strategy="steps", save_steps=config["saveSteps"], save_total_limit=config["saveTotalLimit"],
        save_safetensors=True, save_only_model=False, logging_steps=1, logging_nan_inf_filter=False,
        report_to=[], remove_unused_columns=False, seed=config["seed"], data_seed=config["seed"], dataloader_num_workers=0)
    trainer = Trainer(model=model, args=args, train_dataset=Records(), data_collator=collate, callbacks=[FiniteGradient()])
    result = trainer.train(resume_from_checkpoint=str(checkpoint) if checkpoint else None)
    if trainer.state.global_step < steps:
        raise RuntimeError("Training did not reach requested optimizer steps")
    adapter_dir = output / "adapter"
    model.save_pretrained(adapter_dir, safe_serialization=True)
    tokenizer.save_pretrained(adapter_dir)
    provenance = {"modelId": lock["modelId"], "modelRevision": lock["modelRevision"], "bundleFiles": lock["bundleFiles"],
        "loraConfig": {k: config[k] for k in ("loraRank", "loraAlpha", "loraDropout", "targetModulePattern")},
        "mode": mode, "parentAdapter": str(adapter) if adapter else None,
        "qualityAccepted": False, "gameApplicationApproved": False}
    write_json(adapter_dir / "provenance.json", provenance)
    # Exercise actual adapter deserialization on this same frozen base, not only file existence.
    model.load_adapter(str(adapter_dir), adapter_name="reload_check", is_trainable=False)
    model.set_adapter("default")
    completed = {"mode": mode, "optimizerSteps": trainer.state.global_step, "adapterReloaded": True,
        "secondsIncludingLoadSave": time.perf_counter()-started, "peakAllocatedBytes": torch.cuda.max_memory_allocated(),
        "peakReservedBytes": torch.cuda.max_memory_reserved(), "metrics": result.metrics,
        "bundleFiles": lock["bundleFiles"], "qualityAccepted": False, "local180SecondTargetMeasured": False}
    write_json(output / "completed.json", completed)
    print(json.dumps(completed, ensure_ascii=False, indent=2))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("validate", "prepare", "smoke", "train", "resume", "warmstart"))
    parser.add_argument("--bundle", type=Path, default=Path(__file__).resolve().parent)
    parser.add_argument("--run", type=Path)
    parser.add_argument("--checkpoint", type=Path)
    parser.add_argument("--adapter", type=Path)
    args = parser.parse_args()
    if args.mode == "validate":
        config, rows, _ = validate(args.bundle)
        print(json.dumps({"valid": True, "trainingPairs": len(rows), "experiment": config["experimentId"], "trainingExecuted": False}))
        return
    if args.checkpoint and args.mode != "resume":
        parser.error("--checkpoint is only valid for exact resume")
    if args.adapter and args.mode != "warmstart":
        parser.error("--adapter is only valid for a separate warmstart experiment")
    if not args.run:
        parser.error("--run is required (use persistent Drive storage in Colab)")
    if args.mode == "prepare":
        _, lock, _, encoded = prepare(args.bundle, args.run)
        print(json.dumps({"revision": lock["modelRevision"], "records": len(encoded), "trainingExecuted": False}))
        return
    if args.mode == "warmstart" and not args.adapter:
        parser.error("warmstart requires --adapter; optimizer is intentionally restarted")
    try:
        train(args.bundle, args.run, args.mode, args.checkpoint, args.adapter)
    except Exception as error:
        # No token/env dumps. Keep failure evidence; never label an OOM as a successful run.
        write_json(args.run / "last-failure.json", {"mode": args.mode, "type": type(error).__name__,
            "message": str(error), "qualityAccepted": False})
        raise


if __name__ == "__main__":
    main()
