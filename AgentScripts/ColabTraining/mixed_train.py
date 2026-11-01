"""Combine separately validated public corrections and approved game pairs."""
import json
import train as engine
import public_train

game_validate = engine.validate

def validate_mixed(folder):
    manifest=engine.verify_bundle(folder)
    config=engine.read_json(folder/'experiment.json')
    _, public_rows, _=public_train.validate_public(folder/'public')
    _, game_rows, _=game_validate(folder/'game')
    assert len(public_rows)==66 and len(game_rows)==6
    assert {row['splitGroup'] for row in public_rows}.isdisjoint({row['splitGroup'] for row in game_rows})
    rows=[json.loads(line) for line in (folder/config['data']).read_text(encoding='utf-8-sig').splitlines() if line.strip()]
    if rows!=public_rows+game_rows:raise ValueError('Combined data differ from validated sources')
    assert len({row['id'] for row in rows})==72
    return config,rows,manifest

if __name__=='__main__':
    engine.validate=validate_mixed
    engine.main()
