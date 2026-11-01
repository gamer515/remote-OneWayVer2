"""Write the user-facing five-stage progress without touching game settings."""
import argparse
import base64
import html
import json
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'outputs/AiTraining'
TITLES = ['공개 한국어 자료 선별·분리', '소규모 학습·미학습 문장 비교', '입력 길이·지시 혼재 비교', 'Explanation 기반 게임 자료 보완', '변환·Unity 품질·3분 검증']

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--stage', type=int, default=2, choices=range(1, 6))
    parser.add_argument('--status', default='진행 중', choices=['진행 중', '검토 대기', '보류'])
    parser.add_argument('--detail', default='공개 교정문 학습 66개, 중간 검증 25개, 최종 평가 19개. Colab 시험 학습 실행 중.')
    args = parser.parse_args()
    result = {'currentStage': args.stage, 'totalStages': 5, 'updatedAt': datetime.now(timezone.utc).isoformat(), 'detail': args.detail,
              'stages': [{'number': i, 'title': title, 'status': '완료' if i < args.stage else args.status if i == args.stage else '대기'} for i, title in enumerate(TITLES, 1)],
              'presentationSettingsChanged': False, 'local180SecondTargetMeasured': False}
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT/'TrainingProgress.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf8')
    rows = ''.join(f'<li><strong>전체 {s["number"]}/5 · {html.escape(s["title"])}</strong><span>{s["status"]}</span></li>' for s in result['stages'])
    links = [('PublicDataReview/KoLLA-16908784/Stage1Review.html','자료 선별'), ('PublicCorrectionComparison.html','교정 학습 비교'), ('PromptExamplesAblation.html','입력 비교'), ('MeaningWordingReview.html','정답 표현'), ('MixedComparison.html','혼합 학습 비교')]
    link_html = ' · '.join(f'<a style="color:#8ccaff" href="{path}">{label}</a>' for path,label in links if (OUT/path).exists())
    (OUT/'TrainingProgress.html').write_text('<!doctype html><html lang="ko"><meta charset="utf-8"><title>학습 진행 현황</title><style>body{font:18px/1.7 system-ui;background:#101827;color:#e8edf6;max-width:960px;margin:50px auto;padding:24px}li{background:#202e43;padding:20px;margin:16px 0;list-style:none;border-radius:12px}ul{padding:0}span{float:right;color:#7edac3}p{color:#c3cddd}</style><h1>전체 '+str(args.stage)+'/5 '+html.escape(args.status)+'</h1><p>'+html.escape(args.detail)+'</p><ul>'+rows+'</ul><p>발표용 설정·프로젝트 구조 유지 · 게임 적용 전 로컬 180초 목표 별도 검증</p><p>'+link_html+'</p></html>', encoding='utf8')
    script = ROOT/'AgentScripts/ColabTraining/evaluate_public_correction.py'
    cell = 'import base64\npublic_eval_path=PUBLIC_BUNDLE/"evaluate_public_correction.py"\npublic_eval_path.write_bytes(base64.b64decode('+repr(base64.b64encode(script.read_bytes()).decode())+'))\nrun_command([sys.executable,str(public_eval_path),"--bundle",str(PUBLIC_BUNDLE),"--run",str(PUBLIC_RUN)])\n'
    (OUT/'public-evaluation-cell.py').write_text(cell, encoding='utf8')
    print(json.dumps(result, ensure_ascii=False))

if __name__ == '__main__':
    main()
