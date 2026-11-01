"""Apply a versioned rubric and explicit reviewer observations to saved outputs."""
import hashlib
import html
import json
from pathlib import Path
from evaluate import structure

ROOT = Path(__file__).resolve().parents[2]
CONFIG = ROOT / 'Tests/LocalStoryBenchmark/Colab'
OUT = ROOT / 'outputs/AiTraining'


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    source = OUT / 'CandidateComparison-kanana-1.5-2.1b.json'
    approved = CONFIG / 'KananaAnswerReviewDrafts.json'
    data = json.loads(source.read_text(encoding='utf8'))
    answers = json.loads(approved.read_text(encoding='utf8'))
    assert answers['reviewedByUser'] and all(r['reviewedByUser'] for r in answers['drafts'])
    for r in answers['drafts']:
        assert hashlib.sha256(json.dumps(r['proposedOutput'], ensure_ascii=False, sort_keys=True, separators=(',', ':')).encode('utf8')).hexdigest() == r['approvedOutputSha256']
    rubric = {
        'version': 1, 'sceneGroup': 'gambler-family', 'split': 'validation',
        'authority': 'User-approved answers derived from Story. Detailed Explanation card meanings remain empty and are not overwritten.',
        'checks': [
            {'id': 'format', 'method': 'existing evaluate.structure', 'pass': 'One JSON object; exact card count and local indexes; text strings; exact option count; option strings; existing length limits.'},
            {'id': 'instruction', 'caseId': 'gambler-main/0', 'method': 'reviewer', 'pass': 'NPC instructs player: choose heads or tails with gear, then press yellow button. Do not claim already done or switch actor.'},
            {'id': 'resultRule', 'caseId': 'gambler-main/0', 'method': 'reviewer', 'pass': 'Coin spins on board, stops on contact with floor, upward face determines result. Preserve sequence; invent no odds.'},
            {'id': 'choiceQuestion', 'caseId': 'gambler-main/2', 'method': 'reviewer', 'pass': 'NPC asks player to choose heads versus tails for wager; not a repeated instruction or placeholder.'},
            {'id': 'playerChoices', 'caseId': 'gambler-main/2', 'method': 'reviewer', 'pass': 'Four actual player replies, ordered heads, tails, request rules, refuse gambling. Metadata intent/reference does not count as a reply. No completed button action.'},
            {'id': 'scope', 'method': 'reviewer', 'pass': 'Only requested cards; NPC direct speech; no borrowed lamp scene, narration, copied input, new rewards, or new probabilities.'},
            {'id': 'expression', 'method': 'reviewer', 'pass': 'Natural understandable Korean, no role prefixes or literal schema placeholders; approved exact wording is not mandatory.'},
        ],
        'aggregation': 'All applicable checks must pass for acceptance. Partial readable fragments may be described but cannot repair invalid JSON or replace missing responses.',
        'limits': 'This is retrospective exploratory review, not blind final evaluation. No fresh generations, training, runtime grammar, or local timing measurement.',
        'reviewer': 'Codex; rubric operationalizes approved meaning, individual ratings have not been user-reviewed.',
    }
    (CONFIG / 'KananaValidationRubric.json').write_text(json.dumps(rubric, ensure_ascii=False, indent=2) + '\n', encoding='utf8')
    # Explicit reviews of the eight observed outputs, never a keyword meaning classifier.
    failed_checks = {
        ('gambler-main/0', 12345, 'base'): ['instruction', 'resultRule', 'scope', 'expression'],
        ('gambler-main/0', 67890, 'base'): ['instruction', 'resultRule', 'scope', 'expression'],
        ('gambler-main/0', 12345, 'adapter'): ['instruction', 'resultRule', 'scope'],
        ('gambler-main/0', 67890, 'adapter'): ['instruction', 'resultRule', 'scope', 'expression'],
        ('gambler-main/2', 12345, 'base'): ['playerChoices', 'scope', 'expression'],
        ('gambler-main/2', 67890, 'base'): ['choiceQuestion', 'playerChoices', 'scope'],
        ('gambler-main/2', 12345, 'adapter'): ['choiceQuestion', 'playerChoices', 'scope', 'expression'],
        ('gambler-main/2', 67890, 'adapter'): ['choiceQuestion', 'playerChoices', 'scope'],
    }
    korean_notes = [
        '인물·장면 입력 메타데이터만 출력해 요청된 대사가 없다.',
        '입력과 예시를 대량 재출력하고 승률 우위를 추가했다. NPC가 조작하는 서술과 미완성 문장도 있다.',
        '무관한 등불 반환과 자유로운 여행을 추가했다. 기어·노란 버튼·정지 시 결과 조건이 빠졌다.',
        '등불 예시를 복사하고 NPC의 삼인칭 행동 설명을 출력했다. 위쪽 면이라는 조건은 일부 남지만 정지 조건과 조작 안내가 빠졌다.',
        '여러 JSON으로 앞 카드와 ‘직접 대사’ 틀 문구를 반복했다. 마지막에 선택 질문은 있지만 네 응답이 없다.',
        '규칙 질문과 거절 문구는 남았지만 선택지가 객체다. 앞 두 응답은 버튼을 이미 눌렀다는 사건을 추가했고 NPC는 질문 대신 안내를 반복했다.',
        '‘직접 대사’라는 틀 문구뿐이며 질문과 네 응답이 없다.',
        '네 의도 명세의 순서는 남았지만 실제 플레이어 응답을 생성하지 않고 명세를 복사했다. NPC도 앞선 조작 안내를 반복했다.',
    ]
    observations = {key: (note, failed) for (key, failed), note in zip(failed_checks.items(), korean_notes)}
    cases = {r['id']: r for r in data['inputs']['cases']}
    rows = []
    for r in data['requests']:
        note, failed = observations[(r['caseId'], r['seed'], r['arm'])]
        _, error = structure(r['raw'], cases[r['caseId']])
        rows.append({'caseId': r['caseId'], 'seed': r['seed'], 'arm': r['arm'], 'formatPassed': error is None,
                     'formatError': error, 'reviewerFailedChecks': failed, 'reviewerNote': note,
                     'accepted': error is None and not failed, 'rawSha256': hashlib.sha256(r['raw'].encode('utf8')).hexdigest()})
    result = {'rubricVersion': 1, 'provenance': {str(p.relative_to(ROOT)): digest(p) for p in (source, approved, CONFIG / 'KananaValidationRubric.json')},
              'reviewer': 'Codex', 'userReviewedRatings': False, 'freshGenerationExecuted': False, 'trainingAdded': 0,
              'presentationChanged': False, 'local180SecondTargetMeasured': False, 'rows': rows,
              'summary': {arm: {'requests': sum(r['arm'] == arm for r in rows), 'formatPassed': sum(r['arm'] == arm and r['formatPassed'] for r in rows),
                                'accepted': sum(r['arm'] == arm and r['accepted'] for r in rows)} for arm in ('base', 'adapter')}}
    (CONFIG / 'KananaSemanticReview.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf8')
    sections = ''.join('<tr><td>' + html.escape(f"{r['caseId']} / {r['seed']} / {r['arm']}") + '</td><td>' + html.escape(r['formatError'] or '통과') + '</td><td>' + html.escape(r['reviewerNote']) + '</td></tr>' for r in rows)
    (OUT / 'KananaSemanticReview.html').write_text('<!doctype html><html lang="ko"><meta charset="utf-8"><title>승인 정답 기준 평가</title><style>body{font:17px/1.7 system-ui;max-width:1200px;margin:40px auto;padding:20px}td,th{padding:14px;border:1px solid #ccd3dc}table{border-collapse:collapse}li{margin:12px 0}</style><h1>전체 4/5 · 승인 정답 기준 평가</h1><p>기존 출력 8건 재검토. 학습 전 0/4, 학습 후 0/4 통과. 새 생성·학습·게임 적용은 하지 않았습니다.</p><p>문구 일치 대신 의미·사건 순서·선택 의도를 검토합니다. Explanation의 빈 의미를 작성하지 않았습니다. 사람 승인 정답과 Codex의 평가 판정은 구분합니다.</p><ul>' + ''.join('<li><b>' + html.escape(c['id']) + '</b>: ' + html.escape(c['pass']) + '</li>' for c in rubric['checks']) + '</ul><table><tr><th>출력</th><th>형식 검사</th><th>의미 검토</th></tr>' + sections + '</table><p>검증용 Gambler 자료는 학습에 추가하지 않았습니다. 이 결과는 Unity GBNF 적용 결과나 로컬 180초 측정치가 아닙니다.</p></html>', encoding='utf8')
    print(json.dumps(result['summary'], ensure_ascii=False))


if __name__ == '__main__':
    main()
