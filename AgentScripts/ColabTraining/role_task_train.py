"""Task-aligned adapter experiment; approved answers are only split, never rewritten."""
import json
import train as engine

NPC_INSTRUCTION = '의미와 상태를 유지해 NPC가 듣는 사람에게 하는 대사만 쓴다. 이름 접두사·해설·JSON 없이 대사 한 줄만 출력한다. 말투를 지키고 새 사건·설정·보상을 만들지 않는다. speechMode가 garbled이면 semanticText 뜻을 유지한 알아볼 수 있는 난독 발음으로 쓴다. 160자 이내.\n'
OPTIONS_INSTRUCTION = '플레이어가 NPC에게 하는 응답만 쓴다. intents의 뜻과 순서를 지켜 실제 대사 문자열 네 개의 JSON 배열만 출력한다. 명세 객체·NPC 답변·해설은 쓰지 않는다. 질문·무관심·모욕·거절을 바꾸거나 새 사건·보상을 만들지 않는다. garbled이면 뜻을 눈치챌 수 있는 난독 발음으로 쓰며 뜻 없는 중얼거림 의도도 보존한다. 각 45자 이내.\n'


def objects(messages):
    values = []
    for line in messages[0]['content'].splitlines():
        try:
            value = json.loads(line)
            if isinstance(value, dict):
                values.append(value)
        except ValueError:
            pass
    assert len(values) == 2 and 'characters' in values[0] and 'cards' in values[1]
    return values


def derive(source_id, group, messages, answer):
    header, payload = objects(messages)
    result = []
    assert len(payload['cards']) == len(answer['cards'])
    for card, target in zip(payload['cards'], answer['cards']):
        character = next(c for c in header['characters'] if c['id'] == card['speakerId'])
        meaning = {k: v for k, v in card.items() if k not in ('index', 'type', 'speakerId', 'options', 'reference') and v is not None}
        # Explanation is authoritative; when empty, retain the actual Story reference.
        if not card.get('coreMeaning'):
            assert card.get('reference')
            meaning['storyReference'] = card['reference']
        context = {'speaker': card['speaker'], 'listener': card['listener'],
                   'speechStyle': character.get('speechStyle'), 'meaning': meaning,
                   'memory': payload.get('memory'), 'previousEvents': payload.get('previousEvents', [])}
        prefix = source_id + '/' + str(card['index'])
        for kind in ('npc', 'options'):
            if kind == 'options' and not card['options']:
                continue
            data = dict(context)
            if kind == 'options':
                data['speaker'], data['listener'] = card['listener'], card['speaker']
                data['intents'] = [{k: v for k, v in opt.items() if k != 'reference'} for opt in card['options']]
            content = (NPC_INSTRUCTION if kind == 'npc' else OPTIONS_INSTRUCTION) + json.dumps(data, ensure_ascii=False, separators=(',', ':'))
            expected = target['text'] if kind == 'npc' else json.dumps(target['options'], ensure_ascii=False, separators=(',', ':'))
            assert expected and (kind == 'npc' or len(target['options']) == 4)
            result.append({'id': prefix + '-' + kind, 'splitGroup': group,
                           'messages': [{'role': 'user', 'content': content}, {'role': 'assistant', 'content': expected}]})
    return result


game_validate = engine.validate


def validate_role_tasks(folder):
    manifest = engine.verify_bundle(folder)
    config = engine.read_json(folder / 'experiment.json')
    _, originals, _ = game_validate(folder / 'game')
    expected = []
    for row in originals:
        expected.extend(derive(row['id'], row['splitGroup'], row['messages'][:-1], json.loads(row['messages'][-1]['content'])))
    rows = [json.loads(line) for line in (folder / config['data']).read_text(encoding='utf8').splitlines() if line.strip()]
    assert rows == expected and len(rows) == 9
    assert {r['splitGroup'] for r in rows} == {'guide-family'}
    assert config['trainingApprovalScope'] == 'lossless_projection_of_existing_approved_answers'
    return config, rows, manifest


if __name__ == '__main__':
    engine.validate = validate_role_tasks
    engine.main()
