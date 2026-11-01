"""Render the current-material Gemma training result and manual checkpoint review."""
import hashlib
import html
import json
from pathlib import Path


def main():
    root = Path(__file__).resolve().parents[2]
    out = root / 'outputs/AiTraining'
    source = out / 'GemmaMeaningCurriculumComparison.json'
    data = json.loads(source.read_text(encoding='utf8'))
    review = json.loads((root / 'Tests/LocalStoryBenchmark/Colab/MeaningCurriculum/ResultReview.json').read_text(encoding='utf8'))
    assert review['sourceSha256'] == hashlib.sha256(source.read_bytes()).hexdigest()
    assert data['modelId'] == 'google/gemma-3-4b-it'
    assert len(data['cases']) == 11 and len(data['rows']) == len(review['rows']) == 33
    esc = html.escape
    titles = {'parent': '직전 Gemma 어댑터', 'midpoint': '보존된 중간 체크포인트', 'trained': '64단계 학습 후'}
    sections = []
    for case in data['cases']:
        panels = []
        for arm in data['evaluationIdentity']['arms']:
            row = next(r for r in data['rows'] if (r['id'], r['arm']) == (case['id'], arm))
            note = next(r for r in review['rows'] if (r['id'], r['arm']) == (case['id'], arm))
            assert note['basicFormatPassed'] == (row['structureError'] is None)
            panels.append('<article><h3>' + titles[arm] + '</h3><pre>' + esc(row['raw']) + '</pre><p>' + esc(note['note']) + f'</p><p>생성 {row["seconds"]:.2f}초</p></article>')
        sections.append('<h2>' + esc(case['id']) + ' · ' + esc(case['splitGroup']) + '</h2><div class="grid">' + ''.join(panels) + '</div><details><summary>실제 입력과 기준 정답</summary><pre>' + esc(json.dumps(case['messages'], ensure_ascii=False, indent=2)) + '</pre></details>')
    doc = '<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Gemma 현재 자료 개선 학습</title><style>body{font:18px/1.7 system-ui;max-width:1500px;margin:30px auto;padding:24px;background:#142337;color:#edf4fb}pre{white-space:pre-wrap;overflow-wrap:anywhere;font:15px/1.7 monospace}.grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:16px}article{background:#243a54;padding:20px}h1,h2{color:#8cddc2}a{color:#9ccfff}@media(max-width:1000px){.grid{grid-template-columns:1fr}}</style><h1>전체 4/5 · 현재 자료로 Gemma 개선 학습</h1><p>' + esc(review['conclusionKorean']) + '</p><pre>' + esc(json.dumps(review['summary'], ensure_ascii=False, indent=2)) + '</pre><p>승인된 게임 6쌍에서 21개 작업을 분해하고, 사용자 승인 정상 표기 5개와 기존 난독 정답을 같은 표현 연습 형식으로 짝지어 총 31개 작업을 학습했습니다. 새 문구·독립 장면·실패 생성물을 추가하지 않았습니다. NPC 문맥과 플레이어 응답 역할은 입력에서 분리했습니다.</p><p>직전 Gemma 어댑터에서 새 optimizer로 64단계 이어 학습한 warmstart이며 정확한 중단 재개가 아닙니다. 같은 입력·NF4·정밀도·결정적 생성 조건에서 직전 어댑터와 보존된 중간·최종 결과를 확인했습니다. 체크포인트 보존·선택 기록은 검토 JSON에 남겼습니다.</p><p>Guide 본 작업 5개 및 정상/난독 표현 연습 2개는 학습 자료 진단입니다. 도박꾼 4개는 이 모델 학습에 쓰지 않았으나 앞선 실험에서 관찰한 중간 검증 장면입니다. 대련 최종 평가 자료는 학습에 넣지 않았습니다. 의미 판정은 Codex 검토이며 사용자 정답 승인이 아닙니다.</p><p>HF NF4와 발표용 QAT GGUF는 실행 조건이 다릅니다. 변환·양자화·Unity 및 로컬 장면 전체 180초는 별도 검증이 필요합니다. 발표용 모델·프롬프트·실행 설정·프로젝트 구조는 유지했습니다.</p>'
    (out / 'GemmaMeaningCurriculumComparison.html').write_text(doc + ''.join(sections) + '</html>', encoding='utf8')
    print(json.dumps(review['summary'], ensure_ascii=False))


if __name__ == '__main__':
    main()
