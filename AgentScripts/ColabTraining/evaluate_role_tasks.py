"""One bounded base/adapter check after aligned training, with raw outputs retained."""
import argparse
from contextlib import nullcontext
import json
from pathlib import Path
import time
import train as engine


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--bundle', type=Path, required=True)
    parser.add_argument('--run', type=Path, required=True)
    parser.add_argument('--parent-adapter', type=Path)
    parser.add_argument('--checkpoint', type=Path)
    args = parser.parse_args()
    manifest = engine.verify_bundle(args.bundle)
    lock = engine.read_json(args.run / 'experiment-lock.json')
    assert lock['packages'] == engine.package_versions()
    assert lock['bundleFiles'] == manifest['files']
    cases = engine.read_json(args.bundle / 'EvaluationTasks.json')
    import torch
    from transformers import AutoModelForCausalLM, AutoTokenizer, BitsAndBytesConfig, Gemma3ForConditionalGeneration
    from peft import PeftModel
    from run_guide_input_diagnostic import check_output
    runtime = engine.read_json(args.run / 'trial/runtime.json')
    dtype = torch.bfloat16 if runtime['computeDtype'] == 'torch.bfloat16' else torch.float16
    tokenizer = AutoTokenizer.from_pretrained(lock['modelId'], revision=lock['modelRevision'], trust_remote_code=False)
    loader = {'AutoModelForCausalLM': AutoModelForCausalLM, 'Gemma3ForConditionalGeneration': Gemma3ForConditionalGeneration}[lock['config']['modelLoader']]
    model = loader.from_pretrained(lock['modelId'], revision=lock['modelRevision'], trust_remote_code=False,
        torch_dtype=dtype, device_map={'': 0}, attn_implementation='eager',
        quantization_config=BitsAndBytesConfig(load_in_4bit=True, bnb_4bit_quant_type='nf4', bnb_4bit_use_double_quant=True, bnb_4bit_compute_dtype=dtype))
    model = PeftModel.from_pretrained(model, str(args.run / 'trial/adapter'), is_trainable=False)
    arms = ['base', 'trained']
    adapter_paths = {'trained': args.run / 'trial/adapter'}
    if args.parent_adapter:
        parent = engine.read_json(args.parent_adapter / 'provenance.json')
        assert (parent['modelId'], parent['modelRevision']) == (lock['modelId'], lock['modelRevision'])
        model.load_adapter(str(args.parent_adapter), adapter_name='parent', is_trainable=False)
        adapter_paths['parent'] = args.parent_adapter
        arms = ['parent', 'trained']
    if args.checkpoint:
        assert args.parent_adapter, 'Checkpoint comparison requires explicit pre-training parent'
        assert args.checkpoint.resolve().parent == (args.run / 'trial').resolve()
        model.load_adapter(str(args.checkpoint), adapter_name='midpoint', is_trainable=False)
        adapter_paths['midpoint'] = args.checkpoint
        arms = ['parent', 'midpoint', 'trained']
    model.eval()
    output = args.run / 'evaluation'
    identity = {'bundleFiles': lock['bundleFiles'], 'adapterFiles':
                {name: {p.name: engine.sha(p) for p in path.iterdir() if p.name in ('adapter_config.json', 'adapter_model.safetensors')}
                 for name, path in adapter_paths.items()}, 'arms': arms}
    if (output / 'evaluation-lock.json').exists():
        assert engine.read_json(output / 'evaluation-lock.json') == identity
    engine.write_json(output / 'evaluation-lock.json', identity)
    rows = []
    for arm in arms:
        if arm != 'base':
            model.set_adapter('default' if arm == 'trained' else arm)
        for case in cases:
            path = output / (arm + '-' + case['id'].replace('/', '_') + '.json')
            if path.exists():
                rows.append(engine.read_json(path))
                continue
            prompt = tokenizer.apply_chat_template(case['messages'][:-1], tokenize=False, add_generation_prompt=True)
            encoded = tokenizer(prompt, add_special_tokens=False, return_token_type_ids=False, return_tensors='pt').to('cuda')
            count = encoded['input_ids'].shape[1]
            budget = 128 if case['task'] == 'npc' else 192
            assert count + budget <= 2048
            torch.cuda.synchronize()
            started = time.perf_counter()
            with torch.inference_mode(), model.disable_adapter() if arm == 'base' else nullcontext():
                generated = model.generate(**encoded, do_sample=False, max_new_tokens=budget, max_time=45, use_cache=True)
            torch.cuda.synchronize()
            elapsed = time.perf_counter() - started
            tokens = generated[0, count:]
            raw = tokenizer.decode(tokens, skip_special_tokens=True)
            shape = {'outputKind': 'npcText' if case['task'] == 'npc' else 'playerOptions',
                     'optionCount': 4, 'limits': {'textMaxChars': 160, 'optionMaxChars': 45}}
            parsed, error = check_output(raw, shape)
            row = {'id': case['id'], 'task': case['task'], 'splitGroup': case['splitGroup'], 'arm': arm,
                   'raw': raw, 'parsed': parsed, 'structureError': error, 'inputTokens': count,
                   'outputTokens': len(tokens), 'seconds': elapsed, 'tokenLimitReached': len(tokens) >= budget,
                   'timeBudgetReached': elapsed >= 45, 'exactApprovedMatch': raw == case['messages'][-1]['content']}
            engine.write_json(path, row)
            rows.append(row)
            print(json.dumps({k: row[k] for k in ('id', 'arm', 'structureError', 'seconds')}, ensure_ascii=False), flush=True)
    result = {'modelId': lock['modelId'], 'modelRevision': lock['modelRevision'],
              'training': engine.read_json(args.run / 'trial/completed.json'), 'cases': cases, 'rows': rows,
              'sampling': {'do_sample': False}, 'runtime': runtime, 'bundleFiles': lock['bundleFiles'],
              'evaluationIdentity': identity,
              'presentationChanged': False, 'local180SecondTargetMeasured': False, 'automaticGameApplication': False}
    engine.write_json(output / 'Comparison.json', result)
    print('ROLE_TASK_RESULT_BEGIN', flush=True)
    print(json.dumps(result, ensure_ascii=False), flush=True)
    print('ROLE_TASK_RESULT_END', flush=True)


if __name__ == '__main__':
    main()
