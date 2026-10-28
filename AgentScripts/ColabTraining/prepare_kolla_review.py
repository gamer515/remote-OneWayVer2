"""Reconstruct published M2 references and stage consensus corrections for review."""
from collections import Counter
from difflib import SequenceMatcher
import hashlib
import html
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]
FOLDER = ROOT / "outputs/AiTraining/PublicDataReview/KoLLA-16908784"


def detokenize(tokens):
    return re.sub(r"\s+([.,!?;:])", r"\1", " ".join(tokens)).strip()


def main():
    metadata = json.loads((FOLDER / "metadata.json").read_text(encoding="utf-8-sig"))
    source = FOLDER / "KoLLA_multi-refs.m2"
    raw = source.read_bytes()
    descriptor = next(item for item in metadata["files"] if item["key"] == source.name)
    assert "md5:" + hashlib.md5(raw).hexdigest() == descriptor["checksum"]
    digest = hashlib.sha256(raw).hexdigest()
    rows, reasons = [], Counter()
    blocks = raw.decode("utf-8-sig").strip().split("\n\n")
    for index, block in enumerate(blocks):
        lines = block.splitlines()
        if not lines or not lines[0].startswith("S "):
            raise ValueError("Invalid M2 source block")
        tokens = lines[0][2:].split()
        annotators = {}
        for line in lines[1:]:
            parts = line.split("|||")
            if not line.startswith("A ") or len(parts) != 6:
                raise ValueError("Invalid M2 annotation")
            edits = annotators.setdefault(parts[5], [])
            start, end = map(int, parts[0][2:].split())
            if parts[1] in ("noop", "UNK", "Um") or start == end == -1:
                continue
            if not 0 <= start <= end <= len(tokens):
                raise ValueError("Invalid annotation span")
            edits.append((start, end, parts[2], parts[1]))
        references = []
        for annotator, edits in annotators.items():
            result = list(tokens)
            boundary = len(tokens) + 1
            for start, end, replacement, kind in sorted(edits, reverse=True):
                if end > boundary:
                    raise ValueError("Overlapping edits")
                result[start:end] = [] if replacement == "-NONE-" else replacement.split()
                boundary = start
            references.append({"annotator": annotator, "text": detokenize(result), "edits": edits})
        original = detokenize(tokens)
        flags = []
        targets = {item["text"] for item in references}
        if len(annotators) < 2 or len(targets) != 1:
            flags.append("references_disagree_or_insufficient")
        target = references[0]["text"] if references else ""
        if original == target:
            flags.append("unchanged_control")
        if not 8 <= len(original) <= 100 or not 8 <= len(target) <= 110:
            flags.append("length_outside_pilot")
        if re.search(r"[A-Za-z0-9@]|https?://", original + target):
            flags.append("mixed_script_or_sensitive_pattern_review")
        if SequenceMatcher(None, original, target).ratio() < 0.78:
            flags.append("large_edit_review")
        reasons.update(flags)
        rows.append({"id": f"kolla-v2-{index:04d}", "sourceBlock": index, "sourceUrl": "https://zenodo.org/records/16908784",
            "sourceSha256": digest, "license": metadata["metadata"]["license"]["id"], "sourceText": original,
            "rawSourceTokens": tokens, "references": references, "flags": flags, "targetText": target if len(targets) == 1 else None,
            "splitGroup": "kolla-16908784-unresolved-document-groups", "split": "unassigned_review_only",
            "trainingEligible": False, "humanApproved": False})
    candidates = [row for row in rows if not row["flags"]]
    candidates.sort(key=lambda row: (-SequenceMatcher(None, row["sourceText"], row["targetText"]).ratio(), row["id"]))
    selected = candidates[:32]
    for name, values in (("reconstructed.jsonl", rows), ("pilot-candidates.jsonl", selected)):
        (FOLDER / name).write_text("".join(json.dumps(row, ensure_ascii=False) + "\n" for row in values), encoding="utf-8")
    summary = {"sourceRecord": metadata["id"], "sourceSha256": digest, "md5Verified": True, "sentenceBlocks": len(rows),
        "referencePairs": sum(len(row["references"]) for row in rows), "filterEligibleBeforeMeaningReview": len(candidates),
        "pilotCandidates": len(selected), "flags": dict(reasons), "trainingEligible": 0,
        "limitations": ["Consensus is not proof of meaning preservation", "M2 has no essay IDs; no sentence-random split", "Published learner essays differ from NPC dialogue", "GPL terms retained; game/model distribution implications not resolved", "Not uploaded to Colab or included in training"]}
    (FOLDER / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    table = "".join("<tr><td>" + html.escape(row["id"]) + "</td><td>" + html.escape(row["sourceText"]) + "</td><td>" + html.escape(row["targetText"]) + "</td></tr>" for row in selected)
    (FOLDER / "Review.html").write_text('<!doctype html><meta charset="utf-8"><title>공개 한국어 교정 후보</title><style>body{font:16px/1.7 system-ui;max-width:1200px;margin:30px auto;padding:20px}td,th{border:1px solid #ddd;padding:12px}table{border-collapse:collapse}p{background:#fff1cd;padding:15px}</style><h1>KoLLA 공개 교정 자료 · 검토 후보</h1><p>원본의 두 교정 주석이 일치한 짧은 문장 중 32개를 선별했습니다. 의미 보존과 원문 문맥 검토 전이며 학습에 넣지 않았습니다. 자료의 GPL 조건과 원문 문서 단위 분리도 확인해야 합니다.</p><table><tr><th>ID</th><th>원문</th><th>발행 자료의 교정문</th></tr>' + table + '</table>', encoding="utf-8")
    print(json.dumps(summary, ensure_ascii=False))


if __name__ == "__main__":
    main()
