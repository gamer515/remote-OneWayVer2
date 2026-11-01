"""Validate approved meaning tasks, then reuse the existing training engine."""
import hashlib
import json
from pathlib import Path
import train as engine
import prepare_meaning_curriculum as preparation

game_validate = engine.validate


def assemble(folder):
    _, original, _ = game_validate(folder / 'game')
    assert len(original) == 6
    preparation.ROOT = folder / 'source'
    preparation.DATA = folder / 'game'
    rows, _, _, drafts = preparation.build()
    approval = engine.read_json(folder / 'NormalRenderingApproval.json')
    assert approval['answerApprovedByUser'] is True
    assert approval['scope'] == 'five_canonical_normal_renderings_for_auxiliary_training'
    approved = {r['id']: r for r in approval['records']}
    for draft in drafts:
        proof = approved[draft['id']]
        assert proof['messagesSha256'] == hashlib.sha256(json.dumps(draft['messages'], ensure_ascii=False, sort_keys=True).encode()).hexdigest()
        assert proof['sourceSha256'] == draft['sourceSha256']
        row = {k: draft[k] for k in ('id', 'splitGroup', 'messages')}
        rows.append(row)
        task = json.loads(draft['messages'][0]['content'].split('\n', 1)[1])
        assert task['representation'] == 'normal'
        task['representation'] = 'garbled'
        rows.append({'id': draft['id'].replace('/normal-', '/surface-garbled-'), 'splitGroup': draft['splitGroup'],
                     'messages': preparation.messages(task, draft['approvedGarbled'])})
    assert len(rows) == len({r['id'] for r in rows}) == 31
    assert {r['splitGroup'] for r in rows} == {'guide-family'}
    return rows


def validate_curriculum(folder):
    manifest = engine.verify_bundle(folder)
    config = engine.read_json(folder / 'experiment.json')
    assert config['modelId'] == 'google/gemma-3-4b-it'
    rows = [json.loads(s) for s in (folder / config['data']).read_text(encoding='utf8').splitlines() if s.strip()]
    assert rows == assemble(folder), 'Training tasks differ from approved source projections/normal renderings'
    return config, rows, manifest


if __name__ == '__main__':
    engine.validate = validate_curriculum
    engine.main()
