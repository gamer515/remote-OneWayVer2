"""Prepare review-only corrections from observed Kanana failures; never train."""
import hashlib
import html
import json
from pathlib import Path
from evaluate import structure

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'outputs/AiTraining'
CONFIG = ROOT / 'Tests/LocalStoryBenchmark/Colab'


def main():
    target = CONFIG / 'KananaAnswerReviewDrafts.json'
    if target.exists() and json.loads(target.read_text(encoding='utf8')).get('reviewedByUser'):
        raise ValueError('Approved review already exists; preserve its answers and approval record.')
    comparison = OUT / 'CandidateComparison-kanana-1.5-2.1b.json'
    story = ROOT / 'Assets/Decision_YYS/Resources/Chapters/Chapter_00/Chapter_00_01/Gambler/Story.json'
    explanation = story.with_name('Explanation.json')
    data = json.loads(comparison.read_text(encoding='utf8'))
    authored = json.loads(story.read_text(encoding='utf8'))['gambler']['MainStory']
    meanings = json.loads(explanation.read_text(encoding='utf8'))['gambler']['coreMeaning']
    outputs = [
        {'cards': [
            {'index': 0, 'text': '기어로 앞면이나 뒷면을 고른 다음, 노란 버튼을 누르게.', 'options': []},
            {'index': 1, 'text': '동전은 보드 위를 돌다가 바닥에 닿으면 멈춘다네. 그때 위를 향한 면이 결과가 되지.', 'options': []},
        ]},
        {'cards': [
            {'index': 0, 'text': '자, 앞면과 뒷면 중 어느 쪽에 걸겠나?',
             'options': ['앞면으로 하겠어.', '뒷면으로 하겠어.', '규칙을 설명해 줘.', '도박은 하지 않겠어.']},
        ]},
    ]
    drafts = []
    for case, output in zip(data['inputs']['cases'], outputs):
        for card in case['sourceCards']:
            original = next(r for r in authored if 'card:' + str(r['ID']) == card['eventId'])
            assert original['Text'] == card['text']
            assert original.get('options', []) == card['options']
            assert meanings[str(original['ID'])] == card['meaning']
        _, error = structure(json.dumps(output, ensure_ascii=False), case)
        if error:
            raise ValueError(error)
        drafts.append({
            'id': case['id'], 'status': 'draft_pending_user_review',
            'reviewedByUser': False, 'trainingEligible': False,
            'sceneGroup': 'gambler-family', 'split': 'validation_review_only',
            'meaningAuthority': 'Explanation first; these card meanings are empty. Story is supplementary evidence, not an invented Explanation.',
            'currentInputMessages': case['messages'], 'sourceCards': case['sourceCards'],
            'actualFailures': [r for r in data['requests'] if r['caseId'] == case['id'] and r['arm'] == 'adapter'],
            'proposedOutput': output, 'structureCheck': 'passed',
            'approvalScope': 'Wording and intended meaning only; no automatic training or split reassignment.',
        })
    result = {
        'schemaVersion': 1, 'purpose': 'Human answer review only; not a training dataset',
        'reviewedByUser': False, 'trainingEligible': False,
        'provenance': {str(p.relative_to(ROOT)).replace('\\', '/'): hashlib.sha256(p.read_bytes()).hexdigest()
                       for p in (comparison, story, explanation)},
        'preserve': ['NPC instruction versus player reply', 'Gear then yellow button; do not claim already pressed',
                     'Coin stopping and upward-face result', 'Four choice meanings and order; no odds or rewards invented'],
        'splitPolicy': 'Keep gambler-family validation. Training requires an explicit new split manifest and a separate untouched evaluation family; old runs retain their original splits.',
        'drafts': drafts,
    }
    target = CONFIG / 'KananaAnswerReviewDrafts.json'
    target.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf8')
    sections = []
    for row in drafts:
        sections.append('<h2>' + html.escape(row['id']) + '</h2><h3>원문 근거</h3>' +
                        '<pre>' + html.escape(json.dumps(row['sourceCards'], ensure_ascii=False, indent=2)) +
                        '</pre><h3>정답 초안</h3><pre>' + html.escape(json.dumps(row['proposedOutput'], ensure_ascii=False, indent=2)) +
                        '</pre><details><summary>실제 실패 출력</summary>' + ''.join('<pre>' + html.escape(r['raw']) + '</pre>' for r in row['actualFailures']) + '</details>')
    (OUT / 'KananaAnswerReview.html').write_text(
        '<!doctype html><html lang="ko"><meta charset="utf-8"><title>Kanana 정답 초안 검토</title>'
        '<style>body{font:18px/1.7 system-ui;max-width:1000px;margin:40px auto;padding:24px}pre{white-space:pre-wrap;background:#f1f4f8;padding:20px}</style>'
        '<h1>전체 4/5 · 실패 사례 정답 초안 2건</h1><p>승인 전 초안이며 학습에 넣지 않습니다. Explanation 세부 의미는 비어 있어 Story를 보조 근거로 표시합니다. 검증용 묶음을 유지합니다.</p>' + ''.join(sections) + '</html>', encoding='utf8')
    print(json.dumps({'drafts': len(drafts), 'structurePassed': len(drafts), 'trainingEligible': False, 'output': str(target)}, ensure_ascii=False))


if __name__ == '__main__':
    main()
