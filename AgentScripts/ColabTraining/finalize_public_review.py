"""Match reviewed corrections to original essays; freeze group splits without training."""
from collections import Counter
import hashlib
import html
import json
from pathlib import Path
import random
import re
import tarfile
import unicodedata
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
FOLDER = ROOT / "outputs/AiTraining/PublicDataReview/KoLLA-16908784"
CONFIG = ROOT / "Tests/LocalStoryBenchmark/Colab"


def normalize(text):
    return re.sub(r"\s+", "", unicodedata.normalize("NFC", text))


def main():
    selection = json.loads((CONFIG / "PublicCorrectionSelection.json").read_text(encoding="utf-8-sig"))
    rows = [json.loads(line) for line in (FOLDER / "reconstructed.jsonl").read_text(encoding="utf-8").splitlines()]
    chosen = [row for row in rows if row["sourceBlock"] in selection["acceptedBlockIds"]]
    if len(chosen) != len(set(selection["acceptedBlockIds"])) or any(row["flags"] for row in chosen):
        raise ValueError("Selection is not a subset of verified consensus candidates")
    docs = {}
    archive_path = FOLDER / "kolla-original.tgz"
    # Read known data in place; never extract or execute archive scripts.
    with tarfile.open(archive_path) as archive:
        for name in archive.getnames():
            if name.startswith("corpus/working/") and name.endswith(".exb.xml") and "/._" not in name:
                tree = ET.fromstring(archive.extractfile(name).read())
                tier = next(t for t in tree.iter("tier") if t.attrib.get("id") == "TIER-TOKEN")
                docs[Path(name).stem.removesuffix(".exb")] = normalize(" ".join(event.text or "" for event in tier.findall("event")))
        for name in ("corpus/LICENSE", "corpus/README", "corpus/data/README.data"):
            (FOLDER / Path(name).name).write_bytes(archive.extractfile(name).read())
    unmatched, ambiguous = [], []
    for row in chosen:
        matches = sorted(doc for doc, text in docs.items() if normalize(row["sourceText"]) in text)
        row["originalEssayMatches"] = matches
        if not matches:
            unmatched.append(row["id"])
        elif len(matches) != 1:
            ambiguous.append(row["id"])
    matched = [row for row in chosen if len(row["originalEssayMatches"]) == 1]
    groups = sorted({row["originalEssayMatches"][0] for row in matched})
    parents = {group: group for group in groups}
    def find(group):
        while parents[group] != group:
            group = parents[group]
        return group
    seen = {}
    for row in matched:
        group = row["originalEssayMatches"][0]
        for text in (row["sourceText"], row["targetText"]):
            key = normalize(text)
            if key in seen:
                parents[find(group)] = find(seen[key])
            seen[key] = group
    grouped = sorted({find(group) for group in groups})
    random.Random(12345).shuffle(grouped)
    count = len(grouped)
    split_map = {group: "train" if index < int(count * .6) else "validation" if index < int(count * .8) else "test" for index, group in enumerate(grouped)}
    for row in matched:
        row["splitGroup"] = "kolla-essay-" + find(row["originalEssayMatches"][0])
        row["split"] = split_map[find(row["originalEssayMatches"][0])]
        row["agentMeaningReview"] = "accepted_published_correction"
        row["trainingEligible"] = False
        row["humanApproved"] = False
    pair_sets = {split: {normalize(row[field]) for row in matched if row["split"] == split for field in ("sourceText", "targetText")} for split in ("train", "validation", "test")}
    assert not (pair_sets["train"] & pair_sets["validation"] or pair_sets["train"] & pair_sets["test"] or pair_sets["validation"] & pair_sets["test"])
    data = "".join(json.dumps(row, ensure_ascii=False) + "\n" for row in matched)
    (FOLDER / "reviewed-split-candidates.jsonl").write_text(data, encoding="utf-8")
    manifest = {"stage": "1/5", "status": "published_corrections_curated_and_group_splits_fixed", "reviewedCandidates": 176,
        "acceptedBeforeEssayMatch": len(chosen), "matchedCandidates": len(matched), "essayCount": len(groups), "mergedGroupCount": count,
        "splitCounts": dict(Counter(row["split"] for row in matched)), "splitSeed": 12345, "splitRatio": [0.6, 0.2, 0.2],
        "unmatched": unmatched, "ambiguous": ambiguous, "trainingEligible": 0, "newTrainingExecuted": False,
        "authorization": "User requested public corpus curation and execution; public_train validates published consensus and agent review. Per-pair user approval is not claimed. Game answer drafts keep their existing approval gate.",
        "sourceSha256": hashlib.sha256((FOLDER / "KoLLA_multi-refs.m2").read_bytes()).hexdigest(),
        "originalArchiveSha256": hashlib.sha256(archive_path.read_bytes()).hexdigest(),
        "reviewedDatasetSha256": hashlib.sha256((FOLDER / "reviewed-split-candidates.jsonl").read_bytes()).hexdigest(),
        "license": "GPL-3.0-or-later per Zenodo metadata; original archive includes GPLv3 and corpus copyright notice",
        "heldOutRule": "Keep full original essays and all references together; duplicate source/target across essays merged. Do not use final test for training or hyperparameter selection.",
        "limitation": "Exact duplicate check does not prove no paraphrases across independently authored essays; game/learner domains differ"}
    (CONFIG / "PublicCorrectionSplitManifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    body = []
    for split in ("train", "validation", "test"):
        section = [row for row in matched if row["split"] == split]
        table = "".join(f'<tr><td>{html.escape(row["id"])}</td><td>{html.escape(row["splitGroup"])}</td><td>{html.escape(row["sourceText"])}</td><td>{html.escape(row["targetText"])}</td></tr>' for row in section)
        body.append(f'<h2>{split} · {len(section)}쌍</h2><table><tr><th>ID</th><th>원본 글 묶음</th><th>원문</th><th>발행 자료의 교정문</th></tr>{table}</table>')
    (FOLDER / "Stage1Review.html").write_text('<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>전체 1/5 · 공개 한국어 자료 확정 검토</title><style>body{font:16px/1.7 system-ui;max-width:1400px;margin:30px auto;padding:20px;background:#f5f6f8}table{border-collapse:collapse;background:white;width:100%}td,th{border:1px solid #ddd;padding:10px}h1,h2{line-height:1.4}.note{padding:18px;background:#fff1cd}pre{white-space:pre-wrap}</style><h1>전체 1/5 · 공개 한국어 자료 확정 검토</h1><p class="note">176개 후보를 검토하고 원본 글과 대조했습니다. 교정문을 새로 생성하지 않고 발행 자료의 두 주석이 일치하는 쌍만 사용했습니다. 공개 자료 선별·실행 요청에 따라 검토한 자료입니다. 실제 학습 상태는 전체 진행 화면을 참조하세요.</p><pre>' + html.escape(json.dumps(manifest, ensure_ascii=False, indent=2)) + '</pre>' + "".join(body) + '</html>', encoding="utf-8")
    print(json.dumps(manifest, ensure_ascii=False))


if __name__ == "__main__":
    main()
