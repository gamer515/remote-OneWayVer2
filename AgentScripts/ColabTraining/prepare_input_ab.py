"""Prepare a reversible input-only experiment from existing module exports. No training."""
import copy
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DATA = ROOT / "Tests/LocalStoryBenchmark"


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def unpack(content):
    # Existing module exports end in two JSON documents on separate lines.
    instructions, characters, task = content.rsplit("\n", 2)
    return instructions, json.loads(characters), json.loads(task)


def rename(task, reverse=False):
    result = copy.deepcopy(task)
    for card in result["cards"]:
        mapping = {"reference": "npc_line_reference", "options": "player_response_specs"}
        if reverse:
            mapping = {value: key for key, value in mapping.items()}
        for old, new in mapping.items():
            if old in card:
                if new in card:
                    raise ValueError("Ambiguous card field")
                card[new] = card.pop(old)
        key = "options" if reverse else "player_response_specs"
        for option in card[key]:
            old, new = ("player_response_reference", "reference") if reverse else ("reference", "player_response_reference")
            if old in option:
                if new in option:
                    raise ValueError("Ambiguous response field")
                option[new] = option.pop(old)
    return result


def transform(messages):
    result = copy.deepcopy(messages)
    for message in result:
        if message["role"] != "user":
            continue
        instructions, characters, task = unpack(message["content"])
        changed = rename(task)
        if rename(changed, reverse=True) != task:
            raise ValueError("Input transformation changed source meaning fields")
        clarification = (
            "\n입력 필드 구분: npc_line_reference는 NPC 원문이다. "
            "player_response_specs는 플레이어 응답의 명세이며, 각 intent와 player_response_reference의 의미를 유지한다. "
            "이 입력 객체를 출력에 복사하지 않는다. 출력 text는 NPC 발화, options는 플레이어가 말하거나 행동할 문장들의 문자열 배열이다. "
            "입력의 player_response_specs 순서와 개수를 출력 options에 유지한다. "
            "출력 형식은 기존 cards/index/text/options 그대로다. speechMode가 난독을 요구하면 semanticText의 의미와 난독 상태를 함께 유지한다.")
        message["content"] = instructions + clarification + "\n" + json.dumps(characters, ensure_ascii=False, separators=(",", ":")) + "\n" + json.dumps(changed, ensure_ascii=False, separators=(",", ":"))
    return result


def main():
    baseline = read(DATA / "TrainingBaseline.json")
    frozen = next(value for value in baseline.values() if isinstance(value, list) and value and isinstance(value[0], dict) and "sha256" in value[0] and "path" in value[0])
    for item in frozen:
        if hashlib.sha256((ROOT / item["path"]).read_bytes()).hexdigest() != item["sha256"]:
            raise ValueError("Frozen source changed: " + item["path"])
    exported = read(DATA / "Colab/EvaluationInputs.json")
    cases = []
    for case in exported["cases"]:
        if case["split"] != "validation":
            continue
        row = copy.deepcopy(case)
        row["messagesByArm"] = {"current": row.pop("messages")}
        row["messagesByArm"]["separated"] = transform(row["messagesByArm"]["current"])
        cases.append(row)
    plan = copy.deepcopy(exported["plan"])
    plan.update(evaluationId="input-separation-validation-v1", comparison="Same base model without LoRA; current versus separated input fields", cases=[x for x in plan["cases"] if x["splitGroup"] == "gambler-family"])
    result = {"plan": plan, "baselineId": exported["baselineId"], "cases": cases,
        "trainingExecuted": False, "gameApplied": False, "remoteExecuted": False,
        "limitations": ["Exploratory validation only; incomplete Explanation stays incomplete", "Raw HF generation without game grammar", "Local full-scene 180-second target not measured", "Duelist excluded; select a fresh final holdout before further model selection"]}
    target = DATA / "Colab/InputSeparationInputs.json"
    target.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    # Existing approved answers are unchanged; transformed inputs are isolated candidates.
    pairs = [json.loads(line) for line in (DATA / "TrainingPairs.jsonl").read_text(encoding="utf-8-sig").splitlines() if line.strip()]
    candidates = [{**pair, "messages": transform(pair["messages"]), "trainingEligible": False,
        "status": "input_ab_pending", "answerChanged": False} for pair in pairs]
    (DATA / "Colab/InputSeparationTrainingCandidates.json").write_text(json.dumps(candidates, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"frozenFilesVerified": len(frozen), "validationSections": len(cases), "plannedGenerations": len(cases) * len(plan["seeds"]) * 2, "isolatedCandidates": len(candidates), "answersChanged": 0, "trainingExecuted": False}))


if __name__ == "__main__":
    main()
