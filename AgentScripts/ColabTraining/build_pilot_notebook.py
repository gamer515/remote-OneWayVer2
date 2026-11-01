"""Make a clean replay notebook; preserve the original six-pair notebook."""
import copy
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'outputs/AiTraining'
def main():
    original=json.loads((ROOT/'Tests/LocalStoryBenchmark/Colab/OneWay_Colab_Training.ipynb').read_text(encoding='utf8'))
    keep={'oneway-00','oneway-01','oneway-02','oneway-03','oneway-04','oneway-05'}
    cells=[copy.deepcopy(cell) for cell in original['cells'] if cell.get('id') in keep]
    cells[0]['source']=['# OneWay · 공개 한국어 교정 및 승인 게임 정답 실험\n','기존 발표용 설정과 별도 실험입니다. GPU 및 Drive와 HF_TOKEN 접근이 필요합니다. 토큰은 출력하지 않습니다. 원본 실험은 보존하며 완료 기록과 번들 해시가 일치할 때만 재실행을 건너뜁니다. 최종 평가 자료는 사용하지 않습니다.\n']
    for index,(title,name) in enumerate([
        ('전체 2/5 · 공개 교정 66쌍 학습','public-training-cell.py'),
        ('전체 2/5 · 미학습 25문장 비교','public-evaluation-cell.py'),
        ('전체 3/5 · 예시 포함·제거 비교','prompt-ablation-cell.py'),
        ('전체 4/5 · 공개 교정 66쌍 + 승인 게임 정답 6쌍','mixed-training-cell.py'),
        ('전체 4/5 · 게임 및 일반 교정 검증','mixed-evaluation-cell.py')]):
        cells.append({'cell_type':'markdown','metadata':{},'id':f'pilot-title-{index}','source':['## '+title+'\n']})
        cells.append({'cell_type':'code','metadata':{},'id':f'pilot-code-{index}','source':(OUT/name).read_text(encoding='utf8').splitlines(keepends=True),'execution_count':None,'outputs':[]})
    for cell in cells:
        if cell['cell_type']=='code':
            cell.update(execution_count=None,outputs=[])
            compile(''.join(cell['source']),cell['id'],'exec')
    original['cells']=cells
    original['metadata'].setdefault('colab',{})['name']='OneWay_PublicCorrection_Pilot.ipynb'
    target=ROOT/'Tests/LocalStoryBenchmark/Colab/OneWay_PublicCorrection_Pilot.ipynb'
    target.write_text(json.dumps(original,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    print(json.dumps({'notebook':str(target),'codeCells':sum(cell['cell_type']=='code' for cell in cells),'includesOutputs':False}))
if __name__=='__main__':main()
