"""Track fast selection-to-training work separately from the completed Gemma pilot."""
import argparse
import html
import json
from datetime import datetime,timezone
from pathlib import Path
import shutil

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'outputs/AiTraining'
CONFIG=ROOT/'Tests/LocalStoryBenchmark/Colab'
def main():
    parser=argparse.ArgumentParser();parser.add_argument('--stage',type=int,default=2);parser.add_argument('--detail',required=True);parser.add_argument('--status',default='진행 중');args=parser.parse_args()
    titles=['미평가 후보 2개·기준 입력 고정','기본 성능 비교 후 학습 후보 즉시 선정','선정 모델 72쌍 소규모 학습','학습 전후 의미 평가·GGUF·로컬 호환 확인','Unity 저장·분기·로컬 장면 전체 3분 검증']
    data={'experimentId':'model-screening-20261031','updatedAt':datetime.now(timezone.utc).isoformat(),'deadlineSeoul':'2026-11-10','priority':'후보를 최대한 빨리 정하고 학습까지 진행; 달력상 목표일까지 대기하지 않음','currentStage':args.stage,'detail':args.detail,'stages':[{'number':i,'title':title,'status':'완료' if i<args.stage else args.status if i==args.stage else '대기'} for i,title in enumerate(titles,1)],'presentationChanged':False,'local180SecondTargetMeasured':False}
    (CONFIG/'ModelScreeningStatus.json').write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    backup=OUT/'TrainingProgress-PilotCompleted.html'
    if not backup.exists() and (OUT/'TrainingProgress.html').exists():shutil.copyfile(OUT/'TrainingProgress.html',backup)
    items=''.join(f'<li><strong>전체 {row["number"]}/5 · {html.escape(row["title"])}</strong><span>{row["status"]}</span></li>' for row in data['stages'])
    doc='<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>새 모델 선정·학습 진행</title><style>body{font:18px/1.7 system-ui;max-width:1000px;margin:40px auto;padding:24px;background:#101827;color:#e8edf6}li{list-style:none;padding:20px;background:#202e43;margin:16px 0;border-radius:12px}ul{padding:0}span{float:right;color:#7edac3}a{color:#8ccaff}</style><h1>새 모델 선정·학습 · 전체 '+str(args.stage)+'/5</h1><p>'+html.escape(data['priority'])+'</p><p>'+html.escape(args.detail)+'</p><ul>'+items+'</ul><p>선별 계획: LFM2 / Kanana, 모델당 최대 41회. 실제 완료 수와 중단 사유는 결과 보고서에 기록.</p><p>11월 10일은 최종 여유일이며 단계 사이에 날짜를 기다리지 않습니다. <a href="KananaTrainingSummary.html">학습 결과 보고서</a></p><p>발표용 모델·프롬프트·런타임·구조 유지. 품질과 로컬 장면 전체 180초는 별도 검증.</p><p><a href="PilotSummary.html">이전 Gemma 학습 결과</a> · <a href="TrainingProgress-PilotCompleted.html">이전 진행 기록</a></p></html>'
    (OUT/'TrainingProgress.html').write_text(doc,encoding='utf8')
    print(json.dumps({'stage':args.stage,'deadlineSeoul':data['deadlineSeoul'],'detail':args.detail},ensure_ascii=False))
if __name__=='__main__':main()
