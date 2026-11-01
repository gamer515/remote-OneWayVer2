"""Present raw held-out outputs alongside published corrections for review."""
import html
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'outputs/AiTraining'

def main():
    data = json.loads((OUT/'PublicComparison.json').read_text(encoding='utf8'))
    cases = {}
    for row in data['requests']:
        cases.setdefault(row['id'], {})[row['arm']] = row
    body = []
    for case_id, arms in cases.items():
        row = arms['base']
        body.append('<tr>'+''.join('<td>'+html.escape(str(value))+'</td>' for value in [case_id, row['source'], row['target'], arms['base']['raw'], arms['adapter']['raw']])+'</tr>')
    summary = html.escape(json.dumps(data['metrics'], ensure_ascii=False, indent=2))
    (OUT/'PublicCorrectionComparison.html').write_text('<!doctype html><html lang="ko"><meta charset="utf-8"><title>전체 2/5 · 공개 교정 학습 비교</title><style>body{font:16px/1.7 system-ui;max-width:1500px;margin:24px auto;padding:20px;background:#f4f6f9}td,th{border:1px solid #cbd3df;padding:12px;vertical-align:top;white-space:pre-wrap}table{border-collapse:collapse;background:white;width:100%}.note{background:#fff0c9;padding:20px}pre{background:white;padding:20px}</style><h1>전체 2/5 · 미학습 25문장 원본·학습 모델 비교</h1><p class="note">공개 교정 66쌍을 1회 학습했습니다. 이 25문장은 원본 글 단위로 학습 자료와 분리했습니다. 문자열 일치는 보조 지표이며 의미·자연스러움은 원문과 실제 출력을 함께 검토해야 합니다. 최종 19문장은 사용하지 않았습니다. 게임 적용과 로컬 장면 전체 180초는 아직 검증하지 않았습니다.</p><pre>'+summary+'</pre><table><tr><th>ID</th><th>입력</th><th>발행된 교정문</th><th>원본 모델</th><th>학습 모델</th></tr>'+''.join(body)+'</table></html>', encoding='utf8')
    print(json.dumps(data['metrics'], ensure_ascii=False))

if __name__ == '__main__':
    main()
