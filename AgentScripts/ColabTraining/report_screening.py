"""Render actual candidate outputs next to unchanged source messages."""
import html
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'outputs/AiTraining'
def main():
    inputs=json.loads((OUT/'ModelScreeningInputs.json').read_text(encoding='utf8'))
    decision_path=ROOT/'Tests/LocalStoryBenchmark/Colab/CandidateSelectionDecision.json'
    decision=json.loads(decision_path.read_text(encoding='utf8')) if decision_path.exists() else {}
    cases={case['id']:case for case in inputs['cases']}
    metrics={};sections=[]
    for candidate in inputs['candidates']:
        path=OUT/('Screening-'+candidate['id']+'.json')
        if not path.exists():continue
        data=json.loads(path.read_text(encoding='utf8'))
        game=[r for r in data['requests'] if r['kind']=='game']
        correction=[r for r in data['requests'] if r['kind']=='correction']
        metrics[candidate['id']]={'completed':len(data['requests']),'planned':data['plannedRequests'],
            'gameStrictPass':sum(r['structureError'] is None for r in game),'gameRequests':len(game),
            'codeFenceDiagnosticPass':sum(r['codeFenceDiagnosticError'] is None for r in game),
            'publicExactMatch':sum(r['exactTarget'] for r in correction),'publicRequests':len(correction),
            'stoppedReason':data['stoppedReason'],'candidateSelectedForTraining':candidate['id']==decision.get('candidateId'),'meaningAccepted':None}
        rows=[]
        for row in data['requests']:
            case=cases[row['caseId']]
            reference=case.get('meaningSource') or case.get('sourceCards') or case.get('target') or case['messages']
            rows.append('<tr>'+''.join('<td>'+html.escape(str(v))+'</td>' for v in [row['caseId'],row['seed'],round(row['seconds'],2),json.dumps(reference,ensure_ascii=False),row['raw'],row['structureError']])+'</tr>')
        sections.append('<h2>'+html.escape(candidate['modelId'])+'</h2><pre>'+html.escape(json.dumps(metrics[candidate['id']],ensure_ascii=False,indent=2))+'</pre><table><tr><th>항목</th><th>시드</th><th>초</th><th>의미 근거·비교 대상</th><th>실제 출력</th><th>엄격한 구조 검사</th></tr>'+''.join(rows)+'</table>')
    (OUT/'ModelScreeningComparison.html').write_text('<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>새 모델 선별 비교</title><style>body{font:16px/1.7 system-ui;max-width:1600px;margin:30px auto;padding:20px;background:#f3f6fa}table{border-collapse:collapse;width:100%;background:white}td,th{border:1px solid #ccd5df;padding:12px;vertical-align:top;white-space:pre-wrap;overflow-wrap:anywhere}pre{white-space:pre-wrap}.note{padding:20px;background:#fff0c9}</style><h1>전체 2/5 · 새 모델 기본 성능 선별</h1><p class="note">추가 학습 전의 고정 입력 비교입니다. Guide는 승인된 진단 자료이며 새로운 최종 평가로 주장하지 않습니다. 공개 교정은 같은 25개 중간 검증 자료입니다. 문자열 일치는 정확도와 다르며 구조 통과만으로 의미를 승인하지 않습니다. Colab NF4 시간은 로컬 게임 전체 180초 측정치가 아닙니다.</p>'+''.join(sections)+'</html>',encoding='utf8')
    (OUT/'ModelScreeningMetrics.json').write_text(json.dumps(metrics,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    print(json.dumps(metrics,ensure_ascii=False))
if __name__=='__main__':main()
