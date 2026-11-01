"""Freeze a Gemma warmstart using currently approved material, not new NPC facts."""
import base64
import hashlib
import io
import json
from pathlib import Path
import zipfile
from build_role_task_training import encoded, manifest
from role_task_train import derive
from prepare_meaning_curriculum import build, messages, read

ROOT = Path(__file__).resolve().parents[2]
DATA = ROOT / 'Tests/LocalStoryBenchmark'
OUT = ROOT / 'outputs/AiTraining'
SCRIPTS = Path(__file__).resolve().parent


def main():
    rows, records, contrasts, drafts = build()
    approval = read(DATA / 'Colab/MeaningCurriculum/NormalRenderingApproval.json')
    proofs = {r['id']: r for r in approval['records']}
    assert approval['answerApprovedByUser'] and len(proofs) == 5
    for d in drafts:
        assert proofs[d['id']]['messagesSha256'] == hashlib.sha256(json.dumps(d['messages'], ensure_ascii=False, sort_keys=True).encode()).hexdigest()
        rows.append({k: d[k] for k in ('id', 'splitGroup', 'messages')})
        task = json.loads(d['messages'][0]['content'].split('\n', 1)[1])
        task['representation'] = 'garbled'
        rows.append({'id': d['id'].replace('/normal-', '/surface-garbled-'), 'splitGroup': d['splitGroup'],
                     'messages': messages(task, d['approvedGarbled'])})
    assert len(rows) == 31
    evaluation = []
    for row in rows:
        if row['id'] in ('knowledge-take/npc', 'garbled-listener/npc', 'garbled-listener/options',
                         'identity-choice/npc', 'identity-choice/options', 'garbled-listener/normal-npc',
                         'garbled-listener/surface-garbled-npc'):
            evaluation.append(dict(row, task='options' if row['id'].endswith('/options') else 'npc'))
    heldout = read(DATA / 'Colab/KananaAnswerReviewDrafts.json')
    # The filename is historical; these user-reviewed answers remain validation only.
    meaning_keys = ('coreMeaning', 'semanticText', 'actor', 'target', 'stateBefore', 'stateAfter', 'currentState', 'dialogueTiming', 'storyReference')
    for draft in heldout['drafts']:
        assert draft['reviewedByUser'] and not draft['trainingEligible']
        for row in derive(draft['id'], draft['sceneGroup'], draft['currentInputMessages'], draft['proposedOutput']):
            old = json.loads(row['messages'][0]['content'].split('\n', 1)[1])
            meaning = {k: old['meaning'][k] for k in meaning_keys if old['meaning'].get(k) is not None}
            if row['id'].endswith('-npc'):
                task = {'kind': 'npc_line', 'speaker': old['speaker'], 'listener': old['listener'],
                        'speechStyle': old['speechStyle'], 'speechMode': old['meaning']['speechMode'],
                        'meaning': meaning, 'memory': old['memory'], 'previousEvents': old['previousEvents']}
            else:
                task = {'kind': 'player_options', 'speaker': old['speaker'], 'listener': old['listener'],
                        'speechMode': old['meaning']['speechMode'], 'intents': [{k: o[k] for k in ('index', 'intent')} for o in old['intents']],
                        'npcContext': {'speaker': old['meaning']['speaker'], 'listener': old['meaning']['listener'], 'meaning': meaning},
                        'memory': old['memory']}
            evaluation.append({'id': row['id'], 'splitGroup': row['splitGroup'],
                               'messages': messages(task, row['messages'][-1]['content']),
                               'task': 'npc' if row['id'].endswith('-npc') else 'options'})
    assert len(evaluation) == 11 and sum(c['splitGroup'] == 'gambler-family' for c in evaluation) == 4
    game = {name: (DATA / name).read_bytes() for name in ('TrainingPairs.jsonl', 'CurrentInputCandidates.json', 'DatasetSplitPlan.json', 'TrainingBaseline.json')}
    game['experiment.json'] = (DATA / 'Colab/experiment.json').read_bytes()
    game['bundle-manifest.json'] = manifest(game)
    files = {'game/' + n: b for n, b in game.items()}
    review = read(DATA / 'Colab/MeaningWordingReviewDraft.json')
    files['game/Colab/MeaningWordingReviewDraft.json'] = (DATA / 'Colab/MeaningWordingReviewDraft.json').read_bytes()
    files['source/' + review['sourceFile']] = (ROOT / review['sourceFile']).read_bytes()
    files['NormalRenderingApproval.json'] = encoded(approval)
    config = read(OUT / 'RoleTaskTrainingBundle/experiment.json')
    config.update(experimentId='gemma-meaning-curriculum-20261101-v1',
                  purpose='Improve current Gemma using role-separated intents and normal/garbled contrast from approved material',
                  trainingApprovalScope='approved_source_projections_plus_five_explicitly_approved_normal_renderings',
                  trialSteps=64, saveSteps=16, batchSize=1, gradientAccumulationSteps=2)
    files.update({'experiment.json': encoded(config), 'TrainingPairs.jsonl': ''.join(json.dumps(r, ensure_ascii=False) + '\n' for r in rows).encode('utf8'),
                  'EvaluationTasks.json': encoded(evaluation)})
    for name in ('train.py', 'role_task_train.py', 'prepare_meaning_curriculum.py', 'meaning_curriculum_train.py',
                 'evaluate_role_tasks.py', 'run_guide_input_diagnostic.py', 'evaluate.py'):
        files[name] = (SCRIPTS / name).read_bytes()
    files['bundle-manifest.json'] = manifest(files)
    folder = OUT / 'MeaningCurriculumTrainingBundle'
    for name, raw in files.items():
        target = folder / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(raw)
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name, raw in files.items():
            archive.writestr(name, raw)
    (OUT / 'OneWay_MeaningCurriculum_Bundle.zip').write_bytes(buffer.getvalue())
    cell = '''import pathlib,sys,subprocess,json,base64,io,zipfile,hashlib,os
from google.colab import userdata
if not os.environ.get('HF_TOKEN'): os.environ['HF_TOKEN']=userdata.get('HF_TOKEN')
MC_BUNDLE=pathlib.Path('/content/oneway_meaning_curriculum_v1')
MC_BUNDLE.mkdir(exist_ok=True)
MC_DRIVE=pathlib.Path('/content/drive/MyDrive/OneWayTraining')
assert MC_DRIVE.is_dir(), 'Restore existing authorized Drive connection first'
MC_PARENT=MC_DRIVE/'gemma-role-task-training-20261101-v1'/'trial'/'adapter'
assert (MC_PARENT/'provenance.json').is_file()
def meaning_command(command):
    process=subprocess.Popen(command,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,bufsize=1)
    for line in process.stdout: print(line,end='',flush=True)
    if process.wait(): raise RuntimeError('Meaning curriculum stopped; inspect saved evidence before retry')
'''
    cell += 'mc_payload=base64.b64decode(' + repr(base64.b64encode(buffer.getvalue()).decode()) + ')\n'
    cell += 'assert hashlib.sha256(mc_payload).hexdigest()==' + repr(hashlib.sha256(buffer.getvalue()).hexdigest()) + '\n'
    cell += '''with zipfile.ZipFile(io.BytesIO(mc_payload)) as archive:
    for item in archive.infolist():
        target=(MC_BUNDLE/item.filename).resolve()
        assert target.is_relative_to(MC_BUNDLE.resolve())
        target.parent.mkdir(parents=True,exist_ok=True)
        target.write_bytes(archive.read(item))
MC_RUN=MC_DRIVE/'gemma-meaning-curriculum-20261101-v1'
for mode in ('validate','prepare','smoke','warmstart'):
    done=MC_RUN/('smoke' if mode=='smoke' else 'trial')/'completed.json'
    if mode in ('smoke','warmstart') and done.exists():
        assert json.loads(done.read_text())['bundleFiles']==json.loads((MC_BUNDLE/'bundle-manifest.json').read_text())['files']
        continue
    command=[sys.executable,str(MC_BUNDLE/'meaning_curriculum_train.py'),mode,'--bundle',str(MC_BUNDLE),'--run',str(MC_RUN)]
    if mode=='warmstart': command+=['--adapter',str(MC_PARENT)]
    meaning_command(command)
meaning_command([sys.executable,str(MC_BUNDLE/'evaluate_role_tasks.py'),'--bundle',str(MC_BUNDLE),'--run',str(MC_RUN),
                 '--parent-adapter',str(MC_PARENT),'--checkpoint',str(MC_RUN/'trial/checkpoint-32')])
'''
    (OUT / 'meaning-curriculum-training-cell.py').write_text(cell, encoding='utf8')
    plan = {'experimentId': config['experimentId'], 'modelId': config['modelId'], 'modelRevision': config['modelRevision'],
            'existingSourcePairs': 6, 'approvedNormalRenderings': 5, 'tasks': 31,
            'taskBreakdown': {'approvedSourceProjections': 21, 'approvedNormalSurfaceTasks': 5, 'approvedGarbledSurfaceCompanions': 5},
            'newPhrasesInvented': 0, 'independentTrainingSceneGroups': 1, 'trainingGroups': ['guide-family'],
            'sceneHeldoutValidationTasks': 4, 'finalTestGroupExcluded': 'duelist-family',
            'trialOptimizerSteps': 64, 'checkpointSelection': [32, 64],
            'parentAdapter': 'gemma-role-task-training-20261101-v1/trial/adapter',
            'continuation': 'warmstart: new optimizer; not exact resume',
            'selectionRule': 'Prefer heldout meaning/event/intent preservation; reject regressions. Shape alone does not qualify. No new model candidates.',
            'trainingExecuted': False, 'presentationChanged': False, 'gameApplied': False, 'local180SecondTargetMeasured': False}
    (DATA / 'Colab/MeaningCurriculum/TrainingExperiment.json').write_bytes(encoded(plan))
    print(json.dumps(plan, ensure_ascii=False))


if __name__ == '__main__':
    main()
