"""Package a short style diagnostic for the existing Colab evaluation notebook."""
import argparse
import base64
import hashlib
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--plan", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args()
files = {name: Path(__file__).with_name(name).read_bytes() for name in ("evaluate.py", "evaluate_short_style.py")}
files["ShortStylePlan.json"] = args.plan.read_bytes()
code = 'import base64, hashlib, pathlib\nSHORT=pathlib.Path("/content/oneway_short_style")\nSHORT.mkdir(parents=True,exist_ok=True)\n'
for name, raw in files.items():
    code += f'b=base64.b64decode("{base64.b64encode(raw).decode()}")\nassert hashlib.sha256(b).hexdigest()=="{hashlib.sha256(raw).hexdigest()}"\n(SHORT/"{name}").write_bytes(b)\n'
code += 'run_command([sys.executable,str(SHORT/"evaluate_short_style.py"),"--bundle",str(SHORT),"--run",str(RUN)])\n'
compile(code, "short-style-cell", "exec")
args.output.write_text(code, encoding="utf-8")
