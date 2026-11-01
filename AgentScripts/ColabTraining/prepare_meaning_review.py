"""Render the JSON wording review against its existing Explanation authority."""
import hashlib
import html
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'outputs/AiTraining'
def main():
    review_path=ROOT/'Tests/LocalStoryBenchmark/Colab/MeaningWordingReviewDraft.json'
    draft=json.loads(review_path.read_text(encoding='utf8'))
    source=(ROOT/draft['sourceFile']).resolve()
    assert source.is_relative_to(ROOT.resolve())
    assert hashlib.sha256(source.read_bytes()).hexdigest()==draft['sourceSha256']
    explanation=json.loads(source.read_text(encoding='utf-8-sig'))
    def find(node):
        if isinstance(node,dict):
            if node.get('semanticText')==draft['authoritativeMeaning']['semanticText']:return node
            for value in node.values():
                match=find(value)
                if match is not None:return match
        elif isinstance(node,list):
            for value in node:
                match=find(value)
                if match is not None:return match
    meaning=find(explanation)
    assert meaning==draft['authoritativeMeaning']
    rows=[json.loads(line) for line in (ROOT/'Tests/LocalStoryBenchmark/TrainingPairs.jsonl').read_text(encoding='utf8').splitlines()]
    prior=next(row for row in rows if row['id']==draft['sourcePairId'])
    answer=draft['proposedOutput']
    assert json.loads(prior['messages'][-1]['content'])['cards'][0]['options']==answer['cards'][0]['options']
    status='사용자가 새 문구를 승인했고 Unity 내보내기 검증을 통과했습니다. 별도 학습 실험에서 사용했으며 게임 원문과 설정은 변경하지 않았습니다.' if draft.get('unityExportValidated') else '표현 검토 중이며 검증·승인 전에는 학습에 넣지 않습니다.'
    (OUT/'MeaningWordingReview.html').write_text('<!doctype html><html lang="ko"><meta charset="utf-8"><title>전체 4/5 · 정답 표현 검토</title><style>body{font:18px/1.8 system-ui;max-width:1000px;margin:40px auto;padding:24px}pre{white-space:pre-wrap;background:#f2f5fa;padding:24px}p{padding:16px;background:#fff0c9}</style><h1>전체 4/5 · Explanation 의미를 유지한 정답 표현 검토</h1><p>'+html.escape(draft['rationale'])+'</p><h2>확정된 뜻</h2><pre>'+html.escape(meaning['semanticText'])+'</pre><h2>정답 표현</h2><pre>'+html.escape(json.dumps(answer,ensure_ascii=False,indent=2))+'</pre><p>'+html.escape(status)+'</p></html>',encoding='utf8')
    print(json.dumps({'review':str(review_path),'answerApprovedByUser':draft['answerApprovedByUser'],'unityExportValidated':draft.get('unityExportValidated',False)},ensure_ascii=False))
if __name__=='__main__':main()
