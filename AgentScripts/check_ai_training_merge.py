"""Read-only merge rehearsal: alternate index and unreferenced snapshot, no checkout or ref updates."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'output/MineChapterReview'
OUT.mkdir(parents=True, exist_ok=True)
command = ['git', '-c', f'safe.directory={ROOT.as_posix()}']
def git(*args, env=None, check=True, data=None):
    r = subprocess.run(command + list(args), cwd=ROOT, env=env, input=data, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if check and r.returncode:
        raise RuntimeError(r.stderr.decode('utf-8', 'replace')[-3000:])
    return r
def value(*args, env=None): return git(*args, env=env).stdout.decode('utf-8').strip()
index = Path(value('rev-parse', '--git-path', 'index'))
if not index.is_absolute(): index = ROOT / index
def index_hash(): return hashlib.sha256(index.read_bytes()).hexdigest()
before_index = index_hash()
head = value('rev-parse', 'HEAD')
remote = value('rev-parse', 'origin/feature/ai-training')
base = value('merge-base', head, remote)
with tempfile.TemporaryDirectory(prefix='merge-check-', dir=OUT) as temporary:
    env = os.environ.copy()
    env['GIT_INDEX_FILE'] = str(Path(temporary) / 'index')
    git('read-tree', head, env=env)
    git('add', '--all', '--', '.', env=env)
    tree = value('write-tree', env=env)
    rehearsal = git('merge-tree', '--write-tree', '--name-only', f'--merge-base={base}', tree, remote, env=env, check=False)
    snapshot = None
    if rehearsal.returncode not in (0, 1):
        env.update(GIT_AUTHOR_NAME='Merge verification', GIT_AUTHOR_EMAIL='merge-verification@localhost',
                   GIT_COMMITTER_NAME='Merge verification', GIT_COMMITTER_EMAIL='merge-verification@localhost')
        snapshot = git('commit-tree', tree, '-p', head, env=env, data=b'Unreferenced working-tree snapshot for conflict verification only\n').stdout.decode().strip()
        rehearsal = git('merge-tree', '--write-tree', '--name-only', snapshot, remote, env=env, check=False)
    if rehearsal.returncode not in (0, 1):
        raise RuntimeError(rehearsal.stderr.decode('utf-8', 'replace'))
    output = rehearsal.stdout.decode('utf-8', 'replace')
    sections = output.split('\n\n')
    conflicts = [line for line in sections[0].splitlines()[1:] if line]
    ours = set(value('diff', '--name-only', base, tree).splitlines())
    theirs = set(value('diff', '--name-only', base, remote).splitlines())
    shared = sorted(ours & theirs)
    report = dict(remoteBranch='origin/feature/ai-training', remoteCommit=remote, head=head, mergeBase=base,
                  workingTreeIncluded=True, trackedAndUntrackedIncluded=True, conflicts=conflicts,
                  clean=rehearsal.returncode == 0, jointlyChangedPaths=shared,
                  diagnostics=output, originalIndexUnchanged=index_hash() == before_index,
                  headUnchanged=value('rev-parse', 'HEAD') == head, mergePerformed=False,
                  snapshotCommit=snapshot, snapshotReferencedByBranch=False)
    (OUT / 'ai-training-merge-check.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k not in ('diagnostics', 'snapshotCommit')}, ensure_ascii=False, indent=2))
