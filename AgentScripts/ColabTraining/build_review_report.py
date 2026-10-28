"""Attach evidence-checked reviewer notes to an unchanged paired-generation report."""
import argparse
import hashlib
import html
import importlib.util
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--comparison", type=Path, required=True)
    parser.add_argument("--review", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    result = json.loads(args.comparison.read_text(encoding="utf-8-sig"))
    review = json.loads(args.review.read_text(encoding="utf-8-sig"))
    if result["inputs"]["plan"]["evaluationId"] != review["evaluationId"]:
        raise ValueError("Review belongs to another experiment")
    rows = {(r["caseId"], r["seed"], r["arm"]): r for r in result["requests"]}
    if len(rows) != 16:
        raise ValueError("Missing or duplicate paired results")
    for finding in review["findings"]:
        if finding["quote"] not in rows[(finding["caseId"], finding["seed"], finding["arm"])]["raw"]:
            raise ValueError("Reviewer quote not found in raw evidence: " + finding["quote"])
    spec = importlib.util.spec_from_file_location("paired_report", Path(__file__).with_name("evaluate.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.report(result, args.output)
    document = args.output.read_text(encoding="utf-8")
    escaped = lambda value: html.escape(str(value))
    counts = {arm: sum(r["structureError"] is None for r in result["requests"] if r["arm"] == arm) for arm in ("base", "adapter")}
    block = "<section><h2>검토 결과</h2><p><strong>" + escaped(review["recommendation"]) + "</strong></p>"
    block += "<p>검토: Codex · 사용자 품질 승인 아님. 엄격 JSON/개수/문자열 형식: 학습 전 " + str(counts["base"]) + "/8, 학습 후 " + str(counts["adapter"]) + "/8. 이는 게임 GBNF를 적용하지 않은 이번 실행의 수치입니다.</p>"
    block += "<details><summary>평가 조건과 한계 펼치기</summary><ul>" + "".join("<li>" + escaped(note) + "</li>" for note in review["limitations"]) + "</ul></details>"
    block += "<details><summary>실제 문구와 판단 근거 " + str(len(review["findings"])) + "건 펼치기</summary>"
    for finding in review["findings"]:
        block += "<article style='margin:12px 0'><b>" + escaped(finding["caseId"]) + " · " + str(finding["seed"]) + " · " + escaped(finding["arm"]) + "</b><blockquote>" + escaped(finding["quote"]) + "</blockquote><p>" + escaped(finding["assessment"]) + "</p></article>"
    block += "</details><nav>"
    for case in result["inputs"]["cases"]:
        for seed in result["inputs"]["plan"]["seeds"]:
            key = case["id"] + "-" + str(seed)
            anchor = "case-" + hashlib.sha256(key.encode()).hexdigest()[:12]
            title = escaped(case["id"]) + " · seed " + str(seed)
            document = document.replace("<h2>" + title + "</h2>", '<h2 id="' + anchor + '">' + title + "</h2>")
            block += '<a style="display:inline-block;margin:6px 12px 0 0" href="#' + anchor + '">' + title + "</a>"
    block += "</nav></section>"
    document = document.replace("<section>", block + "<section>", 1)
    document = document.replace("@media(max-width:1000px)", "@media(max-width:760px)")
    args.output.write_text(document, encoding="utf-8")
    audit = {"comparisonSha256": hashlib.sha256(args.comparison.read_bytes()).hexdigest(),
             "reviewSha256": hashlib.sha256(args.review.read_bytes()).hexdigest(), "verifiedQuotes": len(review["findings"]),
             "structuralPasses": counts, "requests": 16, "humanApproved": None}
    args.output.with_suffix(".audit.json").write_text(json.dumps(audit, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(audit))


if __name__ == "__main__":
    main()
