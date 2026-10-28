"""Create an additive Colab cell for the approved evaluation workflow."""
import base64
import hashlib
import io
import json
from pathlib import Path
import zipfile

root = Path(__file__).resolve().parents[2]
sources = {"InputSeparationInputs.json": root / "Tests/LocalStoryBenchmark/Colab/InputSeparationInputs.json"}
sources.update({name: Path(__file__).with_name(name) for name in ("evaluate.py", "evaluate_input_ab.py")})
payload = {name: path.read_bytes() for name, path in sources.items()}
payload["input-ab-manifest.json"] = json.dumps({"files": {name: hashlib.sha256(raw).hexdigest() for name, raw in payload.items()}}, indent=2).encode()
stream = io.BytesIO()
with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_DEFLATED) as archive:
    for name, raw in payload.items():
        archive.writestr(name, raw)
data = stream.getvalue()
code = f'''# Input separation AB: validation only, no training or game changes
import base64, hashlib, io, pathlib, zipfile
INPUT_AB = pathlib.Path("/content/oneway_input_ab")
input_ab_payload = base64.b64decode("{base64.b64encode(data).decode()}")
assert hashlib.sha256(input_ab_payload).hexdigest() == "{hashlib.sha256(data).hexdigest()}"
INPUT_AB.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(io.BytesIO(input_ab_payload)) as archive:
    for item in archive.infolist():
        target = (INPUT_AB / item.filename).resolve()
        if not target.is_relative_to(INPUT_AB.resolve()):
            raise ValueError("Invalid bundle path")
        target.write_bytes(archive.read(item))
run_command([sys.executable, str(INPUT_AB / "evaluate_input_ab.py"), "--bundle", str(INPUT_AB), "--run", str(RUN)])
'''
compile(code, "input-ab-cell", "exec")
for name in ("evaluate.py", "evaluate_input_ab.py"):
    compile(payload[name].decode(), name, "exec")
output = root / "outputs/AiTraining"
output.mkdir(parents=True, exist_ok=True)
(output / "input-ab-cell.py").write_text(code, encoding="utf-8")
(output / "OneWay_Input_AB_Bundle.zip").write_bytes(data)
print(json.dumps({"bytes": len(data), "cellFile": str(output / "input-ab-cell.py")}))
