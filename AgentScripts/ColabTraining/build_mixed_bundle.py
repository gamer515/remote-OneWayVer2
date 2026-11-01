"""Isolated one-epoch pilot, reusing the original game and public validators."""
import base64
import hashlib
import io
import json
from pathlib import Path
import zipfile

ROOT=Path(__file__).resolve().parents[2]
SCRIPTS=ROOT/'AgentScripts/ColabTraining'
DATA=ROOT/'Tests/LocalStoryBenchmark'
OUT=ROOT/'outputs/AiTraining'

def digest(data):return hashlib.sha256(data).hexdigest()
def main():
    public={}
    with zipfile.ZipFile(OUT/'OneWay_PublicCorrection_Bundle.zip') as archive:
        public={name:archive.read(name) for name in archive.namelist()}
    game={name:(DATA/name).read_bytes() for name in ('TrainingPairs.jsonl','CurrentInputCandidates.json','DatasetSplitPlan.json','TrainingBaseline.json')}
    game.update({'train.py':(SCRIPTS/'train.py').read_bytes(),'experiment.json':(DATA/'Colab/experiment.json').read_bytes()})
    game['bundle-manifest.json']=json.dumps({'files':{name:digest(raw) for name,raw in game.items()}},indent=2).encode()
    files={**{'public/'+name:raw for name,raw in public.items()},**{'game/'+name:raw for name,raw in game.items()}}
    config=json.loads(public['experiment.json'])
    config.update(experimentId='public-game-correction-v1',purpose='One epoch: 66 reviewed published corrections and 6 user-approved game pairs; no deployment',maxSequenceLength=4096,trialSteps=36,saveSteps=12)
    files['experiment.json']=(json.dumps(config,indent=2)+'\n').encode()
    public_rows=[json.loads(line) for line in public['TrainingPairs.jsonl'].decode('utf-8-sig').splitlines() if line.strip()]
    game_rows=[json.loads(line) for line in game['TrainingPairs.jsonl'].decode('utf-8-sig').splitlines() if line.strip()]
    files['TrainingPairs.jsonl']=''.join(json.dumps(row,ensure_ascii=False)+'\n' for row in public_rows+game_rows).encode('utf8')
    for name in ('train.py','public_train.py','mixed_train.py'):files[name]=(SCRIPTS/name).read_bytes()
    files['bundle-manifest.json']=json.dumps({'files':{name:digest(raw) for name,raw in files.items()}},indent=2).encode()
    buffer=io.BytesIO()
    with zipfile.ZipFile(buffer,'w',zipfile.ZIP_DEFLATED) as archive:
        for name,raw in files.items():archive.writestr(name,raw)
    (OUT/'OneWay_MixedCorrection_Bundle.zip').write_bytes(buffer.getvalue())
    cell='import base64,hashlib,io,zipfile\nMIXED_BUNDLE=pathlib.Path("/content/oneway_mixed_correction")\nMIXED_BUNDLE.mkdir(exist_ok=True)\nmixed_payload=base64.b64decode('+repr(base64.b64encode(buffer.getvalue()).decode())+')\nassert hashlib.sha256(mixed_payload).hexdigest()=='+repr(digest(buffer.getvalue()))+'\n'
    cell+='with zipfile.ZipFile(io.BytesIO(mixed_payload)) as archive:\n    for item in archive.infolist():\n        target=(MIXED_BUNDLE/item.filename).resolve()\n        assert target.is_relative_to(MIXED_BUNDLE.resolve())\n        target.parent.mkdir(parents=True,exist_ok=True)\n        target.write_bytes(archive.read(item))\n'
    cell+='MIXED_RUN=RUN.parent/"public-game-correction-v1"\nfor mixed_mode in ("validate","prepare","smoke","train"):\n    stage="smoke" if mixed_mode=="smoke" else "trial"\n    done=MIXED_RUN/stage/"completed.json"\n    if mixed_mode in ("smoke","train") and done.exists():\n        assert json.loads(done.read_text())["bundleFiles"]==json.loads((MIXED_BUNDLE/"bundle-manifest.json").read_text())["files"]\n        continue\n    run_command([sys.executable,str(MIXED_BUNDLE/"mixed_train.py"),mixed_mode,"--bundle",str(MIXED_BUNDLE),"--run",str(MIXED_RUN)])\nprint("MIXED_TRAIN_RESULT_BEGIN")\nprint((MIXED_RUN/"trial/completed.json").read_text())\nprint("MIXED_TRAIN_RESULT_END")\n'
    (OUT/'mixed-training-cell.py').write_text(cell,encoding='utf8')
    (DATA/'Colab/MixedCorrectionExperiment.json').write_bytes(files['experiment.json'])
    print(json.dumps({'public':len(public_rows),'approvedGame':len(game_rows),'steps':36,'maxSequenceLength':4096,'zipSha256':digest(buffer.getvalue())}))

if __name__=='__main__':main()
