"""Inference only: compare original versus compact Guide inputs on one adapter."""
import argparse
import gc
import hashlib
import json
from pathlib import Path
import time
from contextlib import nullcontext
from evaluate import read, write, structure, sha


def check_output(raw, case):
    kind = case.get('outputKind', 'cards')
    if kind == 'cards':
        return structure(raw, case)
    if kind == 'npcText':
        text = raw.strip()
        if not text or len(text) > case['limits']['textMaxChars'] or '\n' in text or text.startswith(('{', '[', '```')):
            return None, 'Expected one plain NPC line within text limit'
        return text, None
    if kind == 'playerOptions':
        try:
            values = json.loads(raw)
            if not isinstance(values, list) or len(values) != case['optionCount'] or any(not isinstance(v, str) or not v.strip() or len(v) > case['limits']['optionMaxChars'] for v in values):
                raise ValueError('Expected exact-count array of player reply strings')
            return values, None
        except (ValueError, TypeError) as error:
            return None, str(error)
    raise ValueError('Unknown output kind: ' + kind)


def run(inputs, run_dir, output):
    import torch
    from transformers import AutoModelForCausalLM, AutoTokenizer, BitsAndBytesConfig, set_seed
    from peft import PeftModel
    plan = read(inputs)
    lock = read(run_dir / 'experiment-lock.json')
    runtime = read(run_dir / 'trial/runtime.json')
    assert torch.cuda.is_available(), 'CUDA required'
    dtype = torch.bfloat16 if runtime['computeDtype'] == 'torch.bfloat16' else torch.float16
    same_base = plan.get('sameBaseAsRun', True)
    if same_base:
        assert lock['modelId'] == plan['modelId'] and lock['modelRevision'] == plan['modelRevision']
    else:
        assert plan.get('adapterEnabled') is False, 'New candidate must not load another model adapter'
    adapter = run_dir / 'trial/adapter'
    identity = {'inputsSha256': sha(inputs), 'scriptSha256': sha(Path(__file__)),
                'experimentLockSha256': sha(run_dir / 'experiment-lock.json'),
                'adapterFiles': {p.name: sha(p) for p in adapter.iterdir() if p.is_file()} if same_base else {},
                'modelId': plan['modelId'], 'modelRevision': plan['modelRevision'],
                'adapterEnabled': plan.get('adapterEnabled', True)}
    output.mkdir(parents=True, exist_ok=True)
    if (output / 'lock.json').exists():
        assert read(output / 'lock.json') == identity, 'Diagnostic changed; use new output directory'
    write(output / 'lock.json', identity)
    tokenizer = AutoTokenizer.from_pretrained(plan['modelId'], revision=plan['modelRevision'], trust_remote_code=False)
    model = AutoModelForCausalLM.from_pretrained(plan['modelId'], revision=plan['modelRevision'], trust_remote_code=False,
        torch_dtype=dtype, device_map={'': 0}, attn_implementation='eager',
        quantization_config=BitsAndBytesConfig(load_in_4bit=True, bnb_4bit_quant_type='nf4', bnb_4bit_use_double_quant=True, bnb_4bit_compute_dtype=dtype))
    if same_base:
        model = PeftModel.from_pretrained(model, str(adapter), is_trainable=False)
    model.eval()
    rows = []
    for case in plan['cases']:
        for seed in plan['seeds']:
            for variant in plan.get('variants', ('original', 'compact')):
                path = output / 'requests' / f"{case['id']}-{seed}-{variant}.json"
                if path.exists():
                    rows.append(read(path)); continue
                set_seed(seed)
                prompt = tokenizer.apply_chat_template(case[variant + 'Messages'], tokenize=False, add_generation_prompt=True)
                encoded = tokenizer(prompt, add_special_tokens=False, return_token_type_ids=False, return_tensors='pt').to('cuda')
                count = encoded['input_ids'].shape[1]
                assert count + case['maxNewTokens'] <= 4096
                torch.cuda.synchronize(); started = time.perf_counter()
                with torch.inference_mode(), model.disable_adapter() if same_base and not plan.get('adapterEnabled', True) else nullcontext():
                    generated = model.generate(**encoded, max_new_tokens=case['maxNewTokens'], max_time=180,
                                               do_sample=True, use_cache=True, **plan['sampling'])
                torch.cuda.synchronize(); elapsed = time.perf_counter() - started
                tokens = generated[0, count:]
                raw = tokenizer.decode(tokens, skip_special_tokens=True)
                parsed, error = check_output(raw, case)
                row = {'caseId': case['id'], 'seed': seed, 'variant': variant, 'raw': raw, 'parsed': parsed,
                       'structureError': error, 'inputTokens': count, 'outputTokens': len(tokens), 'seconds': elapsed,
                       'tokenLimitReached': len(tokens) >= case['maxNewTokens'], 'timeBudgetReached': elapsed >= 180}
                write(path, row); rows.append(row)
                print(json.dumps({k: row[k] for k in ('caseId', 'seed', 'variant', 'inputTokens', 'structureError', 'seconds')}, ensure_ascii=False), flush=True)
                del generated, encoded, tokens
    result = {'plan': plan, 'identity': identity, 'rows': rows, 'trainingExecuted': False,
              'unseenEvaluation': False, 'presentationChanged': False, 'local180SecondTargetMeasured': False}
    write(output / 'Comparison.json', result)
    print('GUIDE_DIAGNOSTIC_RESULT_BEGIN', flush=True)
    print(json.dumps(result, ensure_ascii=False), flush=True)
    print('GUIDE_DIAGNOSTIC_RESULT_END', flush=True)
    del model, tokenizer
    gc.collect(); torch.cuda.empty_cache()


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('--inputs', type=Path, required=True)
    p.add_argument('--run', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    args = p.parse_args(); run(args.inputs, args.run, args.output)
