"""Build an auditable, game-only task-aligned training bundle and Colab cell."""
import base64
import hashlib
import io
import json
from pathlib import Path
import zipfile
from role_task_train import derive

ROOT = Path(__file__).resolve().parents[2]
DATA = ROOT / 'Tests/LocalStoryBenchmark'
OUT = ROOT / 'outputs/AiTraining'
SCRIPTS = Path(__file__).resolve().parent


def encoded(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode('utf8')


def manifest(files):
    return encoded({'files': {name: hashlib.sha256(raw).hexdigest() for name, raw in files.items()}})


def main():
    source_rows = [json.loads(s) for s in (DATA / 'TrainingPairs.jsonl').read_text(encoding='utf-8-sig').splitlines() if s.strip()]
    entries = {e['id']: e for e in json.loads((DATA / 'CurrentInputCandidates.json').read_text(encoding='utf8'))['entries']}
    tasks = []
    for row in source_rows:
        entry = entries[row['id']]
        assert entry['trainingEligible'] and entry['answerApprovedByUser'] and row['splitGroup'] == 'guide-family'
        answer = json.loads(row['messages'][-1]['content'])
        assert answer == entry['expectedOutput']
        tasks.extend(derive(row['id'], row['splitGroup'], row['messages'][:-1], answer))
    assert len(tasks) == 9
    heldout = []
    reviews = json.loads((DATA / 'Colab/KananaAnswerReviewDrafts.json').read_text(encoding='utf8'))
    for draft in reviews['drafts']:
        assert draft['reviewedByUser'] and not draft['trainingEligible']
        assert draft['status'] == 'answer_approved_validation_only'
        heldout.extend(derive(draft['id'], draft['sceneGroup'], draft['currentInputMessages'], draft['proposedOutput']))
    assert len(heldout) == 4
    diagnostic = [r for r in tasks if r['id'].split('/')[0] in ('knowledge-take', 'garbled-listener', 'identity-choice')]
    evaluation = [dict(r, task='npc' if r['id'].endswith('-npc') else 'options') for r in diagnostic + heldout]
    assert len(evaluation) == 9
    game = {name: (DATA / name).read_bytes() for name in ('TrainingPairs.jsonl', 'CurrentInputCandidates.json', 'DatasetSplitPlan.json', 'TrainingBaseline.json')}
    game['experiment.json'] = (DATA / 'Colab/experiment.json').read_bytes()
    game['bundle-manifest.json'] = manifest(game)
    config = json.loads(game['experiment.json'])
    gemma_reference = json.loads((OUT / 'MixedComparison.json').read_text(encoding='utf8'))
    assert gemma_reference['modelId'] == 'google/gemma-3-4b-it'
    config.update(experimentId='gemma-role-task-training-20261101-v1',
                  modelId=gemma_reference['modelId'], modelRevision=gemma_reference['modelRevision'],
                  modelLoader='Gemma3ForConditionalGeneration',
                  purpose='Correct task/output mismatch using only lossless projections of six approved game answers',
                  trainingApprovalScope='lossless_projection_of_existing_approved_answers',
                  maxSequenceLength=2048, smokeSteps=2, trialSteps=36, saveSteps=12,
                  batchSize=1, gradientAccumulationSteps=2)
    files = {'game/' + n: b for n, b in game.items()}
    files.update({'experiment.json': encoded(config), 'TrainingPairs.jsonl': ''.join(json.dumps(r, ensure_ascii=False) + '\n' for r in tasks).encode('utf8'),
                  'EvaluationTasks.json': encoded(evaluation)})
    for name in ('train.py', 'role_task_train.py', 'evaluate_role_tasks.py', 'run_guide_input_diagnostic.py', 'evaluate.py'):
        files[name] = (SCRIPTS / name).read_bytes()
    files['bundle-manifest.json'] = manifest(files)
    folder = OUT / 'RoleTaskTrainingBundle'
    for name, raw in files.items():
        path = folder / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name, raw in files.items():
            archive.writestr(name, raw)
    (OUT / 'OneWay_RoleTaskTraining_Bundle.zip').write_bytes(buffer.getvalue())
    payload = base64.b64encode(buffer.getvalue()).decode()
    cell = '''import pathlib,sys,subprocess,json,base64,io,zipfile,hashlib,importlib.metadata
ROLE_BUNDLE=pathlib.Path('/content/oneway_role_tasks_v1')
ROLE_BUNDLE.mkdir(exist_ok=True)
DRIVE_ROOT=pathlib.Path('/content/drive/MyDrive/OneWayTraining')
assert DRIVE_ROOT.is_dir(), 'Restore existing authorized Drive connection first'
def role_command(command):
    process=subprocess.Popen(command,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,bufsize=1)
    for line in process.stdout: print(line,end='',flush=True)
    if process.wait(): raise RuntimeError('Role-task experiment failed; inspect saved failure before retry')
'''
    cell += 'role_payload=base64.b64decode(' + repr(payload) + ')\n'
    cell += 'assert hashlib.sha256(role_payload).hexdigest()==' + repr(hashlib.sha256(buffer.getvalue()).hexdigest()) + '\n'
    cell += '''with zipfile.ZipFile(io.BytesIO(role_payload)) as archive:
    for item in archive.infolist():
        target=(ROLE_BUNDLE/item.filename).resolve()
        assert target.is_relative_to(ROLE_BUNDLE.resolve())
        target.parent.mkdir(parents=True,exist_ok=True)
        target.write_bytes(archive.read(item))
ROLE_RUN=DRIVE_ROOT/'gemma-role-task-training-20261101-v1'
for mode in ('validate','prepare','smoke','train'):
    done=ROLE_RUN/('smoke' if mode=='smoke' else 'trial')/'completed.json'
    if mode in ('smoke','train') and done.exists():
        assert json.loads(done.read_text())['bundleFiles']==json.loads((ROLE_BUNDLE/'bundle-manifest.json').read_text())['files']
        continue
    role_command([sys.executable,str(ROLE_BUNDLE/'role_task_train.py'),mode,'--bundle',str(ROLE_BUNDLE),'--run',str(ROLE_RUN)])
role_command([sys.executable,str(ROLE_BUNDLE/'evaluate_role_tasks.py'),'--bundle',str(ROLE_BUNDLE),'--run',str(ROLE_RUN)])
'''
    (OUT / 'role-task-training-cell.py').write_text(cell, encoding='utf8')
    decision = {'experimentId': config['experimentId'], 'originalApprovedPairs': len(source_rows), 'derivedTrainingTasks': len(tasks),
                'independentTrainingSceneGroups': 1, 'trainingGroups': ['guide-family'], 'heldOutValidationTasks': len(heldout),
                'heldOutValidationGroups': ['gambler-family'], 'finalTestGroupUntouched': 'duelist-family',
                'optimizerSteps': 36, 'nominalExamplePresentations': 72, 'nominalEpochs': 8,
                'epochAccounting': 'Actual Trainer epoch must be read from completed metrics; final partial accumulation can change it',
                'modelId': config['modelId'], 'modelRevision': config['modelRevision'],
                'modelSource': 'Existing Gemma MixedComparison.json; no new candidate selection',
                'freshAdapterFromPinnedGemmaBase': True, 'publicCorrectionTrainingInThisExperiment': False,
                'sourceFiles': {n: hashlib.sha256(b).hexdigest() for n, b in game.items() if n != 'bundle-manifest.json'},
                'answerTransformation': 'Extract exactly approved text or JSON-serialize exactly approved options, no rewriting',
                'inputTransformation': 'Remove examples, reference answers and unrelated full-scene fields; preserve supplied meaning/actor/state/intents/memory/previousEvents. Empty Explanation retains Story reference.',
                'comparisonScope': 'One deterministic base/adapter check of 5 known training tasks and 4 scene-heldout validation tasks, not candidate ranking or final test',
                'presentationChanged': False, 'generatedFailuresAddedToTraining': False}
    (DATA / 'Colab/RoleTaskTrainingExperiment.json').write_bytes(encoded(decision))
    print(json.dumps(decision, ensure_ascii=False))


if __name__ == '__main__':
    main()
