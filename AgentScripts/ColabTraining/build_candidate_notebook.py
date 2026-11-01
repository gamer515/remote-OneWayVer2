"""Standalone replay of fixed screening, selected training and paired evaluation."""
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'outputs/AiTraining'
CONFIG=ROOT/'Tests/LocalStoryBenchmark/Colab'
def main():
    decision=json.loads((CONFIG/'CandidateSelectionDecision.json').read_text(encoding='utf8'))
    candidate=decision['candidateId']
    cells=[{'cell_type':'markdown','metadata':{},'source':['# 미평가 모델 선별 → 선정 후보 소규모 학습\n','사용자 목표는 후보를 빠르게 정하고 학습까지 진행하는 것입니다. 날짜를 기다리지 않습니다. 후보 선정은 학습 실험용이며 배포용 채택과 다릅니다. 기존 발표용 설정과 과거 실험은 보존합니다. Drive 연결과 GPU가 필요합니다. 토큰을 코드에 넣지 마세요.\n']}]
    for index,(label,filename) in enumerate([('전체 2/5 · 고정 입력 선별','model-screening-cell.py'),('전체 3/5 · 선정 후보 72쌍 학습','candidate-training-'+candidate+'-cell.py'),('전체 4/5 · 학습 전후 비교','candidate-evaluation-cell.py')]):
        code=(OUT/filename).read_text(encoding='utf8')
        compile(code,filename,'exec')
        cells.extend([{'cell_type':'markdown','metadata':{},'source':['## '+label+'\n']},{'cell_type':'code','metadata':{},'execution_count':None,'outputs':[],'source':code.splitlines(keepends=True)}])
    notebook={'nbformat':4,'nbformat_minor':5,'metadata':{'accelerator':'GPU','kernelspec':{'display_name':'Python 3','name':'python3'},'language_info':{'name':'python'}},'cells':cells}
    path=CONFIG/'OneWay_Candidate_Training.ipynb'
    path.write_text(json.dumps(notebook,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    print(json.dumps({'candidate':candidate,'codeCells':3,'includesOutputs':False,'path':str(path)}))
if __name__=='__main__':main()
