"""Compare a new native-Llama candidate against fixed historical role-split requests."""
import hashlib
import html
import json
from pathlib import Path


def main():
    root = Path(__file__).resolve().parents[2]
    out = root / 'outputs/AiTraining'
    source = out / 'MidmRoleSplitComparison.json'
    result = json.loads(source.read_text(encoding='utf8'))
    base = json.loads((out / 'GuideRoleSplitBaseComparison.json').read_text(encoding='utf8'))
    review = json.loads((root / 'Tests/LocalStoryBenchmark/Colab/MidmRoleSplitReview.json').read_text(encoding='utf8'))
    assert review['sourceSha256'] == hashlib.sha256(source.read_bytes()).hexdigest()
    for key in ('cases', 'seeds', 'sampling', 'variants'):
        assert result['plan'][key] == base['plan'][key], key
    assert result['identity']['adapterEnabled'] is False
    assert result['identity']['adapterFiles'] == {}
    assert len(result['rows']) == len(review['rows']) == 10
    esc = html.escape
    sections = []
    for row in result['rows']:
        old = next(r for r in base['rows'] if (r['caseId'], r['seed']) == (row['caseId'], row['seed']))
        note = next(r for r in review['rows'] if (r['caseId'], r['seed']) == (row['caseId'], row['seed']))
        assert note['formatPassed'] == (row['structureError'] is None)
        sections.append(f'<h2>{esc(row["caseId"])} · seed {row["seed"]}</h2><div class="grid"><article><h3>Mi:dm 원본</h3><pre>{esc(row["raw"])}</pre><p>{esc(note["note"])}</p><p>생성 {row["seconds"]:.2f}초 · 입력 {row["inputTokens"]}토큰</p></article><article><h3>이전 Kanana 원본</h3><pre>{esc(old["raw"])}</pre></article></div>')
    doc = '<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Mi:dm 분리 요청 비교</title><style>body{font:18px/1.7 system-ui;max-width:1250px;margin:30px auto;padding:24px;background:#142337;color:#edf4fb}pre{white-space:pre-wrap;overflow-wrap:anywhere;font:15px/1.7 monospace}.grid{display:grid;grid-template-columns:1fr 1fr;gap:20px}article{background:#243a54;padding:20px}h1,h2{color:#8cddc2}a{color:#9ccfff}@media(max-width:850px){.grid{grid-template-columns:1fr}}</style><h1>전체 4/5 · Mi:dm 분리 요청 진단</h1>'
    doc += '<p>' + esc(review['conclusionKorean']) + '</p><pre>' + esc(json.dumps(review['summary'], ensure_ascii=False, indent=2)) + '</pre>'
    doc += '<p>동일한 요청 내용·시드·샘플링·NF4·생성 제한. 모델별 공식 채팅 템플릿과 토크나이저는 다릅니다. Mi:dm에 불필요한 token_type_ids를 제외했고, 첫 시도 오류는 보존했습니다. 의미 평가는 Codex 검토이며 사용자 정답 승인이 아닙니다. Guide는 기존 학습 장면 진단으로 미학습 일반화 평가가 아닙니다. Colab T4 시간은 로컬 장면 전체 180초 검증을 대신하지 않습니다. 생성 결과는 학습 자료에 추가하지 않았습니다.</p><p><a href="https://huggingface.co/K-intelligence/Midm-2.0-Mini-Instruct">공식 모델 카드</a> · <a href="TrainingProgress.html">전체 진행</a></p>'
    (out / 'MidmRoleSplitComparison.html').write_text(doc + ''.join(sections) + '</html>', encoding='utf8')
    print(json.dumps(review['summary'], ensure_ascii=False))


if __name__ == '__main__':
    main()
