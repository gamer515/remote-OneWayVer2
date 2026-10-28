"""Display actual tone diagnostic results and reviewed public correction candidates."""
import html
import json
from pathlib import Path

root = Path(__file__).resolve().parents[2]
output = root / "outputs/AiTraining"
escape = lambda value: html.escape(str(value))
sections = []
for filename, title in (("ShortStyleGeneralResult.json", "일반 문장"), ("ShortStyleResult.json", "게임 원문")):
    result = json.loads((output / filename).read_text(encoding="utf-8"))
    cards = "".join(f'<article><h3>seed {row["seed"]} · {row["seconds"]:.2f}초</h3><p>{escape(row["raw"])}</p></article>' for row in result["requests"])
    sections.append(f'<section><h2>{title}</h2><blockquote>{escape(result["plan"]["sourceText"])}</blockquote><details><summary>실제 요청</summary><pre>{escape(result["plan"]["messages"][0]["content"])}</pre></details><div class="grid">{cards}</div></section>')
rows = [json.loads(line) for line in (output / "PublicDataReview/KoLLA-16908784/selected-review.jsonl").read_text(encoding="utf-8").splitlines()]
table = "".join(f'<tr><td>{escape(row["id"])}</td><td>{escape(row["sourceText"])}</td><td>{escape(row["targetText"])}</td></tr>' for row in rows)
document = '''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>짧은 말투 변환 시험 · 공개 교정 자료 검토</title><style>body{font:16px/1.7 system-ui;max-width:1200px;margin:24px auto;padding:20px;background:#f5f6f8;color:#202631}.grid{display:grid;grid-template-columns:1fr 1fr;gap:16px}article,blockquote{background:white;border:1px solid #ddd;border-radius:10px;padding:18px;margin:12px 0}h1,h2,h3{line-height:1.4}.note{background:#fff1cd;padding:16px}pre{white-space:pre-wrap}td,th{padding:12px;border:1px solid #ddd}table{border-collapse:collapse;background:white}section{margin:30px 0}@media(max-width:800px){.grid{grid-template-columns:1fr}}</style><h1>짧은 말투 변환 · 실제 결과</h1><p class="note">일반 문장은 두 번 모두 자연스럽게 변환됐습니다. 게임 문장은 두 번 모두 의미가 바뀌었습니다. 긴 프롬프트만이 원인이라고 단정할 수 없습니다. 같은 HF NF4 기본 모델로 LoRA 없이 실행했으며 발표용 게임 설정은 유지했습니다. 로컬 3분 검증은 별도입니다.</p>'''
document += "".join(sections) + '<section><h2>공개 교정 자료 · 우선 검토 13쌍</h2><p>KoLLA v2 원본 1,418문장, 교정 주석 2,828개를 재구성했습니다. 두 교정 주석 일치·짧은 수정 후보 176개 중 먼저 32개를 살펴 13쌍을 골랐습니다. 아직 학습에 넣지 않았습니다. 이 표는 교정문을 새로 만든 것이 아니라 발행 자료를 재구성한 것입니다.</p><p><a href="https://zenodo.org/records/16908784">원본 KoLLA v2</a> · 공식 API 라이선스 GPL-3.0-or-later · 원본 문서 묶음 복원과 의미 검토 후 분리합니다.</p><table><tr><th>원본 ID</th><th>교정 전</th><th>발행 자료의 교정문</th></tr>' + table + '</table><p class="note">AI Hub 자료는 국외 반출 별도 합의 조건이 있어 Colab에 전송하지 않았습니다. 공개 자료도 무검수 일괄 학습하지 않습니다.</p></section></html>'
(output / "ShortStyleAndDataReview.html").write_text(document, encoding="utf-8")
print(json.dumps({"diagnosticRequests": 4, "publicReviewPairs": len(rows), "html": str(output / "ShortStyleAndDataReview.html")}))
