"""Pair historical adapter outputs with fixed-request adapter-disabled diagnostics."""
import hashlib
import html
import json
from pathlib import Path


def main():
    root = Path(__file__).resolve().parents[2]
    out = root / 'outputs/AiTraining'
    base = json.loads((out / 'GuideRoleSplitBaseComparison.json').read_text(encoding='utf8'))
    adapter = json.loads((out / 'GuideRoleSplitComparison.json').read_text(encoding='utf8'))
    review = json.loads((root / 'Tests/LocalStoryBenchmark/Colab/GuideRoleSplitBaseReview.json').read_text(encoding='utf8'))
    for key in ('modelId', 'modelRevision', 'cases', 'sampling', 'seeds'):
        assert base['plan'][key] == adapter['plan'][key], 'Comparison condition changed: ' + key
    assert base['identity']['adapterEnabled'] is False
    for key in ('experimentLockSha256', 'adapterFiles'):
        assert base['identity'][key] == adapter['identity'][key]
    assert review['baseSourceSha256'] == hashlib.sha256((out / 'GuideRoleSplitBaseComparison.json').read_bytes()).hexdigest()
    esc = html.escape
    sections = []
    for r in base['rows']:
        a = next(x for x in adapter['rows'] if x['caseId'] == r['caseId'] and x['seed'] == r['seed'])
        assert a['inputTokens'] == r['inputTokens']
        note = next(x for x in review['rows'] if x['caseId'] == r['caseId'] and x['seed'] == r['seed'])
        sections.append('<h2>' + esc(r['caseId']) + f" · seed {r['seed']}</h2><div class=grid><article><h3>원본 (어댑터 끔)</h3><pre>" + esc(r['raw']) + '</pre><p>' + esc(note['note']) + '</p></article><article><h3>학습 어댑터 켬 (기존 기록)</h3><pre>' + esc(a['raw']) + '</pre></article></div>')
    doc = '<!doctype html><html lang="ko"><meta charset="utf-8"><title>Kanana 원본·학습 비교</title><style>body{font:18px/1.7 system-ui;max-width:1250px;margin:30px auto;padding:24px;background:#142337;color:#edf4fb}pre{white-space:pre-wrap;overflow-wrap:anywhere;font:15px/1.7 monospace}.grid{display:grid;grid-template-columns:1fr 1fr;gap:20px}article{background:#243a54;padding:20px}h1,h2{color:#8cddc2}table{border-collapse:collapse}td,th{padding:12px;border:1px solid #78899a}@media(max-width:850px){.grid{grid-template-columns:1fr}}</style><h1>전체 4/5 · 원본 Kanana와 학습 어댑터 비교</h1><p>' + esc(review['conclusionKorean']) + '</p><pre>' + esc(json.dumps(review['summary'], ensure_ascii=False, indent=2)) + '</pre><p>같은 입력·시드·샘플링·NF4·기반 모델 revision. 기존 어댑터 출력과 이번 원본 출력을 비교합니다. 패키지 버전·GPU 검증 기록은 별도 보존했습니다. Guide 학습 장면 진단이며 최종 일반화·Unity·로컬 전체 180초 검증이 아닙니다. 의미 판정은 Codex 검토입니다.</p>'
    (out / 'GuideRoleSplitBaseComparison.html').write_text(doc + ''.join(sections) + '</html>', encoding='utf8')
    print(json.dumps(review['summary'], ensure_ascii=False))


if __name__ == '__main__':
    main()
