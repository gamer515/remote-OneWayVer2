"""Prepare a chosen model's pilot using the already validated 72-pair bundle."""
import argparse
import base64
import hashlib
import io
import json
from pathlib import Path
import zipfile

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'outputs/AiTraining'
CONFIG=ROOT/'Tests/LocalStoryBenchmark/Colab'
def main():
    parser=argparse.ArgumentParser();parser.add_argument('--candidate',required=True);args=parser.parse_args()
    plan=json.loads((CONFIG/'ModelScreeningPlan.json').read_text(encoding='utf8'))
    candidate=next(c for c in plan['candidates'] if c['id']==args.candidate)
    with zipfile.ZipFile(OUT/'OneWay_MixedCorrection_Bundle.zip') as archive:files={n:archive.read(n) for n in archive.namelist()}
    del files['bundle-manifest.json']
    config=json.loads(files['experiment.json'])
    config.update(experimentId='candidate-training-20261031-'+candidate['id'],modelId=candidate['modelId'],modelRevision=candidate['revision'],
        modelLoader='AutoModelForCausalLM',targetModulePattern=r'.*\.(q_proj|v_proj)$',purpose='User requested rapid candidate selection then immediate 72-pair pilot; no presentation changes')
    files['experiment.json']=(json.dumps(config,indent=2)+'\n').encode()
    for name in ('train.py','public_train.py','mixed_train.py'):files[name]=(Path(__file__).parent/name).read_bytes()
    files['bundle-manifest.json']=json.dumps({'files':{n:hashlib.sha256(raw).hexdigest() for n,raw in files.items()}},indent=2).encode()
    buffer=io.BytesIO()
    with zipfile.ZipFile(buffer,'w',zipfile.ZIP_DEFLATED) as archive:
        for name,raw in files.items():archive.writestr(name,raw)
    cell='import sys,subprocess,pathlib,base64,io,zipfile,json\n'
    cell+='screening_command([sys.executable,"-m","pip","install","-q","peft==0.17.1"])\n'
    cell+='CANDIDATE_BUNDLE=pathlib.Path("/content/oneway_candidate_training")\nCANDIDATE_BUNDLE.mkdir(exist_ok=True)\n'
    cell+='with zipfile.ZipFile(io.BytesIO(base64.b64decode('+repr(base64.b64encode(buffer.getvalue()).decode())+'))) as archive:\n    for item in archive.infolist():\n        target=(CANDIDATE_BUNDLE/item.filename).resolve()\n        assert target.is_relative_to(CANDIDATE_BUNDLE.resolve())\n        target.parent.mkdir(parents=True,exist_ok=True)\n        target.write_bytes(archive.read(item))\n'
    cell+='CANDIDATE_RUN=pathlib.Path("/content/drive/MyDrive/OneWayTraining")/'+repr(config['experimentId'])+'\n'
    cell+='for mode in ("validate","prepare","smoke","train"):\n    stage="smoke" if mode=="smoke" else "trial"\n    done=CANDIDATE_RUN/stage/"completed.json"\n    if mode in ("smoke","train") and done.exists():\n        assert json.loads(done.read_text())["bundleFiles"]==json.loads((CANDIDATE_BUNDLE/"bundle-manifest.json").read_text())["files"]\n        continue\n    screening_command([sys.executable,str(CANDIDATE_BUNDLE/"mixed_train.py"),mode,"--bundle",str(CANDIDATE_BUNDLE),"--run",str(CANDIDATE_RUN)])\nprint("CANDIDATE_TRAIN_RESULT_BEGIN")\nprint((CANDIDATE_RUN/"trial/completed.json").read_text())\nprint("CANDIDATE_TRAIN_RESULT_END")\n'
    target=OUT/('candidate-training-'+candidate['id']+'-cell.py');target.write_text(cell,encoding='utf8')
    (OUT/('CandidateTraining-'+candidate['id']+'.zip')).write_bytes(buffer.getvalue())
    print(json.dumps({'candidate':candidate['id'],'preparedOnly':True,'pairs':72,'trialSteps':36,'cell':str(target)}))
if __name__=='__main__':main()
