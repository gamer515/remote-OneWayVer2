"""Deadline screening: reuse frozen inputs and schema checks; never train/apply."""
import argparse
import gc
import importlib.metadata
import json
from pathlib import Path
import time
from evaluate import read,write,sha,structure

def run(bundle,output):
    manifest=read(bundle/'screening-manifest.json')
    for name,digest in manifest['files'].items():
        assert sha(bundle/name)==digest,'Screening bundle changed: '+name
    plan=read(bundle/'ScreeningInputs.json')
    output.mkdir(parents=True,exist_ok=True)
    import torch
    from transformers import AutoTokenizer,AutoModelForCausalLM,BitsAndBytesConfig,set_seed
    assert torch.cuda.is_available(),'CUDA required; no CPU fallback'
    dtype=torch.bfloat16 if torch.cuda.is_bf16_supported() else torch.float16
    packages={p:importlib.metadata.version(p) for p in ('torch','transformers','accelerate','bitsandbytes','huggingface-hub')}
    environment={'packages':packages,'gpu':torch.cuda.get_device_name(0),'dtype':str(dtype),'bundle':manifest}
    lock=output/'environment.json'
    if lock.exists():assert read(lock)==environment,'Environment changed; use a separate experiment'
    write(lock,environment)
    for candidate in plan['candidates']:
        folder=output/candidate['id']
        folder.mkdir(exist_ok=True)
        if (folder/'completed.json').exists():
            print(json.dumps(read(folder/'completed.json'),ensure_ascii=False),flush=True)
            continue
        started_model=time.perf_counter()
        print('LOADING '+candidate['modelId'],flush=True)
        tokenizer=AutoTokenizer.from_pretrained(candidate['modelId'],revision=candidate['revision'],trust_remote_code=False)
        model=AutoModelForCausalLM.from_pretrained(candidate['modelId'],revision=candidate['revision'],trust_remote_code=False,
            torch_dtype=dtype,device_map={'':0},attn_implementation='eager',
            quantization_config=BitsAndBytesConfig(load_in_4bit=True,bnb_4bit_quant_type='nf4',bnb_4bit_use_double_quant=True,bnb_4bit_compute_dtype=dtype))
        model.eval()
        load_seconds=time.perf_counter()-started_model
        rows=[]
        consecutive_limits=0
        stopped=None
        for case in plan['cases']:
            if stopped:break
            for seed in case['seeds']:
                if time.perf_counter()-started_model>plan['perModelWallSeconds']:
                    stopped='per_model_wall_budget';break
                path=folder/'requests'/(case['id'].replace('/','_')+'-'+str(seed)+'.json')
                if path.exists():rows.append(read(path));continue
                set_seed(seed)
                prompt=tokenizer.apply_chat_template(case['messages'],tokenize=False,add_generation_prompt=True)
                encoded=tokenizer(prompt,add_special_tokens=False,return_tensors='pt').to('cuda')
                count=encoded['input_ids'].shape[1]
                assert count+case['maxNewTokens']<=plan['maxSequenceLength'],'Context exceeds fixed budget'
                torch.cuda.reset_peak_memory_stats()
                torch.cuda.synchronize()
                started=time.perf_counter()
                with torch.inference_mode():
                    generated=model.generate(**encoded,max_new_tokens=case['maxNewTokens'],max_time=180,
                        do_sample=case.get('doSample',True),use_cache=True,**case['sampling'])
                torch.cuda.synchronize()
                seconds=time.perf_counter()-started
                tokens=generated[0,count:]
                raw=tokenizer.decode(tokens,skip_special_tokens=True)
                parsed,error=structure(raw,case) if case['kind']=='game' else (None,None)
                diagnostic=None
                if case['kind']=='game':
                    body=raw.strip()
                    if body.startswith('```') and body.endswith('```'):body='\n'.join(body.splitlines()[1:-1])
                    _,diagnostic=structure(body,case)
                row={'caseId':case['id'],'kind':case['kind'],'seed':seed,'raw':raw,'parsed':parsed,'structureError':error,
                    'codeFenceDiagnosticError':diagnostic,'seconds':seconds,'inputTokens':count,'outputTokens':len(tokens),
                    'tokenLimitReached':len(tokens)>=case['maxNewTokens'],'timeBudgetReached':seconds>=180,
                    'peakAllocatedBytes':torch.cuda.max_memory_allocated(),'exactTarget':raw.strip()==case.get('target') if case['kind']=='correction' else None}
                write(path,row);rows.append(row)
                print(json.dumps({k:row[k] for k in ('caseId','seed','seconds','structureError','exactTarget')},ensure_ascii=False),flush=True)
                consecutive_limits=consecutive_limits+1 if row['tokenLimitReached'] or row['timeBudgetReached'] else 0
                del generated,encoded,tokens
                if consecutive_limits>=2:stopped='two_consecutive_generation_limits';break
        result={'candidate':candidate,'requests':rows,'plannedRequests':sum(len(c['seeds']) for c in plan['cases']),
            'stoppedReason':stopped,'modelLoadSeconds':load_seconds,'environment':environment,'trainingExecuted':False,
            'gameApplied':False,'local180SecondTargetMeasured':False,'humanQualityAccepted':None}
        write(folder/'completed.json',result)
        print('SCREEN_MODEL_RESULT_BEGIN',flush=True)
        print(json.dumps(result,ensure_ascii=False),flush=True)
        print('SCREEN_MODEL_RESULT_END',flush=True)
        del model,tokenizer
        gc.collect();torch.cuda.empty_cache()
    print('SCREENING_FINISHED',flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--bundle',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args();run(args.bundle,args.output)
