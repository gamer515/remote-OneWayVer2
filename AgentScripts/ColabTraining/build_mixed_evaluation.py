"""Prepare validation only; final game and public test sets remain unopened."""
import base64
import hashlib
import io
import json
from pathlib import Path
import zipfile

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'outputs/AiTraining'
SCRIPTS=ROOT/'AgentScripts/ColabTraining'
CONFIG=ROOT/'Tests/LocalStoryBenchmark/Colab'
def main():
    data=json.loads((CONFIG/'EvaluationInputs.json').read_text(encoding='utf8'))
    data['cases']=[case for case in data['cases'] if case['split']=='validation']
    data['plan']['evaluationId']='mixed-game-validation-v1'
    data['plan']['cases']=[case for case in data['plan']['cases'] if case['id']=='gambler-main']
    data['plan']['scope']='Exploratory validation only. Do not treat previously inspected scenes as fresh final evaluation.'
    files={'EvaluationInputs.json':json.dumps(data,ensure_ascii=False,indent=2).encode('utf8'),'evaluate.py':(SCRIPTS/'evaluate.py').read_bytes()}
    with zipfile.ZipFile(OUT/'OneWay_MixedCorrection_Bundle.zip') as archive:
        bundle_manifest=json.loads(archive.read('bundle-manifest.json'))
        rows=[json.loads(line) for line in archive.read('TrainingPairs.jsonl').decode('utf8').splitlines()]
    manifest={'files':{name:hashlib.sha256(raw).hexdigest() for name,raw in files.items()},'trainingDatasetSha256':bundle_manifest['files']['TrainingPairs.jsonl'],'trainingGroups':sorted({row['splitGroup'] for row in rows})}
    files['evaluation-manifest.json']=json.dumps(manifest,indent=2).encode()
    public_script=(SCRIPTS/'evaluate_public_correction.py').read_text(encoding='utf8')
    assert 'bundle / "PublicCorpusLock.json"' in public_script
    public_script=public_script.replace('bundle / "PublicCorpusLock.json"','bundle / "public/PublicCorpusLock.json"')
    files['evaluate_mixed_public.py']=public_script.encode('utf8')
    buffer=io.BytesIO()
    with zipfile.ZipFile(buffer,'w',zipfile.ZIP_DEFLATED) as archive:
        for name,raw in files.items():archive.writestr(name,raw)
    cell='import base64,io,zipfile\nMIXED_EVAL_BUNDLE=pathlib.Path("/content/oneway_mixed_eval")\nMIXED_EVAL_BUNDLE.mkdir(exist_ok=True)\n'
    cell+='with zipfile.ZipFile(io.BytesIO(base64.b64decode('+repr(base64.b64encode(buffer.getvalue()).decode())+'))) as archive:\n    for item in archive.infolist():\n        target=(MIXED_EVAL_BUNDLE/item.filename).resolve()\n        assert target.is_relative_to(MIXED_EVAL_BUNDLE.resolve())\n        target.write_bytes(archive.read(item))\n'
    cell+='run_command([sys.executable,str(MIXED_EVAL_BUNDLE/"evaluate.py"),"--bundle",str(MIXED_EVAL_BUNDLE),"--run",str(MIXED_RUN)])\nprint("MIXED_GAME_RESULT_BEGIN")\nprint((MIXED_RUN/"evaluation/mixed-game-validation-v1/Comparison.json").read_text())\nprint("MIXED_GAME_RESULT_END")\n'
    cell+='mixed_public_path=MIXED_BUNDLE/"evaluate_mixed_public.py"\nmixed_public_path.write_bytes((MIXED_EVAL_BUNDLE/"evaluate_mixed_public.py").read_bytes())\nrun_command([sys.executable,str(mixed_public_path),"--bundle",str(MIXED_BUNDLE),"--run",str(MIXED_RUN)])\n'
    (OUT/'mixed-evaluation-cell.py').write_text(cell,encoding='utf8')
    print(json.dumps({'gameCases':len(data['cases']),'gameRequests':8,'publicRequests':50,'finalTestIncluded':False}))
if __name__=='__main__':main()
