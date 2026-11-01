import html
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'outputs/AiTraining'
def main():
    data=json.loads((OUT/'PromptExamplesAblation.json').read_text(encoding='utf8'))
    rows=[]
    for row in data['requests']:
        label=data['inputs']['plan']['armLabels'][row['arm']]
        rows.append('<tr>'+''.join('<td>'+html.escape(str(v))+'</td>' for v in (row['caseId'],row['seed'],label,row['inputTokens'],round(row['seconds'],2),row['raw']))+'</tr>')
    note='예시 제거만으로 개선되지 않았습니다. 두 조건 모두 도입부의 2개 카드를 1개로 줄이고 없던 선택지를 만들었습니다. 선택 장면은 기존 입력에서 문자열 선택지를 복사했고, 예시 제거 시 두 시드 모두 명세 객체를 출력했습니다. 모두 코드 펜스를 포함해 엄격한 JSON 검사는 통과하지 못했습니다. 이 비교에는 게임의 GBNF가 없으므로 Unity 런타임 전체의 실패율로 해석하지 않습니다. 예시 제거는 길이와 예시 정보량을 함께 바꾸므로 순수 길이 효과를 단정할 수 없습니다.'
    (OUT/'PromptExamplesAblation.html').write_text('<!doctype html><html lang="ko"><meta charset="utf-8"><title>전체 3/5 · 예시 제거 비교</title><style>body{font:16px/1.7 system-ui;max-width:1500px;margin:30px auto;padding:20px;background:#f4f6f9}td,th{border:1px solid #ccd;padding:12px;vertical-align:top;white-space:pre-wrap}table{border-collapse:collapse;background:white}p{background:#fff0c9;padding:20px}</style><h1>전체 3/5 · 기존 입력과 예시만 제거한 입력</h1><p>'+html.escape(note)+'</p><table><tr><th>장면</th><th>시드</th><th>조건</th><th>입력 토큰</th><th>초</th><th>실제 출력</th></tr>'+''.join(rows)+'</table></html>',encoding='utf8')
    review={'stage':'3/5','reviewer':'Codex','requests':8,'onlyChangedField':'examples','adoptInputChange':False,'presentationChanged':False,'conclusion':note}
    (ROOT/'Tests/LocalStoryBenchmark/Colab/PromptExamplesAblationReview.json').write_text(json.dumps(review,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    print(json.dumps(review,ensure_ascii=False))

if __name__=='__main__': main()
