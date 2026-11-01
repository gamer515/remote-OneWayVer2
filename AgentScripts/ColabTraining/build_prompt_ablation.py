"""Reuse existing A/B evaluator with examples removed as the only input change."""
import base64
import copy
import hashlib
import io
import json
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parents[2]
CONFIG = ROOT/'Tests/LocalStoryBenchmark/Colab'
OUT = ROOT/'outputs/AiTraining'

def main():
    inputs = json.loads((CONFIG/'InputSeparationInputs.json').read_text(encoding='utf8'))
    inputs['plan']['evaluationId'] = 'prompt-examples-ablation-v1'
    inputs['plan']['comparison'] = 'Unchanged base, same instructions and JSON; only examples array becomes empty'
    inputs['plan']['armLabels'] = {'current': '기존 전체 입력', 'separated': '동일 입력에서 예시만 제거'}
    inputs['plan']['limitations'] = ['Validation Gambler family only; exploratory', 'Removing examples changes context length and example availability together; it does not isolate pure token length', 'Same HF NF4 base, no adapter, no runtime GBNF; does not verify installed QAT GGUF or local 180 seconds', 'Missing Explanation fields remain missing; source text fallback is unchanged']
    for case in inputs['cases']:
        original = case['messagesByArm']['current']
        content = original[0]['content']
        lines = content.split('\n')
        payload = json.loads(lines[-1])
        assert lines[-1] == json.dumps(payload, ensure_ascii=False, separators=(',', ':'))
        assert isinstance(payload['examples'], list) and payload['examples']
        modified = copy.deepcopy(payload)
        modified['examples'] = []
        assert {k:v for k,v in modified.items() if k!='examples'} == {k:v for k,v in payload.items() if k!='examples'}
        lines[-1] = json.dumps(modified, ensure_ascii=False, separators=(',', ':'))
        case['messagesByArm'] = {'current': original, 'separated': [{'role':'user','content':'\n'.join(lines)}]}
    raw = (json.dumps(inputs, ensure_ascii=False, indent=2)+'\n').encode('utf8')
    (CONFIG/'PromptExamplesAblationInputs.json').write_bytes(raw)
    files = {'InputSeparationInputs.json': raw}
    for name in ('evaluate.py','evaluate_input_ab.py'):
        files[name] = (ROOT/'AgentScripts/ColabTraining'/name).read_bytes()
    manifest = {'files': {name:hashlib.sha256(data).hexdigest() for name,data in files.items()}}
    files['input-ab-manifest.json'] = json.dumps(manifest).encode()
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name, data in files.items():
            archive.writestr(name, data)
    encoded = base64.b64encode(buffer.getvalue()).decode()
    cell = 'import base64,io,zipfile\nABLATION_BUNDLE=pathlib.Path("/content/oneway_examples_ablation")\nABLATION_BUNDLE.mkdir(exist_ok=True)\n'
    cell += 'with zipfile.ZipFile(io.BytesIO(base64.b64decode('+repr(encoded)+'))) as archive:\n    for item in archive.infolist():\n        target=(ABLATION_BUNDLE/item.filename).resolve()\n        assert target.is_relative_to(ABLATION_BUNDLE.resolve())\n        target.write_bytes(archive.read(item))\n'
    cell += 'run_command([sys.executable,str(ABLATION_BUNDLE/"evaluate_input_ab.py"),"--bundle",str(ABLATION_BUNDLE),"--run",str(RUN)])\nprint("ABLATION_RESULT_BEGIN")\nprint((RUN/"evaluation/prompt-examples-ablation-v1/InputComparison.json").read_text())\nprint("ABLATION_RESULT_END")\n'
    (OUT/'prompt-ablation-cell.py').write_text(cell, encoding='utf8')
    print(json.dumps({'cases':len(inputs['cases']), 'requests':len(inputs['cases'])*len(inputs['plan']['seeds'])*2, 'onlyChange':'examples=[]'}))

if __name__ == '__main__':
    main()
