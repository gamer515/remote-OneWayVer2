"""Record quality-gate evidence; never convert or apply an unaccepted model."""
import hashlib
import html
import json
from datetime import datetime, timezone
from pathlib import Path
from evaluate import structure

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'outputs/AiTraining'
CONFIG=ROOT/'Tests/LocalStoryBenchmark/Colab'

def read(name):return json.loads((OUT/name).read_text(encoding='utf8'))
def main():
    public=read('PublicComparison.json')
    mixed_public=read('MixedPublicComparison.json')
    game=read('MixedComparison.json')
    train=read('MixedTrainingResult.json')
    baseline=json.loads((ROOT/'Tests/LocalStoryBenchmark/TrainingBaseline.json').read_text(encoding='utf8'))
    changed=[row['path'] for row in baseline['frozenFiles'] if hashlib.sha256((ROOT/row['path']).read_bytes()).hexdigest()!=row['sha256']]
    assert not changed
    cases={case['id']:case for case in game['inputs']['cases']}
    metrics={}
    for arm in ('base','adapter'):
        rows=[row for row in game['requests'] if row['arm']==arm]
        diagnostic=0
        for row in rows:
            raw=row['raw'].strip()
            if raw.startswith('```') and raw.endswith('```'):raw='\n'.join(raw.splitlines()[1:-1])
            _,error=structure(raw,cases[row['caseId']])
            diagnostic+=error is None
        metrics[arm]={'requests':len(rows),'strictRawJsonPass':sum(row['structureError'] is None for row in rows),'afterRemovingCodeFenceDiagnosticPass':diagnostic}
    status={'updatedAt':datetime.now(timezone.utc).isoformat(),'completedStages':[1,2,3,4],'stage5':'held_before_conversion_due_to_quality',
        'reviewer':'Codex','humanQualityAccepted':None,'gameQualityAccepted':False,'publicOnlyMetrics':public['metrics'],'mixedPublicMetrics':mixed_public['metrics'],'gameStructureMetrics':metrics,
        'findings':['Public correction targets match more often; this is not a grammar accuracy score because correct variants can differ',
            'Mixed model still collapses the two introductory cards into one and invents choices',
            'Mixed model invents a payout of two times for heads and one time for tails at seed 67890',
            'Choice scene still copies descriptions instead of writing direct player responses',
            'Examples-only removal did not fix the task; pure prompt-length causation is not established',
            'Mixed correction regresses 전심→점심 on kolla-v2-0282 despite some particle/ending improvements'],
        'wordingApproval':'새 문구 승인; Explanation semanticText and four choices preserved; Unity export validation 6/6 passed',
        'training':{'publicPairs':66,'approvedGamePairs':6,'steps':train['optimizerSteps'],'epochs':train['metrics']['epoch'],'peakAllocatedGiB':round(train['peakAllocatedBytes']/2**30,2),'adapterReloaded':train['adapterReloaded']},
        'frozenFilesVerified':len(baseline['frozenFiles']),'frozenFilesChanged':changed,'modelConverted':False,'gameApplied':False,'local180SecondTargetMeasured':False,
        'publicFinalTestEvaluated':False,'gameFinalTestEvaluatedThisPilot':False,
        'limits':['Gambler scenes are exploratory validation already inspected in earlier experiments','HF NF4 raw generation lacks Unity GBNF and differs from installed QAT GGUF','Full save/next-run branch application and local full-scene timing remain untested'],
        'nextDecision':'Compare a different suitable base model on the same validation cases before expanding the corpus; keep presentation configuration frozen',
        'driveAdapterFolder':'MyDrive/OneWayTraining/public-game-correction-v1/trial/adapter'}
    (CONFIG/'PilotExecutionStatus.json').write_text(json.dumps(status,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    columns=[('원본',mixed_public['metrics']['base']),('공개 교정 66쌍',public['metrics']['adapter']),('공개 66 + 게임 6쌍',mixed_public['metrics']['adapter'])]
    table=''.join(f'<tr><td>{label}</td><td>{metric["exactTargetCount"]}/25</td><td>{metric["totalTargetEditDistance"]}</td></tr>' for label,metric in columns)
    notes=''.join('<li>'+html.escape(note)+'</li>' for note in ['정답 문구와의 일치는 5→8→10개로 늘었습니다. 다른 올바른 표현도 있으므로 한국어 정확도 점수로 해석하지 않습니다.', '공개 자료와 게임 자료를 섞어도 도입부 2개 카드를 1개로 줄이고 없던 보상·선택지를 추가했습니다.', '선택지 4개는 원문 명세를 복사했습니다. 의미·형식·새 표현을 함께 만족하는 개선은 확인되지 않았습니다.', '혼합 모델은 일부 조사·말끝을 더 잘 유지했으나 점심을 전심으로 남기는 퇴행도 보였습니다.', '따라서 변환·게임 적용을 진행하지 않았습니다. 로컬 전체 장면 180초 목표도 아직 검증 전입니다.'])
    doc='<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>학습 실험 결과</title><style>body{font:18px/1.7 system-ui;max-width:1100px;margin:36px auto;padding:24px;background:#f4f6fa;color:#202b3d}table{border-collapse:collapse;width:100%;background:white}th,td{border:1px solid #ccd5e3;padding:16px}h1{line-height:1.4}.gate{background:#fff0ca;padding:24px;border-radius:12px}a{color:#245bc0}li{margin:12px 0}</style><h1>전체 4/5 실험 완료 · 5/5 게임 적용 보류</h1><p class="gate">표현 개선은 일부 확인했지만 게임의 의미·카드·선택지 오류가 남아 이번 모델은 채택하지 않았습니다. 발표용 설정과 고정 파일 99개를 유지했습니다.</p><h2>미학습 교정 25문장</h2><table><tr><th>조건</th><th>발행된 교정문과 정확 일치</th><th>정답까지 문자 편집 거리 합계</th></tr>'+table+'</table><ul>'+notes+'</ul><p>72쌍 1회 학습 · 36단계 · 어댑터 저장/재로딩 통과 · 학습 최고 할당 VRAM '+str(status['training']['peakAllocatedGiB'])+' GiB</p><p>다음 비교는 자료를 더 늘리기 전에 동일한 검증 입력에서 다른 기반 모델의 지시 준수·한국어·시간을 확인하는 것입니다.</p><p><a href="TrainingProgress.html">전체 진행</a> · <a href="MixedComparison.html">게임 실제 출력 비교</a> · <a href="PublicCorrectionComparison.html">공개 교정 학습 비교</a> · <a href="PromptExamplesAblation.html">예시 제거 비교</a> · <a href="MeaningWordingReview.html">승인된 난독 문구</a></p></html>'
    (OUT/'PilotSummary.html').write_text(doc,encoding='utf8')
    print(json.dumps({'gameQualityAccepted':False,'publicExact':{'base':5,'publicOnly':8,'mixed':10},'gameStructure':metrics,'frozenFilesVerified':99,'stage5':'held'},ensure_ascii=False))

if __name__=='__main__':main()
