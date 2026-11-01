"""Render saved Guide diagnostic outputs, without treating schema as meaning."""
import html
import hashlib
import json
from pathlib import Path


def main():
    root = Path(__file__).resolve().parents[2]
    out = root / 'outputs/AiTraining'
    data = json.loads((out / 'GuideInputDiagnosticComparison.json').read_text(encoding='utf8'))
    rows = data['rows']
    metrics = {v: {'requests': sum(r['variant'] == v for r in rows),
                   'structurePassed': sum(r['variant'] == v and r['structureError'] is None for r in rows),
                   'generationSeconds': sum(r['seconds'] for r in rows if r['variant'] == v)}
               for v in ('original', 'compact')}
    sections = []
    esc = html.escape
    for case in data['plan']['cases']:
        for seed in data['plan']['seeds']:
            columns = []
            for v in ('original', 'compact'):
                r = next(r for r in rows if r['caseId'] == case['id'] and r['seed'] == seed and r['variant'] == v)
                columns.append('<article><h3>' + ('기존 입력' if v == 'original' else '단순화 입력') + '</h3><p>' +
                               f"입력 {r['inputTokens']}토큰 · 생성 {r['seconds']:.1f}초 · 형식 " + esc(r['structureError'] or '통과') +
                               '</p><pre>' + esc(r['raw']) + '</pre></article>')
            sections.append('<h2>' + esc(case['id']) + f' · seed {seed}</h2><div class="grid">' + ''.join(columns) + '</div>')
    summary = {'metrics': metrics, 'trainingExecuted': False, 'unseenEvaluation': False,
               'presentationChanged': False, 'local180SecondTargetMeasured': False,
               'conclusion': 'Format and meaning require separate review. Combined input intervention cannot isolate prompt length causality.'}
    (out / 'GuideInputDiagnosticMetrics.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2) + '\n', encoding='utf8')
    (out / 'GuideInputDiagnostic.html').write_text('<!doctype html><html lang="ko"><meta charset="utf-8"><title>Guide 입력 비교</title><style>body{font:17px/1.7 system-ui;max-width:1300px;margin:32px auto;padding:20px;background:#f3f5f8}.grid{display:grid;grid-template-columns:1fr 1fr;gap:18px}article{background:white;padding:18px}pre{white-space:pre-wrap;overflow-wrap:anywhere}h2{margin-top:40px}@media(max-width:800px){.grid{grid-template-columns:1fr}}</style><h1>전체 4/5 · Guide 입력 단순화 진단 12회</h1><p>같은 학습된 Kanana 어댑터·3건·두 시드. 추가 학습 없음. Guide는 학습 자료이므로 미학습 일반화 평가가 아닙니다.</p><p>기존 입력과 짧은 지시문·예시/앞 대사 제거·선택지 명세 필드 구분을 함께 비교합니다. 형식 통과는 의미 통과가 아니며, 길이만의 원인 효과를 분리할 수 없습니다. 발표용 설정은 유지했습니다.</p><pre>' + esc(json.dumps(metrics, ensure_ascii=False, indent=2)) + '</pre>' + ''.join(sections) + '</html>', encoding='utf8')
    review_path = root / 'Tests/LocalStoryBenchmark/Colab/GuideInputDiagnosticReview.json'
    if review_path.exists():
        review = json.loads(review_path.read_text(encoding='utf8'))
        assert review['sourceSha256'] == hashlib.sha256((out / 'GuideInputDiagnosticComparison.json').read_bytes()).hexdigest()
        target = out / 'GuideInputDiagnostic.html'
        document = target.read_text(encoding='utf8')
        conclusion = '<p><b>검토 결론: 형식 0/6 → 2/6, 전체 요구 통과 0/6 → 0/6. 발표용 적용 보류.</b> 난독 대사의 뜻은 유지했지만 정상 문장으로 출력해 의도적 난독 발음을 잃었습니다. 길이만의 문제로 단정할 수 없습니다.</p>'
        document = document.replace('<h1>전체 4/5 · Guide 입력 단순화 진단 12회</h1>', '<h1>전체 4/5 · Guide 입력 단순화 진단 12회</h1>' + conclusion)
        details = '<h2>출력별 의미·상태 검토 (Codex 판정)</h2>' + ''.join('<p><b>' + esc(f"{r['caseId']} / {r['seed']} / {r['variant']}") + '</b>: ' + esc(r['reviewerNote']) + '</p>' for r in review['rows'])
        target.write_text(document.replace('</html>', details + '</html>'), encoding='utf8')
    print(json.dumps(metrics, ensure_ascii=False))


if __name__ == '__main__':
    main()
