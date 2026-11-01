"""Public-corpus validation adapter for the existing isolated training engine.

Reuses engine preparation/training/checkpoint code without changing game validation
or claiming that the user individually approved each published correction.
"""
import json
from pathlib import Path
import train as engine


def validate_public(folder):
    manifest = engine.verify_bundle(folder)
    config = engine.read_json(folder / "experiment.json")
    selection = engine.read_json(folder / "PublicCorpusLock.json")
    candidates = {row["id"]: row for row in selection["records"]}
    rows = [json.loads(line) for line in (folder / config["data"]).read_text(encoding="utf-8").splitlines() if line.strip()]
    if len(rows) != selection["splitCounts"]["train"] or len({row["id"] for row in rows}) != len(rows):
        raise ValueError("Public training count or uniqueness mismatch")
    for row in rows:
        proof = candidates[row["id"]]
        if proof["split"] != "train" or proof["agentMeaningReview"] != "accepted_published_correction":
            raise ValueError("Unreviewed or held-out public record")
        if row["splitGroup"] != proof["splitGroup"] or len(proof["originalEssayMatches"]) != 1:
            raise ValueError("Original essay identity mismatch")
        expected = [{"role": "user", "content": selection["instruction"] + proof["sourceText"]}, {"role": "assistant", "content": proof["targetText"]}]
        if row["messages"] != expected:
            raise ValueError("Published correction/input changed")
    for left, right in (("train", "validation"), ("train", "test"), ("validation", "test")):
        if {r["splitGroup"] for r in candidates.values() if r["split"] == left} & {r["splitGroup"] for r in candidates.values() if r["split"] == right}:
            raise ValueError("Essay group leaked across splits")
    return config, rows, manifest


if __name__ == "__main__":
    # Process-local validator injection is limited to this public-data entry point.
    # The original game runner, frozen files and its validation gates are untouched.
    engine.validate = validate_public
    engine.main()
