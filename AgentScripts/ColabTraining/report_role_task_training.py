"""Report the Gemma improvement experiment without treating memorization as generalization."""
import hashlib
import html
import json
from pathlib import Path


def main():
    root = Path(__file__).resolve().parents[2]
    out = root / 'outputs/AiTraining'
    source = out / 'GemmaRoleTaskComparison.json'
    result = json.loads(source.read_text(encoding='utf8'))
    review = json.loads((root / 'Tests/LocalStoryBenchmark/Colab/GemmaRoleTaskReview.json').read_text(encoding='utf8'))
    assert review['sourceSha256'] == hashlib.sha256(source.read_bytes()).hexdigest()
    assert result['modelId'] == 'google/gemma-3-4b-it'
    assert len(result['rows']) == 18 and len(result['cases']) == 9
    esc = html.escape
    parts = []
    for case in result['cases']:
        panels = []
        for arm, name in [('base', '학습 전'), ('trained', '집중 학습 후')]:
            row = next(r for r in result['rows'] if (r['id'], r['arm']) == (case['id'], arm))
            note = next(r for r in review['rows'] if (r['id'], r['arm']) == (case['id'], arm))
            assert note['basicFormatPassed'] == (row['structureError'] is None)
            panels.append('<article><h3>' + name + '</h3><pre>' + esc(row['raw']) + '</pre><p>' + esc(note['note']) + f'</p><p>생성 {row["seconds"]:.2f}초</p></article>')
        parts.append('<h2>' + esc(case['id']) + ' · ' + esc(case['splitGroup']) + '</h2><div class="grid">' + ''.join(panels) + '</div><details><summary>실제 입력·기존 승인 정답</summary><pre>' + esc(json.dumps(case['messages'], ensure_ascii=False, indent=2)) + '</pre></details>')
    doc = '<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Gemma 집중 학습 결과</title><style>body{font:18px/1.7 system-ui;max-width:1250px;margin:30px auto;padding:24px;background:#142337;color:#edf4fb}pre{white-space:pre-wrap;overflow-wrap:anywhere;font:15px/1.7 monospace}.grid{display:grid;grid-template-columns:1fr 1fr;gap:20px}article{background:#243a54;padding:20px}h1,h2{color:#8cddc2}a{color:#a7d3ff}@media(max-width:850px){.grid{grid-template-columns:1fr}}</style><h1>전체 4/5 · Gemma 집중 학습</h1><p>' + esc(review['conclusionKorean']) + '</p><pre>' + esc(json.dumps(review['summary'], ensure_ascii=False, indent=2)) + '</pre><p>후보 비교를 중단하고 기존 Gemma 원본에 새 어댑터를 학습했습니다. 승인된 게임 6쌍을 대사·선택지 9개 작업으로 분해했으며 정답은 수정하지 않았습니다. 36 optimizer 단계이며 실제 Trainer 기록은 약 7.22 epoch입니다. 공개 교정 자료는 이번 집중 학습에 섞지 않았습니다.</p><p>Guide 5건은 학습 자료 진단, 도박꾼 4건은 학습에 쓰지 않은 장면의 중간 검증입니다. 도박꾼은 이미 여러 실험에서 관찰한 장면이므로 최종 미공개 평가가 아닙니다. 대련 최종 평가 자료는 학습에서 제외했습니다. 기초 형식·의미·새 표현은 별도 판정하며 의미 평가는 Codex 검토입니다.</p><p>Gemma HF NF4 학습 원본은 발표용 QAT GGUF와 실행 경로가 다릅니다. 변환·양자화·Unity 및 로컬 전체 180초 검증은 미실시입니다. 발표용 설정과 프로젝트 구조는 유지했습니다.</p>'
    (out / 'GemmaRoleTaskComparison.html').write_text(doc + ''.join(parts) + '</html>', encoding='utf8')
    print(json.dumps(review['summary'], ensure_ascii=False))


if __name__ == '__main__':
    main()
