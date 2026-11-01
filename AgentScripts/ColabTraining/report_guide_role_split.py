"""Render the role-separated diagnostic and its explicit semantic review."""
import hashlib
import html
import json
from pathlib import Path
from run_guide_input_diagnostic import check_output


def main():
    root = Path(__file__).resolve().parents[2]
    out = root / 'outputs/AiTraining'
    source = out / 'GuideRoleSplitComparison.json'
    data = json.loads(source.read_text(encoding='utf8'))
    review = json.loads((root / 'Tests/LocalStoryBenchmark/Colab/GuideRoleSplitReview.json').read_text(encoding='utf8'))
    assert hashlib.sha256(source.read_bytes()).hexdigest() == review['sourceSha256']
    cases = {c['id']: c for c in data['plan']['cases']}
    esc = html.escape
    sections = []
    for row in data['rows']:
        case = cases[row['caseId']]
        _, error = check_output(row['raw'], case)
        assert error == row['structureError']
        rating = next(r for r in review['rows'] if r['caseId'] == row['caseId'] and r['seed'] == row['seed'])
        sections.append('<section><h2>' + esc(row['caseId']) + f" · seed {row['seed']}</h2><p>입력 {row['inputTokens']}토큰 · 생성 {row['seconds']:.2f}초 · 형식: " + esc(error or '통과') + '</p><pre>' + esc(row['raw']) + '</pre><p>' + esc(rating['note']) + '</p><details><summary>실제 입력</summary><pre>' + esc(case['separatedMessages'][0]['content']) + '</pre></details></section>')
    document = '''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>NPC·선택지 분리 결과</title><style>body{font:18px/1.65 system-ui;max-width:1100px;margin:30px auto;padding:24px;background:#132236;color:#ecf3fb}h1,h2{color:#8bdfc1}table{border-collapse:collapse;width:100%;margin:20px 0}th,td{border:1px solid #53677d;padding:12px}section{background:#20334c;padding:20px;margin:24px 0;border-radius:12px}pre{white-space:pre-wrap;overflow-wrap:anywhere;font:15px/1.7 monospace}a{color:#a7d3ff}.note{background:#32485f;padding:18px;border-radius:10px}</style><h1>전체 4/5 · NPC·선택지 분리 실험 완료</h1><p>같은 Kanana 학습 어댑터 · NPC 3건/선택지 2건 × 두 시드 = 10회</p><table><tr><th>작업</th><th>형식 통과</th><th>의미·상태 유지</th><th>새 표현 변환 성공</th></tr><tr><td>NPC 대사</td><td>6/6</td><td>2/6</td><td>0/6</td></tr><tr><td>플레이어 선택지</td><td>0/4</td><td>0/4</td><td>0/4</td></tr></table><div class="note"><b>채택 보류 · 추가 학습 없음</b><br>의미를 유지한 두 건은 같은 승인 원문을 그대로 복사했습니다.<br>난독: 뜻은 남았지만 난독 발음 소실. 정체: 삼인칭 해설 추가.<br>선택지: 명세 객체 복사 또는 네 응답을 하나로 합침.</div><p>학습에 사용된 Guide 진단입니다. 미학습 일반화·Unity·로컬 전체 180초는 검증하지 않았습니다. 짧은 문장과 문자열 배열을 따로 검사하므로 기존 cards JSON 통과율과 직접 비교하지 않습니다. 평가 판정은 Codex 검토이며 사용자 승인은 아닙니다.</p><p>발표용 모델·프롬프트·실행 설정 유지. 분리만으로 해결되지 않아 이 결과를 근거로 재학습을 시작하지 않습니다.</p>'''
    (out / 'GuideRoleSplit.html').write_text(document + ''.join(sections) + '</html>', encoding='utf8')
    print(json.dumps({'verifiedRequests': len(data['rows']), 'summary': review['summary']}, ensure_ascii=False))


if __name__ == '__main__':
    main()
