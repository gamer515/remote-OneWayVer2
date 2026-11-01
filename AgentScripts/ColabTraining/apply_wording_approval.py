"""Record the explicit user approval; rebuild inputs separately via Unity exporter."""
import json
import argparse
from pathlib import Path
import shutil

ROOT=Path(__file__).resolve().parents[2]
DATA=ROOT/'Tests/LocalStoryBenchmark'
def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--finalize-export',action='store_true')
    args=parser.parse_args()
    review_path=DATA/'Colab/MeaningWordingReviewDraft.json'
    review=json.loads(review_path.read_text(encoding='utf8'))
    assert review['proposedOutput']['cards'][0]['text'] and review['sourcePairId']
    if args.finalize_export:
        candidates=json.loads((DATA/'CurrentInputCandidates.json').read_text(encoding='utf8'))
        current=next(row for row in candidates['entries'] if row['id']==review['sourcePairId'])
        assert current['expectedOutput']==review['proposedOutput'] and current['trainingEligible']
        assert all(current['validation'][key] for key in ('tryBuild','sameBindings','situation','lengths'))
        review.update(trainingEligible=True,pending=None,unityExportValidated=True)
        review_path.write_text(json.dumps(review,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
        path=DATA/'AnswerReviewDrafts.json'
        original=json.loads(path.read_text(encoding='utf8'))
        row=next(row for row in original['drafts'] if row['id']==review['sourcePairId'])
        row.update(currentInputRebuildRequired=False,trainingEligible=True)
        path.write_text(json.dumps(original,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
        page=ROOT/'outputs/AiTraining/MeaningWordingReview.html'
        page.write_text(page.read_text(encoding='utf8').replace('새 문구는 승인 전이며 학습에 넣지 않았습니다.','사용자가 새 문구를 승인했습니다. 기존 Unity 내보내기 모듈의 검증을 통과해 별도 학습 실험에 사용합니다.'),encoding='utf8')
        print(json.dumps({'wordingApproved':True,'unityExportValidated':True,'gameApplied':False}))
        return
    backup=ROOT/'outputs/AiTraining/PriorGameDataV1'
    backup.mkdir(parents=True,exist_ok=True)
    for name in ('AnswerReviewDrafts.json','CurrentInputCandidates.json','TrainingPairs.jsonl'):
        if not (backup/name).exists():shutil.copyfile(DATA/name,backup/name)
    review.update(status='user_approved_wording',answerApprovedByUser=True,trainingEligible=False,
        approval={'dateSeoul':'2026-10-29','userReply':'새 문구 승인','scope':'NPC wording only; existing choices/order and Explanation meaning preserved'},
        pending='Rebuild and validate through existing Unity export module before training')
    review_path.write_text(json.dumps(review,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    source=DATA/'AnswerReviewDrafts.json'
    data=json.loads(source.read_text(encoding='utf8'))
    row=next(row for row in data['drafts'] if row['id']==review['sourcePairId'])
    assert row['proposedOutput']['cards'][0]['options']==review['proposedOutput']['cards'][0]['options']
    row['proposedOutput']=review['proposedOutput']
    row['approval']=review['approval']
    row['reviewedByUser']=True
    row['currentInputRebuildRequired']=True
    row['trainingEligible']=False
    row['currentInputSemanticReview']['status']='consistent_with_current_input_by_agent'
    row['currentInputSemanticReview']['notes']='User approved 새 문구 승인. NPC question preserves Explanation semanticText 알아듣지 못하겠지?; player perception remains garbled, Loki comprehension remains normal. Existing four choices unchanged. Re-export current inputs before training.'
    source.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    print(json.dumps({'wordingApproved':True,'unityExportPending':True,'gameApplied':False}))
if __name__=='__main__':main()
