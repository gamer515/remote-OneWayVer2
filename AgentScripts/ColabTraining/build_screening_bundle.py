"""Keep candidate/model choices in JSON and reuse approved exported inputs."""
import base64
import hashlib
import io
import json
from pathlib import Path
import zipfile

ROOT=Path(__file__).resolve().parents[2]
DATA=ROOT/'Tests/LocalStoryBenchmark'
CONFIG=DATA/'Colab'
OUT=ROOT/'outputs/AiTraining'
def main():
    plan=json.loads((CONFIG/'ModelScreeningPlan.json').read_text(encoding='utf8'))
    source=json.loads((CONFIG/'EvaluationInputs.json').read_text(encoding='utf8'))
    cases=[]
    for case in source['cases']:
        if case['split']!='validation':continue
        cases.append({**case,'kind':'game','seeds':source['plan']['seeds'],'sampling':source['plan']['sampling']})
    approved=json.loads((DATA/'CurrentInputCandidates.json').read_text(encoding='utf8'))
    for entry in approved['entries']:
        if entry['id'] not in plan['approvedDiagnosticIds']:continue
        assert entry['answerApprovedByUser'] and entry['trainingEligible']
        payload=json.loads(entry['messages'][0]['content'].splitlines()[-1])
        cases.append({'id':'guide-diagnostic/'+entry['id'],'kind':'game','count':1,'sourceCards':entry['expectedOutput']['cards'],
            'limits':payload['limits'],'messages':entry['messages'],'maxNewTokens':entry['maxTokens'],'seeds':source['plan']['seeds'],
            'sampling':source['plan']['sampling'],'meaningSource':payload['cards'],'isFinalHoldout':False})
    for filename in ('ShortStylePlan.json','ShortStyleGeneralPlan.json'):
        short=json.loads((CONFIG/filename).read_text(encoding='utf8'))
        cases.append({'id':short['evaluationId'],'kind':'style','messages':short['messages'],'maxNewTokens':short['maxNewTokens'],
            'seeds':short['seeds'],'sampling':short['sampling']})
    with zipfile.ZipFile(OUT/'OneWay_PublicCorrection_Bundle.zip') as archive:public=json.loads(archive.read('PublicCorpusLock.json'))
    for row in public['records']:
        if row['split']=='validation':
            cases.append({'id':row['id'],'kind':'correction','messages':[{'role':'user','content':public['instruction']+row['sourceText']}],
                'target':row['targetText'],'maxNewTokens':128,'seeds':[12345],'sampling':{},'doSample':False})
    inputs={**plan,'cases':cases}
    files={'ScreeningInputs.json':(json.dumps(inputs,ensure_ascii=False,indent=2)+'\n').encode('utf8')}
    for name in ('evaluate.py','screen_models.py'):files[name]=(Path(__file__).parent/name).read_bytes()
    files['screening-manifest.json']=json.dumps({'files':{name:hashlib.sha256(raw).hexdigest() for name,raw in files.items()}},indent=2).encode()
    buffer=io.BytesIO()
    with zipfile.ZipFile(buffer,'w',zipfile.ZIP_DEFLATED) as archive:
        for name,raw in files.items():archive.writestr(name,raw)
    cell='import sys,subprocess,pathlib,base64,io,zipfile,json\nfrom google.colab import drive\ndrive.mount("/content/drive")\n'
    cell+='def screening_command(command):\n    process=subprocess.Popen(command,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,bufsize=1)\n    for line in process.stdout:print(line,end="",flush=True)\n    if process.wait():raise RuntimeError("Screening command failed")\n'
    cell+='screening_command([sys.executable,"-m","pip","install","-q","transformers==4.57.1","accelerate==1.11.0","bitsandbytes==0.48.1","huggingface-hub==0.36.0","safetensors==0.6.2"])\n'
    cell+='SCREEN_BUNDLE=pathlib.Path("/content/oneway_screening_20261031")\nSCREEN_BUNDLE.mkdir(exist_ok=True)\n'
    cell+='with zipfile.ZipFile(io.BytesIO(base64.b64decode('+repr(base64.b64encode(buffer.getvalue()).decode())+'))) as archive:\n    for item in archive.infolist():\n        target=(SCREEN_BUNDLE/item.filename).resolve()\n        assert target.is_relative_to(SCREEN_BUNDLE.resolve())\n        target.write_bytes(archive.read(item))\n'
    cell+='SCREEN_OUTPUT=pathlib.Path("/content/drive/MyDrive/OneWayTraining/model-screening-20261031")\nscreening_command([sys.executable,str(SCREEN_BUNDLE/"screen_models.py"),"--bundle",str(SCREEN_BUNDLE),"--output",str(SCREEN_OUTPUT)])\n'
    (OUT/'model-screening-cell.py').write_text(cell,encoding='utf8')
    (OUT/'ModelScreeningInputs.json').write_bytes(files['ScreeningInputs.json'])
    print(json.dumps({'candidates':len(plan['candidates']),'requestsPerCandidate':sum(len(c['seeds']) for c in cases),'training':False}))
if __name__=='__main__':main()
