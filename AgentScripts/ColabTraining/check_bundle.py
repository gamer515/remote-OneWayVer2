"""Offline checks for approved-data integrity and notebook portability; no GPU imports."""
import argparse
import base64
import copy
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import zipfile


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", type=Path, required=True)
    args = parser.parse_args()
    project = args.project.resolve()
    notebook = json.loads((project / "Tests/LocalStoryBenchmark/Colab/OneWay_Colab_Training.ipynb").read_text(encoding="utf-8"))
    cells = notebook["cells"]
    if any(cell.get("outputs") for cell in cells if cell["cell_type"] == "code"):
        raise AssertionError("Notebook must not ship execution output or secrets")
    for index, cell in enumerate(cells):
        if cell["cell_type"] == "code":
            compile("".join(cell["source"]), f"cell-{index}", "exec")
    spec = importlib.util.spec_from_file_location("oneway_training", Path(__file__).with_name("train.py"))
    runner = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(runner)
    with tempfile.TemporaryDirectory(dir=project / "tmp", prefix="colab-check-") as temp:
        folder = Path(temp)
        with zipfile.ZipFile(project / "outputs/AiTraining/OneWay_Colab_Bundle.zip") as archive:
            for name in archive.namelist():
                (folder / name).write_bytes(archive.read(name))
        original = {p.name: p.read_bytes() for p in folder.iterdir()}
        namespace = {"__name__": "__notebook_check__"}
        # Extract the embedded zip without executing notebook code or any imports beyond stdlib.
        import ast
        tree = ast.parse("".join(cells[1]["source"]))
        encoded = next(node.value.args[0].value for node in tree.body if isinstance(node, ast.Assign)
                       and any(isinstance(t, ast.Name) and t.id == "payload" for t in node.targets))
        embedded = base64.b64decode(encoded)
        assert embedded == (project / "outputs/AiTraining/OneWay_Colab_Bundle.zip").read_bytes()
        config, records, _ = runner.validate(folder)
        assert len(records) == 6 and {r["splitGroup"] for r in records} == {"guide-family"}
        for path in folder.glob("*.py"):
            compile(path.read_text(), path.name, "exec")

        def restore():
            for name, raw in original.items():
                (folder / name).write_bytes(raw)

        def update_manifest():
            manifest = runner.read_json(folder / "bundle-manifest.json")
            for name in manifest["files"]:
                manifest["files"][name] = runner.sha(folder / name)
            runner.write_json(folder / "bundle-manifest.json", manifest)

        def must_reject(name, change, repair_hashes=True):
            restore()
            change()
            if repair_hashes:
                update_manifest()
            try:
                runner.validate(folder)
            except (ValueError, KeyError):
                return
            raise AssertionError(f"Unsafe data accepted: {name}")

        def set_candidate(key, value):
            data = runner.read_json(folder / "CurrentInputCandidates.json")
            data["entries"][0][key] = value
            runner.write_json(folder / "CurrentInputCandidates.json", data)

        must_reject("unapproved", lambda: set_candidate("answerApprovedByUser", False))
        must_reject("failed validation", lambda: set_candidate("validation", {"tryBuild": False, "sameBindings": True, "situation": True, "lengths": True}))
        def leak():
            rows = copy.deepcopy(records)
            rows[0]["splitGroup"] = "duelist-family"
            set_candidate("splitGroup", "duelist-family")
            (folder / config["data"]).write_text("\n".join(json.dumps(row, ensure_ascii=False) for row in rows), encoding="utf-8")
        must_reject("final evaluation scene in training", leak)
        def bad_answer():
            rows = copy.deepcopy(records)
            rows[0]["messages"][-1]["content"] = '{"cards":[]}'
            (folder / config["data"]).write_text("\n".join(json.dumps(row, ensure_ascii=False) for row in rows), encoding="utf-8")
        must_reject("changed answer", bad_answer)
        must_reject("tampered bundle", lambda: (folder / config["data"]).write_text("changed"), repair_hashes=False)
        restore()
        runner.validate(folder)
    print(json.dumps({"passed": True, "trainingPairs": 6, "negativeChecks": 5,
                      "notebookCodeCellsCompiled": sum(c["cell_type"] == "code" for c in cells),
                      "embeddedBundleMatches": True, "gpuTrainingExecuted": False}))


if __name__ == "__main__":
    main()
