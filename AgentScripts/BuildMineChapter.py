"""Import the approved Korean source without rewriting dialogue. Line ranges are source provenance.
Run again only when the source changes; review the generated graph diff before applying in Unity.
"""
import json, re, hashlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = Path('C:/Users/유영선/Desktop/이야기.txt')
LINES = SOURCE.read_text(encoding='utf-8-sig').splitlines()
BASE = ROOT / 'Assets/Decision_YYS/Resources/Chapters'
PREFIX = 'Chapter_1/Chapter_1_01/Encounters/'
NAMES = {'브룩':'Brook','도르':'Dor','모아':'Moa','피코':'Pico','틸':'Til','벨라':'Bella','네아':'Nea','시아':'Sia'}
nodes, provenance = {}, {}

def speech(a,b):
    result=[]
    for line in LINES[a-1:b]:
        retry=re.match(r'\*\*도르\.\*\*\s*"([^"]+)"',line)
        if retry:
            result.append({'speakerId':'Dor','text':retry[1]})
            continue
        m=re.match(r'\*\*([^:*]+):\*\*\s*["“](.*)["”]\s*$',line)
        if not m: m=re.match(r'(네아|시아):\s*["“](.*)["”]\s*$',line)
        if m:
            result.append({'speakerId':NAMES.get(m[1],m[1]),'text':m[2]})
    return result

def options(a,b):
    return [m[1] for line in LINES[a-1:b] if (m:=re.match(r'\d+\.\s*["“](.*)["”]',line))]

def node(key,a,b,menu=None,targets=None,next=None,effect=None,fixed=False):
    cards=speech(a,b)
    if not cards: raise ValueError((key,a,b))
    for i,c in enumerate(cards): c.update(type='Next',eventIdStable=f'mine_{key}_{i}')
    if menu:
        opts=options(*menu) if isinstance(menu,tuple) else menu
        assert len(opts)==len(targets),(key,opts,targets)
        cards[-1].update(type='Choice',choiceKind='Mechanic',options=opts,choiceActions=[
            dict(choiceId=f'{key}_{i}',action='story',storyPath=PREFIX+t) for i,t in enumerate(targets)])
    if next: cards[-1]['nextStoryPath']=PREFIX+next
    if effect: cards[-1]['chapterAction']=effect
    nodes[key]={'MainStory':cards,'rewriteAllowed':not fixed}
    provenance[key]={'file':str(SOURCE),'lines':[a,b]}
    return cards[-1]

def allow(): return {'kind':'access','granted':True}
def deny(): return {'kind':'access','granted':False}
def labor(stat,delta,win,lose=None,fail=None):
    return dict(kind='labor',ability=stat,successDelta=delta,failureDelta=fail or [0]*4,
                winStoryPath=PREFIX+win,loseStoryPath=PREFIX+(lose or win),guaranteed=lose is None)
def game(kind,win,lose): return dict(kind=kind,winStoryPath=PREFIX+win,loseStoryPath=PREFIX+lose)
def pay(key,a,b,amount,targets,recipient):
    c=node(key,a,b,menu=['힘 코인으로 내겠습니다.','속도 코인으로 내겠습니다.','지능 코인으로 내겠습니다.','매력 코인으로 내겠습니다.'],targets=targets)
    for i,cost in enumerate(c['choiceActions']): cost.update(abilityCost=[amount if j==i else 0 for j in range(4)],receiverId=recipient)
def result(key,a,b,granted=True,next=None):
    node(key,a,b,next=next,effect=allow() if granted else {'kind':'death'})

node('brook',172,188,(190,194),['brook_pay','brook_why','brook_free','brook_refuse'])
pay('brook_pay',197,203,10,['brook_strength','brook_speed','brook_knowledge','brook_charm'],'Brook')
nodes['brook_pay']['MainStory'][-1]['choiceActions'][2]['receiverId']='Dor'
for choice in nodes['brook_pay']['MainStory'][-1]['choiceActions']:
    choice['insufficientStoryPath']=PREFIX+'brook_insufficient'
nodes['brook_insufficient']={'MainStory':[
    dict(speakerId='Brook',text='뭐야 코인이 없잖아! 이게 누구 놀리는 거야?',type='Next',eventIdStable='brook_insufficient_0'),
    dict(speakerId='Dor',text='브룩, 본 때를 보여주라고! 저 자식 우리를 가지고 논거야!',type='Next',eventIdStable='brook_insufficient_1'),
    dict(speakerId='Brook',text='덤벼 이 자식아!.',type='Next',eventIdStable='brook_insufficient_2',chapterAction=game('duel','brook_duel_win','brook_duel_lose'))
], 'rewriteAllowed':False}
provenance['brook_insufficient']={'source':'user_message_2026-11-02','change':'잔액 부족 시 대사와 결투'}
result('brook_strength',214,224);result('brook_speed',232,244)
node('brook_knowledge',252,266,effect=game('duel','brook_duel_win','brook_duel_lose'))
result('brook_duel_win',273,283);result('brook_duel_lose',288,298,False)
node('brook_charm',305,317,next='brook_charm_after')
result('brook_charm_after',324,332)
node('brook_why',337,343,(345,347),['brook_pay','brook_refuse'])
node('brook_free',356,362,(364,366),['brook_pay','brook_refuse'])
node('brook_refuse',375,380,effect=deny())

node('dor',388,396,(398,402),['dor_pay','dor_why','dor_labor_offer','dor_refuse'])
pay('dor_pay',409,413,10,['dor_strength','dor_speed','dor_knowledge','dor_charm'],'Dor')
result('dor_strength',424,432);result('dor_speed',439,447);result('dor_knowledge',454,462)
result('dor_charm',469,485)
node('dor_revisit',488,493,effect=allow())
node('dor_why',498,506,(508,510),['dor_pay','dor_refuse'])
node('dor_labor_offer',520,526,(528,530),['dor_labor','dor_pay'])
node('dor_labor',533,539,effect=labor(0,[2,-3,1,-4],'dor_labor_done'))
# No invented completion dialogue: the last authored instruction is retained.
node('dor_labor_done',535,536,effect=allow())
node('dor_refuse',544,550,effect=deny())
node('dor_refuse_retry',553,558,next='dor_pay')

node('moa',566,576,(578,581),['moa_pay','moa_why','moa_labor','moa_refuse'])
pay('moa_pay',584,590,8,['moa_strength','moa_speed','moa_knowledge','moa_charm'],'Moa')
result('moa_strength',598,604);result('moa_speed',608,614);result('moa_knowledge',618,624);result('moa_charm',628,638)
node('moa_why',642,650,menu=['알겠습니다. 코인으로 내겠습니다.','그럼 들어가지 않겠습니다.'],targets=['moa_pay','moa_refuse'])
node('moa_labor',654,660,effect=labor(0,[-3,-4,2,1],'moa_labor_done','moa_labor_retry',[-5,-2,1,-1]))
node('moa_labor_done',658,660,effect=allow())
node('moa_labor_retry',654,660,menu=['다시 해 보겠습니다.','코인으로 내겠습니다.'],targets=['moa_labor','moa_pay'])
node('moa_refuse',671,675,effect=deny())

node('pico',685,695,(697,700),['pico_pay','pico_why','pico_gamble','pico_refuse'])
pay('pico_pay',703,707,4,['pico_strength','pico_speed','pico_knowledge','pico_charm'],'Pico')
result('pico_strength',715,719);result('pico_speed',723,727);result('pico_knowledge',731,735);result('pico_charm',739,745)
node('pico_why',749,755,menu=['코인으로 줄게.','그럼 들어가지 않을게.'],targets=['pico_pay','pico_refuse'])
node('pico_gamble',761,769,(771,773),['pico_heads','pico_tails','pico_pay'])
for side in ['heads','tails']:
    node('pico_'+side,762,768,effect=game('coin_'+side,'pico_win','pico_lose'))
result('pico_win',781,789)
node('pico_lose',794,800,(802,804),['pico_pay6','pico_rematch'])
pay('pico_pay6',853,857,6,['pico_strength','pico_speed','pico_knowledge','pico_charm'],'Pico')
node('pico_rematch',812,818,(820,822),['pico_fee','pico_condition','pico_pay6'])
pay('pico_fee',825,829,1,['pico_gamble']*4,'Pico')
node('pico_condition',836,842,(844,846),['pico_gamble7','pico_pay6_retry'])
node('pico_gamble7',825,829,menu=['좋아. 앞면에 걸게.','좋아. 뒷면에 걸게.'],targets=['pico_heads7','pico_tails7'])
for side in ['heads','tails']:
    node('pico_'+side+'7',825,829,effect=game('coin_'+side,'pico_win','pico_lose7'))
node('pico_lose7',794,800,menu=['약속대로 코인 7개를 줄게.','한 번만 더 승부하자.'],targets=['pico_pay7','pico_rematch'])
pay('pico_pay7',853,857,7,['pico_strength','pico_speed','pico_knowledge','pico_charm'],'Pico')
pay('pico_pay6_retry',825,829,6,['pico_gamble']*4,'Pico')
node('pico_refuse',863,869,effect=deny())

node('til',881,889,(891,895),['til_pay','til_why','til_race_offer','til_refuse'])
for amount in [5,6,10,12,15,20]:
    pay('til_pay'+('' if amount==10 else str(amount)),901,905,amount,
        ['til_race_retry']*4 if amount==5 else ['til_strength','til_speed','til_knowledge','til_charm'],'Til')
result('til_strength',915,921);result('til_speed',928,936);result('til_knowledge',942,950);result('til_charm',955,967)
node('til_revisit',972,977,effect=allow())
node('til_why',981,987,(989,991),['til_pay6','til_refuse'])
node('til_race_offer',1000,1008,(1010,1013),['til_race','til_negotiate','til_cancel'])
node('til_race',1016,1022,effect=game('race','til_win','til_lose'))
node('til_negotiate',1070,1076,effect=game('race','til_win','til_lose10'))
node('til_cancel',1082,1086,next='til_pay12')
result('til_win',1026,1034)
node('til_lose',1040,1044,(1046,1049),['til_pay20','til_rematch','til_refuse'])
node('til_lose10',1040,1044,menu=['코인 10개를 내겠습니다.','한 번만 더 경주하죠.','지금은 코인이 부족합니다.'],targets=['til_pay','til_rematch','til_refuse'])
node('til_rematch',1052,1058,(1060,1062),['til_pay5','til_refuse'])
node('til_race_retry',1016,1022,effect=game('race','til_win','til_lose15'))
node('til_lose15',1040,1044,menu=['코인 15개를 내겠습니다.','한 번만 더 경주하죠.','여기서 그만하죠.'],targets=['til_pay15','til_rematch','til_refuse'])
node('til_refuse',1091,1095,effect=game('duel','til_duel_win','til_duel_lose'))
result('til_duel_win',1097,1101);result('til_duel_lose',1103,1108,False)

node('bella',1110,1123,(1125,1130),['bella_pay','bella_why','bella_labor_offer','bella_refuse'])
pay('bella_pay',1134,1138,10,['bella_strength','bella_speed','bella_knowledge','bella_charm'],'Bella')
result('bella_strength',1150,1158);result('bella_speed',1167,1175);result('bella_knowledge',1182,1192);result('bella_charm',1199,1211)
node('bella_revisit',1216,1222,effect=allow())
node('bella_why',1226,1234,(1236,1238),['bella_pay_ack','bella_refuse'])
node('bella_pay_ack',1241,1245,next='bella_pay')
node('bella_refuse',1250,1254,effect=deny())
node('bella_labor_offer',1261,1267,(1269,1271),['bella_labor','bella_pay'])
node('bella_labor',1262,1266,effect=labor(2,[-3,-2,3,2],'bella_labor_win','bella_labor_lose',[-3,-2,1,-4]))
result('bella_labor_win',1276,1282)
node('bella_labor_lose',1289,1291,(1293,1295),['bella_labor','bella_pay'])

node('rumor_1',61,75,fixed=True)
nodes['rumor_1']['MainStory'][1]['text']='켄타우로스라고? 진짜 다리가 네 개라고?'
provenance['rumor_1']['override']={'source':'user_message_2026-11-02','speaker':'Dor','text':'켄타우로스라고? 진짜 다리가 네 개라고?'}
# Fourth place has only the authored first meeting; no personal story is invented.
node('nea_sia',82,103,fixed=True)

def write(path,data):
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

catalog={'chapters':[{'id':'Chapter_1','folder':'Chapter_01','episodes':['Chapter_1_01','Chapter_1_BossPreview']}],'stories':[]}
storypack,explanations,actions={},{},{}
for num,(key,content) in enumerate(nodes.items()):
    cards=content['MainStory']; meanings={}; runtime={}
    authored=[]
    for i,card in enumerate(cards):
        cid=120000+num*100+i
        entry={'ID':cid,'type':card['type'],'speakID':card['speakerId'],'eventIDstable':card['eventIdStable'],'Text':card['text']}
        for field in ['options','choiceKind','choiceActions']:
            if field in card:
                entry[field]=card[field]
        if 'choiceActions' in entry:
            entry['choiceActions']=[dict(choiceID=c['choiceId'],action=c['action'],storyPath=c['storyPath']) for c in card['choiceActions']]
            extras={c['choiceId']:{k:v for k,v in c.items() if k not in ['choiceId','action','storyPath']} for c in card['choiceActions']}
            if any(extras.values()): runtime[str(cid)]={'choiceRules':extras}
        for field in ['chapterAction','nextStoryPath']:
            if field in card: runtime.setdefault(str(cid),{})[field]=card[field]
        meanings[str(cid)]={'coreMeaning':card['text']}
        authored.append(entry)
    storypack[key]={'MainStory':authored}
    meanings['scene']={'style':'지하 갱도에서의 첫 만남. 기분은 말투에만 반영하고 기존 흐름을 유지한다.'}
    explanations[key]={'storyFlame':{'purpose':'청크 1의 첫 만남. 원문의 사건과 결과를 유지한다.'},'coreMeaning':meanings,
        'paragraphs':[dict(id=key+'_paragraph_'+str(i//4),eventIds=[c['eventIdStable'] for c in cards[i:i+4]],purpose='원문의 발화 의미와 순서를 유지한다.',currentState=['새로운 사건이나 대가를 창작하지 않는다.']) for i in range(0,len(cards),4)],
        'promptRules':['사건, 분기, 비용, 결과, 발화자는 변경하지 않는다. 기분에 따른 말투만 바꿀 수 있다.']}
    catalog['stories'].append(dict(path=PREFIX+key,chapterId='Chapter_1',chapterFolder='Chapter_01',episodeId='Chapter_1_01',
        characterId=cards[0]['speakerId'],placeId={'dor':'brook','pico':'moa','bella':'til'}.get(key,key),chunkIndex=0,key=key,
        storyFile='Chapter_01/Chapter_01_01/FirstMeetings/Story.json',explanationFile='Chapter_01/Chapter_01_01/FirstMeetings/Explanation.json',
        rewriteAllowed=content['rewriteAllowed']))
    actions[PREFIX+key]=runtime
write(BASE/'Catalog.json',catalog)
write(BASE/'Chapter_01/Chapter_01_01/FirstMeetings/Story.json',storypack)
write(BASE/'Chapter_01/Chapter_01_01/FirstMeetings/Explanation.json',explanations)
terrain=json.loads((BASE/'Chapter_01/Terrain.json').read_text(encoding='utf-8-sig'))
terrain['actions']=actions
terrain['episodes']['Chapter_1_01']['TerrainInfo']['places']=[dict(placeId=p,chunkIndex=0,prefabId='mine_anchor',characterId=n,connectStoryCards=PREFIX+p) for p,n in [('brook','Brook'),('moa','Moa'),('til','Til'),('nea_sia','Nea')]]
terrain['episodes']['Chapter_1_BossPreview']={'TerrainInfo':dict(terrainName='BossRevealPreparedChunk',groundId='boss_preview',groundIds=['boss_preview'],chunkCount=1,chunkLength=50,places=[])}
write(BASE/'Chapter_01/Terrain.json',terrain)
transform_file=BASE/'Chapter_01/Transform.json'
previous=json.loads(transform_file.read_text(encoding='utf-8-sig'))
previous_places={p['placeId']:p for p in previous['episodes']['Chapter_1_01']['TerrainTransforms']['places']}
write(transform_file,{'episodes':{'Chapter_1_BossPreview':{'TerrainTransforms':{'places':[]}},'Chapter_1_01':{'TerrainTransforms':{'places':[
    dict(previous_places.get(p,{}),placeId=p,useExactPosition=True,position=dict(x=-1.2,y=0,z=z),rotation=dict(x=0,y=180,z=0)) for p,z in [('brook',11),('moa',38),('til',65),('nea_sia',92)]]}}}})
write(BASE/'Chapter_01/Characters.json',{'characters':[dict(characterId=i,name=n,role='갱도 주민',personality='',repeatWithinTerrain=False,publicFacts=[],hidden={}) for n,i in NAMES.items()]})
omnibus=json.loads((BASE/'Omnibus.json').read_text(encoding='utf-8-sig'))
omnibus['chapters'][0]['episodeIds']=['Chapter_1_01','Chapter_1_BossPreview']
write(BASE/'Omnibus.json',omnibus)
write(ROOT/'Documentation/MineChapter/source-map.json',dict(sourceSha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest(),nodes=provenance))
print(f'Imported {len(nodes)} nodes / {sum(len(n["MainStory"]) for n in nodes.values())} speeches, with approved dialogue overrides.')

