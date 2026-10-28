"""Render actual input AB outputs without rewriting model responses."""
import html
import json
from pathlib import Path

root = Path(__file__).resolve().parents[2]
source = root / "outputs/AiTraining/InputComparison.json"
data = json.loads(source.read_text(encoding="utf-8"))
escape = lambda value: html.escape(str(value))
body = []
for case in data["inputs"]["cases"]:
    for seed in data["inputs"]["plan"]["seeds"]:
        columns = []
        for arm, title in (("current", "기존 입력"), ("separated", "구분된 입력")):
            row = next(r for r in data["requests"] if r["caseId"] == case["id"] and r["seed"] == seed and r["arm"] == arm)
            columns.append(f'<article><h3>{title}</h3><p>{row["seconds"]:.2f}초 · 입력 {row["inputTokens"]} / 출력 {row["outputTokens"]}토큰</p><p>엄격 형식: {escape(row["structureError"] or "통과")}</p><pre>{escape(row["raw"])}</pre></article>')
        body.append(f'<section><h2>{escape(case["id"])} · seed {seed}</h2><details><summary>기존 원문·선택지</summary><pre>{escape(json.dumps(case["sourceCards"], ensure_ascii=False, indent=2))}</pre></details><div class="grid">{"".join(columns)}</div></section>')
document = '''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>입력 구분 A/B 비교</title><style>body{font:16px/1.6 system-ui;max-width:1300px;margin:30px auto;padding:20px;background:#f5f6f8;color:#202631}.grid{display:grid;grid-template-columns:1fr 1fr;gap:20px}article{background:white;border:1px solid #ddd;border-radius:10px;padding:20px}pre{white-space:pre-wrap;overflow-wrap:anywhere}section{margin:30px 0}.warn{background:#fff1cd;padding:18px}@media(max-width:800px){.grid{grid-template-columns:1fr}}</style><h1>입력 구분 A/B · 실제 생성 결과</h1><p class="warn">같은 HF NF4 기본 모델에서 LoRA 없이 입력 구분만 비교했습니다. 게임 GBNF 없이 생성했으며 Explanation 미작성 항목은 원문을 보조 근거로 삼습니다. 형식과 의미는 따로 평가해야 합니다. 로컬 전체 장면 3분 검증과 최종 품질 승인은 아직입니다.</p>'''
target = source.with_suffix(".html")
target.write_text(document + "".join(body) + "</html>", encoding="utf-8")
print(json.dumps({"requests": len(data["requests"]), "html": str(target)}))
