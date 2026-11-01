"""Prepare meaning-focused tasks from approved game answers and canonical data.

No generated failure is used as training data. Canonical normal-rendering additions
are review drafts, not implicitly approved because they exist in Explanation.
"""
import hashlib
import html
import json
from pathlib import Path
from role_task_train import objects

ROOT = Path(__file__).resolve().parents[2]
DATA = ROOT / 'Tests/LocalStoryBenchmark'
OUT = ROOT / 'outputs/AiTraining'

INSTRUCTIONS = {
    'npc_line': 'task의 발화자가 청자에게 하는 실제 대사 한 줄만 쓴다. meaning의 사건·주체·대상·전후 상태를 지킨다. 이름 접두사·해설·선택지·새 사건·보상은 쓰지 않는다. garbled일 때는 semanticText의 뜻을 유지한 난독 발음으로 쓴다. 160자 이내.\n',
    'player_options': 'task의 플레이어가 NPC에게 하는 실제 응답을 intents 순서대로 쓴다. NPC의 질문을 대신 출력하지 않는다. 질문·이해·중얼거림·무관심·모욕·거절·반환 요구를 서로 바꾸지 않는다. 출력은 문자열 네 개의 JSON 배열 하나다. garbled이면 뜻을 눈치챌 수 있는 난독 발음을 유지한다. 각 45자 이내.\n',
    'player_option': 'task의 플레이어가 NPC에게 하는 응답 하나만 쓴다. intent의 뜻과 발화 주체를 지킨다. NPC 질문·분류명·해설·이름 접두사·JSON 없이 실제 응답 한 줄만 출력한다. garbled이면 뜻을 눈치챌 수 있는 난독 발음으로 쓴다. 45자 이내.\n',
    'surface_realization': '실제 게임 사건을 진행하지 않는 표현 연습이다. meaningText의 뜻과 발화 역할을 유지하여 representation으로 지정한 표기 방식의 대사 한 줄만 쓴다. normal은 일반 표기, garbled는 뜻을 눈치챌 수 있는 난독 발음이다. 해설·이름 접두사는 쓰지 않는다.\n',
}


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def dump(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf8')


def messages(task, expected):
    return [{'role': 'user', 'content': INSTRUCTIONS[task['kind']] + json.dumps(task, ensure_ascii=False, separators=(',', ':'))},
            {'role': 'assistant', 'content': expected}]


def canonical_matches(value, semantic_text, pointer=''):
    found = []
    if isinstance(value, dict):
        if value.get('semanticText') == semantic_text:
            found.append((pointer, value))
        for key, child in value.items():
            found.extend(canonical_matches(child, semantic_text, pointer + '/' + key.replace('~', '~0').replace('/', '~1')))
    elif isinstance(value, list):
        for index, child in enumerate(value):
            found.extend(canonical_matches(child, semantic_text, pointer + '/' + str(index)))
    return found


def build():
    entries = {e['id']: e for e in read(DATA / 'CurrentInputCandidates.json')['entries']}
    sources = [json.loads(s) for s in (DATA / 'TrainingPairs.jsonl').read_text(encoding='utf-8-sig').splitlines() if s.strip()]
    assert len(sources) == 6
    rows, manifest, contrasts = [], [], []
    normal_drafts = []
    for source in sources:
        entry = entries[source['id']]
        assert entry['trainingEligible'] and entry['answerApprovedByUser']
        assert source['splitGroup'] == entry['splitGroup'] == 'guide-family'
        assert source['messages'][:-1] == entry['messages']
        answer = json.loads(source['messages'][-1]['content'])
        assert answer == entry['expectedOutput']
        header, payload = objects(source['messages'][:-1])
        assert len(payload['cards']) == len(answer['cards']) == 1
        card, target = payload['cards'][0], answer['cards'][0]
        char = next(c for c in header['characters'] if c['id'] == card['speakerId'])
        # NPC task meaning has no instruction to generate player choices.
        meaning = {k: card[k] for k in ('coreMeaning', 'semanticText', 'actor', 'target', 'stateBefore', 'stateAfter', 'currentState', 'dialogueTiming') if card.get(k) is not None}
        npc_task = {'kind': 'npc_line', 'speaker': card['speaker'], 'listener': card['listener'],
                    'speechStyle': char.get('speechStyle'), 'speechMode': card['speechMode'], 'meaning': meaning,
                    'memory': payload.get('memory'), 'previousEvents': payload.get('previousEvents', [])}
        source_tasks = []

        def append(kind, task, expected, field):
            row_id = source['id'] + '/' + kind
            row = {'id': row_id, 'splitGroup': source['splitGroup'], 'messages': messages(task, expected)}
            rows.append(row)
            source_tasks.append(row_id)
            manifest.append({'id': row_id, 'sourceId': source['id'], 'answerField': field,
                             'approval': 'existing_user_approved_answer_exact_projection', 'taskKind': task['kind'],
                             'expectedOutput': expected, 'trainingEligible': True})

        append('npc', npc_task, target['text'], '/cards/0/text')
        if card['options']:
            assert len(card['options']) == len(target['options']) == 4
            options_task = {'kind': 'player_options', 'speaker': card['listener'], 'listener': card['speaker'],
                            'speechMode': card['speechMode'], 'intents': [{'index': o['index'], 'intent': o['intent']} for o in card['options']],
                            'npcContext': {'speaker': card['speaker'], 'listener': card['listener'], 'meaning': meaning},
                            'memory': payload.get('memory')}
            append('options', options_task, json.dumps(target['options'], ensure_ascii=False, separators=(',', ':')), '/cards/0/options')
            for index, (option, phrase) in enumerate(zip(card['options'], target['options'])):
                task = {'kind': 'player_option', 'speaker': option['speaker'], 'listener': option['listener'],
                        'speechMode': card['speechMode'], 'intent': option['intent'],
                        'npcContext': options_task['npcContext'], 'memory': payload.get('memory')}
                assert task['speaker'] == card['listener'] and task['listener'] == card['speaker']
                append('option-' + str(index), task, phrase, '/cards/0/options/' + str(index))
        contrasts.append({'sourceId': source['id'], 'sourceEventId': entry['eventId'], 'trainingTaskIds': source_tasks,
                          'meaning': meaning, 'approvedNpc': target['text'], 'approvedOptions': target['options']})

        if card['speechMode'] == 'garbled':
            # Resolve the canonical module by the already reviewed source file,
            # not by guessed NPC paths or generated replacement facts.
            review_source = read(DATA / 'Colab/MeaningWordingReviewDraft.json')
            source_path = ROOT / review_source['sourceFile']
            assert digest(source_path) == review_source['sourceSha256']
            found = canonical_matches(read(source_path), card['semanticText'])
            assert len(found) == 1
            pointer, canonical = found[0]
            assert canonical['coreMeaning'] == card['coreMeaning']
            pieces = [('npc', card['speaker'], card['listener'], canonical['semanticText'], target['text'], pointer + '/semanticText')]
            choices = list(canonical['choices'].items())
            assert len(choices) == len(card['options']) == 4
            for index, ((key, choice), option) in enumerate(zip(choices, card['options'])):
                assert choice['intent'] == option['intent']
                pieces.append(('option-' + str(index), option['speaker'], option['listener'], choice['text'], target['options'][index], pointer + '/choices/' + key + '/text'))
            for suffix, speaker, listener, normal, garbled, normal_pointer in pieces:
                task = {'kind': 'surface_realization', 'speaker': speaker, 'listener': listener,
                        'meaningText': normal, 'representation': 'normal'}
                normal_drafts.append({'id': source['id'] + '/normal-' + suffix, 'splitGroup': source['splitGroup'],
                                      'messages': messages(task, normal), 'normal': normal, 'approvedGarbled': garbled,
                                      'sourceFile': review_source['sourceFile'], 'sourceSha256': digest(source_path),
                                      'normalJsonPointer': normal_pointer, 'sourceApprovedAnswer': source['id'] + ('/cards/0/text' if suffix == 'npc' else '/cards/0/options/' + suffix.split('-')[-1]),
                                      'status': 'pending_user_review_new_auxiliary_task', 'answerApprovedByUser': False, 'trainingEligible': False,
                                      'scope': 'Auxiliary surface rendering only; actual garbled game event and choice order unchanged'})
    assert len(rows) == 21 and len(manifest) == 21 and len(normal_drafts) == 5
    assert len({r['id'] for r in rows}) == 21
    assert all(len(r['messages'][-1]['content']) <= 160 for r in rows if not r['id'].endswith('/options'))
    return rows, manifest, contrasts, normal_drafts


def main():
    rows, records, contrasts, drafts = build()
    folder = DATA / 'Colab/MeaningCurriculum'
    folder.mkdir(parents=True, exist_ok=True)
    approval_path = folder / 'NormalRenderingApproval.json'
    approved = approval_path.exists() and read(approval_path).get('answerApprovedByUser') is True
    if approved:
        proofs = {r['id']: r for r in read(approval_path)['records']}
        for draft in drafts:
            assert proofs[draft['id']]['messagesSha256'] == hashlib.sha256(json.dumps(draft['messages'], ensure_ascii=False, sort_keys=True).encode()).hexdigest()
            draft.update(status='user_approved_auxiliary_task', answerApprovedByUser=True, trainingEligible=True)
    (folder / 'ApprovedProjectionTasks.jsonl').write_text(''.join(json.dumps(r, ensure_ascii=False) + '\n' for r in rows), encoding='utf8')
    dump(folder / 'NormalRenderingDrafts.json', {'reviewRequired': not approved, 'records': drafts})
    dump(folder / 'Contrasts.json', contrasts)
    baseline = read(DATA / 'TrainingBaseline.json')
    assert all(digest(ROOT / f['path']) == f['sha256'] for f in baseline['frozenFiles'])
    plan = {'modelId': 'google/gemma-3-4b-it', 'modelRevision': read(OUT / 'MixedComparison.json')['modelRevision'],
            'sourceApprovedPairs': 6, 'derivedTasks': 21, 'taskKinds': {'npc_line': 6, 'player_options': 3, 'player_option': 12},
            'newIndependentScenes': 0, 'trainingGroups': ['guide-family'], 'validationGroupsExcluded': ['gambler-family'],
            'finalTestGroupsExcluded': ['duelist-family'], 'draftsExcluded': 0 if approved else 5,
            'approvedNormalAuxiliaryTasks': 5 if approved else 0, 'draftsAutoApprove': False,
            'trainingExecuted': False, 'presentationChanged': False, 'frozenFilesVerified': len(baseline['frozenFiles']),
            'sourceFiles': {n: digest(DATA / n) for n in ('CurrentInputCandidates.json', 'TrainingPairs.jsonl', 'DatasetSplitPlan.json')},
            'sourceOutputSha256': digest(OUT / 'GemmaRoleTaskComparison.json'),
            'records': records, 'risks': ['One training scene family only', 'Single-choice tasks are auxiliary; retain four-choice array tasks',
                                          'Normal rendering drafts are not actual-game state changes', 'No new quality or local-180-second claim']}
    dump(folder / 'Manifest.json', plan)
    esc = html.escape
    panels = []
    for contrast in contrasts:
        panels.append('<section><h2>' + esc(contrast['sourceId']) + '</h2><pre>' + esc(json.dumps(contrast['meaning'], ensure_ascii=False, indent=2)) + '</pre><p>' + esc(contrast['approvedNpc']) + '</p><ol>' + ''.join('<li>' + esc(s) + '</li>' for s in contrast['approvedOptions']) + '</ol></section>')
    table = ''.join('<tr><td>' + esc(d['id']) + '</td><td>' + esc(d['normal']) + '</td><td>' + esc(d['approvedGarbled']) + '</td></tr>' for d in drafts)
    doc = '<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Gemma 의미 보존 학습 자료 검토</title><style>body{font:18px/1.7 system-ui;max-width:1150px;margin:30px auto;padding:24px;background:#142337;color:#edf4fb}pre{white-space:pre-wrap;font:15px/1.7 monospace}section{background:#243a54;padding:20px;margin:20px 0}h1,h2{color:#8cddc2}td,th{border:1px solid #667a91;padding:12px}table{border-collapse:collapse;width:100%}a{color:#9ccfff}</style><h1>전체 4/5 · Gemma 의미 보존 자료 보완</h1><p>기존 Gemma 유지. 승인된 게임 6쌍 → NPC 6개·선택지 배열 3개·개별 응답 12개 = 21개 작업. 새 독립 장면을 21개 확보했다는 뜻은 아닙니다. 기존 승인 정답은 글자까지 그대로 사용합니다.</p><p>플레이어 응답의 발화자·의도는 task에, NPC 대사 정보는 npcContext에 분리했습니다. 새 표현·실패한 생성물은 학습 자료에 넣지 않았습니다. 도박꾼과 대련은 학습에서 제외했습니다.</p><h2>검토할 정상/난독 대비 자료 5개</h2><p>정상 표현은 Explanation 원문입니다. 새 보조 작업으로 추가하려면 검토가 필요해 현재 학습에서 제외했습니다. 실제 난독 장면을 정상 문장으로 바꾸는 요청이 아니라, 표기 조건별 차이를 익히는 표현 연습입니다. 오른쪽은 이미 승인한 난독 정답입니다.</p><table><tr><th>대상</th><th>정상 표현 · 추가 검토</th><th>기존 승인 난독</th></tr>' + table + '</table><h2>기존 승인 의미와 정답</h2>' + ''.join(panels) + '<p>자료 준비 완료 · 이번 보완 자료의 학습은 미실행. 발표용 모델·프롬프트·실행 설정 및 구조 유지.</p></html>'
    if approved:
        doc = doc.replace('검토할 정상/난독 대비 자료 5개', '사용자가 승인한 정상/난독 대비 자료 5개')
        doc = doc.replace('새 보조 작업으로 추가하려면 검토가 필요해 현재 학습에서 제외했습니다.',
                          '별도 Gemma 학습 자료로 사용하도록 승인됐습니다. 정상 5개와 기존 난독 정답의 같은 형식 표현 연습 5개를 추가해 총 31개 작업으로 학습합니다.')
        doc = doc.replace('정상 표현 · 추가 검토', '정상 표현 · 승인 완료')
        doc = doc.replace('자료 준비 완료 · 이번 보완 자료의 학습은 미실행.', '자료 승인·구성 완료 · 실제 실행 상태와 결과는 TrainingExperiment.json 및 학습 결과 보고서에 별도로 기록합니다.')
    (OUT / 'GemmaMeaningCurriculumReview.html').write_text(doc, encoding='utf8')
    print(json.dumps({k: v for k, v in plan.items() if k not in ('records', 'sourceFiles')}, ensure_ascii=False))


if __name__ == '__main__':
    main()
