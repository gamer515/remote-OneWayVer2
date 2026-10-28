"""Import acquired AI Hub ko/corrected rows into review-only JSONL, never training data."""
import argparse
import hashlib
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--release", required=True, help="Exact release of the acquired source")
    parser.add_argument("--terms-reference", required=True, help="Recorded applicable usage terms; does not assert approval")
    args = parser.parse_args()
    if args.output.exists():
        raise ValueError("Refusing to replace an existing review file")
    root = Path(__file__).resolve().parents[2]
    output = args.output.resolve()
    staging = (root / "outputs/AiTraining/PublicDataReview").resolve()
    if not output.is_relative_to(staging):
        raise ValueError("Output must stay in outputs/AiTraining/PublicDataReview")
    files = sorted(args.source.rglob("*.json")) + sorted(args.source.rglob("*.jsonl")) if args.source.is_dir() else [args.source]
    candidates = []
    seen = set()
    for path in files:
        raw = path.read_bytes()
        digest = hashlib.sha256(raw).hexdigest()
        content = raw.decode("utf-8-sig")
        rows = [json.loads(line) for line in content.splitlines() if line.strip()] if path.suffix == ".jsonl" else json.loads(content)
        rows = [rows] if isinstance(rows, dict) else rows
        if not isinstance(rows, list):
            raise ValueError("Unsupported source container: " + path.name)
        for index, row in enumerate(rows):
            if not isinstance(row, dict) or not isinstance(row.get("ko"), str) or not isinstance(row.get("corrected"), str):
                raise ValueError("Expected documented ko/corrected row: " + path.name)
            if not row["ko"].strip() or not row["corrected"].strip():
                continue
            key = (row["ko"], row["corrected"])
            if key in seen:
                continue
            seen.add(key)
            candidates.append({"sourceId": row.get("id"), "sourceFile": path.name, "sourceRow": index,
                "sourceSha256": digest, "sourceUrl": "https://aihub.or.kr/aihubdata/data/view.do?currMenu=115&dataSetSn=71477",
                "release": args.release, "termsReference": args.terms_reference, "inputText": row["ko"], "targetText": row["corrected"],
                "errorAnnotations": row.get("error"), "sourceSubset": row.get("dataSet"), "domain": row.get("domain"),
                "unchangedPair": row["ko"] == row["corrected"], "trainingEligible": False, "humanApproved": False,
                "meaningPreservationReviewed": False, "splitGroup": None, "split": "unassigned_review_only", "task": "normal_korean_correction"})
    if not candidates:
        raise ValueError("No candidates found")
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("x", encoding="utf-8") as stream:
        for candidate in candidates:
            stream.write(json.dumps(candidate, ensure_ascii=False) + "\n")
    print(json.dumps({"reviewCandidates": len(candidates), "trainingEligible": 0, "output": str(output)}))


if __name__ == "__main__":
    main()
