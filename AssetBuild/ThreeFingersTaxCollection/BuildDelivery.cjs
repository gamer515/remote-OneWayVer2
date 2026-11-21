const fs = require('node:fs');
const path = require('node:path');
const output = path.resolve(__dirname, '../../output/ThreeFingersTaxCollection');
const mainAssets = path.resolve(__dirname, '../../Assets/ThreeFingersTaxCollection');
const assets = fs.existsSync(mainAssets) ? mainAssets : path.join(__dirname,'ValidationProject/Assets/ThreeFingersTaxCollection');
const crcTable = Array.from({length:256},(_,v)=>{let c=v;for(let j=0;j<8;j++)c=c&1?0xedb88320^(c>>>1):c>>>1;return c>>>0;});
function chunk(type,payload){const b=Buffer.concat([Buffer.from(type),payload]);let c=0xffffffff;for(const v of b)c=crcTable[(c^v)&255]^(c>>>8);const n=Buffer.alloc(4),crc=Buffer.alloc(4);n.writeUInt32BE(payload.length);crc.writeUInt32BE((c^0xffffffff)>>>0);return Buffer.concat([n,b,crc]);}
function png(file){const b=fs.readFileSync(file),a=[];for(let p=8;p<b.length;){const n=b.readUInt32BE(p);a.push({type:b.toString('ascii',p+4,p+8),data:b.subarray(p+8,p+8+n)});p+=n+12;}return a;}
for(const [mode,end] of [['scale',180],['direct',72]])for(const view of ['front','side']){
  const frames=Array.from({length:end/3+1},(_,i)=>png(path.join(output,mode,`${view}_${String(i*3).padStart(3,'0')}.png`)));
  const header=frames[0].find(x=>x.type==='IHDR').data,actl=Buffer.alloc(8);actl.writeUInt32BE(frames.length);actl.writeUInt32BE(0,4);
  const result=[Buffer.from([137,80,78,71,13,10,26,10]),chunk('IHDR',header),chunk('acTL',actl)];let seq=0;
  frames.forEach((parts,i)=>{const ctl=Buffer.alloc(26);ctl.writeUInt32BE(seq++);ctl.writeUInt32BE(720,4);ctl.writeUInt32BE(600,8);ctl.writeUInt16BE(i===frames.length-1?3:1,20);ctl.writeUInt16BE(i===frames.length-1?5:20,22);result.push(chunk('fcTL',ctl));for(const p of parts.filter(x=>x.type==='IDAT')){if(!i)result.push(chunk('IDAT',p.data));else{const s=Buffer.alloc(4);s.writeUInt32BE(seq++);result.push(chunk('fdAT',Buffer.concat([s,p.data])));}}});
  result.push(chunk('IEND',Buffer.alloc(0)));fs.writeFileSync(path.join(output,`${mode}-${view}.png`),Buffer.concat(result));
}
const validation=JSON.parse(fs.readFileSync(path.join(output,'validation.json')));
if (!validation.passed || validation.results.some(r=>r.samples.some(s=>s.wallInteriorVertices>0))) throw new Error('Playback or bowl contact validation failed');
const stats=validation.results.map(r=>({clip:r.clip,samples:r.samples.length,autoplay:r.autoplayVerified,coinError:Math.max(...r.samples.map(s=>s.coinContactError)),bowlError:Math.max(...r.samples.map(s=>s.bowlContactError)),wallVertices:Math.max(...r.samples.map(s=>s.wallInteriorVertices)),jump:Math.max(...r.samples.map(s=>s.switchJump)),bowlJump:Math.max(...r.samples.map(s=>s.bowlSwitchJump)),tilt:Math.max(...r.samples.map(s=>s.bowlTilt))}));
fs.writeFileSync(path.join(output,'verification-summary.json'),JSON.stringify({passed:validation.passed,stats},null,2));
const readme=`# ThreeFingers 세금 회수 클립

기존 ThreeFingers 리그와 원본 나무 통·저울·코인으로 제작. 원본 프리팹/애니메이션 및 게임 씬은 수정하지 않았습니다. 패키지는 현재 게임과 같은 URP 프로젝트용이며 UPM 패키지 소스는 포함하지 않습니다.

## 재생

- Scenes/ThreeFingers_TaxScaleCollection_Preview.unity: 일반세 저울 회수, 60fps, 3초(0–180프레임).
- Scenes/ThreeFingers_DirectCoinCollection_Preview.unity: 직접 집기, 60fps, 1.2초(0–72프레임).
- 각 씬에서 Play. Animator는 한 번 재생하고 마지막 자세에서 멈춥니다. SideCamera를 켜면 옆에서 확인할 수 있습니다.
- 게임 씬에는 Prefabs의 해당 Rig를 배치하거나 복제해 사용합니다. 기존 Hand 프리팹의 Animator에 클립만 넣으면 경로가 맞지 않습니다. 이 클립은 Rig 루트의 Animator에서 Hand/... 본 경로를 제어합니다.

## 연결 프레임(0부터 시작)

|클립|프레임|초|AnimationEvent|연결 작업|
|---|---:|---:|---|---|
|일반세|30|0.50|OnTaxCoinsPicked|플레이어 접시 코인을 Hand/CoinStackGrip에 연결|
|일반세|75|1.25|OnTaxCoinsDeposited|손에서 코인 해제, 통 내부로 낙하 시작|
|일반세|87|1.45|없음|코인 낙하 완료, 통 바닥에 안착|
|일반세|120|2.00|OnTaxBowlPicked|통을 Hand/BowlRimGrip에 연결|
|일반세|180|3.00|OnTaxCollectionComplete|NPC 인계 위치 도착·정지|
|직접|30|0.50|OnDirectCoinsPicked|납부 코인 더미를 Hand/CoinStackGrip에 연결|
|직접|72|1.20|OnTaxCollectionComplete|NPC 인계 위치 도착·정지|

## 실제 게임에 연결

Rig 루트는 월드 위치/회전/균일 스케일을 조절할 수 있습니다. 전체 경로는 예제 저울·통 배치 기준이며 현재 게임의 UI 카메라 배치에 자동으로 맞추는 기능은 포함하지 않습니다. PlayerCoinStart, Bowl, NpcHandoff를 기준으로 실제 배치와 정렬하고 손 경로를 조정하세요. 프리팹의 Hand 자식 및 본 이름은 유지합니다.

미리보기 PaymentCoins와 Bowl에는 ParentConstraint가 설정되어 있습니다. 코인에는 손/통 두 소스가 있고, 75프레임에 손→통으로 전환합니다. 75–87프레임의 통 기준 오프셋 커브가 낙하를 표현합니다. 통은 120프레임까지 고정되고 그 이후 손을 따라가며 수평을 유지합니다. 실제 오브젝트로 교체할 때는 프리팹의 같은 이름 루트/제약을 유지하고 그 아래 시각 모델을 교체하는 방식이 가장 간단합니다.

별도 게임 로직으로 연결하려면 Animator가 있는 Rig 루트의 컴포넌트에 표의 이벤트와 같은 메서드를 구현하세요. intParameter는 프레임 번호입니다. 이벤트는 DontRequireReceiver로 저장되어 수신기가 없더라도 오류가 발생하지 않습니다. 집는 프레임의 월드 위치/회전을 유지한 상태에서 그립 오프셋을 계산해야 위치가 튀지 않습니다. 원본 그릇 프리팹은 별도로 보존되어 있습니다.

## 검증과 한계

분리된 Unity 프로젝트에서 두 클립의 정상 Animator 자동 재생, 매 프레임 네이티브 ParentConstraint 연결, 연결 시 순간이동, 끝 자세 유지, 통 수평, 접근/운반 중 손 메시와 통 벽 관통을 확인했습니다. 검증용 캡처는 Built-in 렌더러로 원본 색상을 보존해 촬영했고 게임용 씬은 URP 원본 재질을 참조합니다. 실제 게임의 세금 처리/코인 개수 차감/NPC 완료 처리는 연결하지 않았습니다.

제작 도구: AssetBuild/ThreeFingersTaxCollection/TaxCollectionAuthoring.cs. 기존 손 리그에 호환되는 Blender 원본이 없어 Unity에서 제작했습니다. 프레임별 결과: output/ThreeFingersTaxCollection/validation.json. 생성 도구와 보고서는 재현을 위해 보존합니다.
`;
fs.writeFileSync(path.join(assets,'Documentation','README.md'),readme);
const html=`<!doctype html><html lang="ko"><meta charset="utf-8"><title>ThreeFingers 세금 회수 미리보기</title><style>body{margin:0;background:#12191e;color:#efe8da;font:16px/1.65 system-ui}main{max-width:1280px;margin:auto;padding:30px}h1{font-size:27px}button,select{background:#67472b;color:white;border:1px solid #ba9256;padding:8px 16px;border-radius:6px;cursor:pointer}button.active{background:#a06a2b}section{margin:24px 0;padding:22px;background:#1c262d;border:1px solid #64503b;border-radius:12px}.views{display:flex;gap:12px}.views div{width:50%}img{width:100%;border-radius:8px;background:#11171c}input{width:100%}table{width:100%;border-collapse:collapse}td,th{text-align:left;padding:9px;border-bottom:1px solid #45505a}a{color:#efc789}code{font-size:13px}small{color:#adb7bf}pre{white-space:pre-wrap} @media(max-width:800px){.views{display:block}.views div{width:100%}}</style><main><h1>ThreeFingers 세금 회수 애니메이션</h1><p>① 접시 코인 집기 → 통에 투입 → 통을 집어 NPC 방향으로 운반<br>② 납부 코인 더미를 직접 집어 NPC 방향으로 운반</p><p><a href="ThreeFingers_TaxCollection.unitypackage">Unity 에셋 패키지</a> · <a href="../../Assets/ThreeFingersTaxCollection/Documentation/README.md">사용법</a> · <a href="verification-summary.json">검증 수치</a></p><section><button id="scale" class="active" onclick="choose('scale')">① 일반세 저울 회수 · 3초</button> <button id="direct" onclick="choose('direct')">② 직접 집기 · 1.2초</button><div class="views"><div><p>앞</p><img id="front"></div><div><p>옆</p><img id="side"></div></div><button onclick="running=!running;last=0">재생 / 정지</button> <select id="speed"><option value="1">1배속</option><option value="0.5">0.5배속</option></select> <b id="time"></b><input id="scrub" type="range" min="0" max="180" value="0" step="3"><div id="markers"></div><p><small>60fps 클립을 3프레임 간격으로 캡처했습니다. 끝에서 정지합니다. 프레임은 0부터 셉니다.</small></p></section><section><table><tr><th>동작</th><th>프레임</th><th>시각</th><th>이벤트</th></tr><tr><td>접시 코인 집기</td><td>30</td><td>0.50초</td><td>OnTaxCoinsPicked</td></tr><tr><td>통에 코인 놓기</td><td>75</td><td>1.25초</td><td>OnTaxCoinsDeposited</td></tr><tr><td>통 바닥 안착</td><td>87</td><td>1.45초</td><td>—</td></tr><tr><td>통 테두리 집기</td><td>120</td><td>2.00초</td><td>OnTaxBowlPicked</td></tr><tr><td>통 인계 위치 도착</td><td>180</td><td>3.00초</td><td>OnTaxCollectionComplete</td></tr><tr><td>직접 코인 집기</td><td>30</td><td>0.50초</td><td>OnDirectCoinsPicked</td></tr><tr><td>직접 인계 위치 도착</td><td>72</td><td>1.20초</td><td>OnTaxCollectionComplete</td></tr></table></section><section><h2>검증 결과</h2><p>실제 Animator 자동 재생과 네이티브 제약으로 ${stats.map(s=>s.samples).join(' / ')}프레임 샘플을 확인했습니다. 통 집기 전 위치 고정, 부착 전환, 끝 자세 유지, 통 수평을 검사했습니다.</p><pre>${JSON.stringify(stats,null,2)}</pre><p>손과 통 벽의 내부 관통 정점: 0. 원본 손/통/애니메이션과 게임 씬은 보존했습니다.</p><p>현재 게임의 세금 로직·UI 카메라 배치에는 아직 연결하지 않았습니다. 전용 프리팹/미리보기 씬과 이벤트를 제공하며, 실제 위치에 맞춘 정렬과 세금 완료 처리는 게임 쪽에서 연결합니다.</p></section></main><script>let mode='scale',frame=0,running=false,last=0;const scrub=document.getElementById('scrub');function show(){const f=Math.round(frame/3)*3;for(const v of ['front','side'])document.getElementById(v).src=mode+'/'+v+'_'+String(f).padStart(3,'0')+'.png';scrub.value=f;document.getElementById('time').textContent=f+' 프레임 · '+(f/60).toFixed(2)+'초'}function jump(f){running=false;frame=f;show()}function choose(m){mode=m;frame=0;running=false;last=0;scrub.max=m==='scale'?180:72;for(const k of ['scale','direct'])document.getElementById(k).classList.toggle('active',k===m);const marks=m==='scale'?[[30,'코인 집기'],[75,'투입'],[87,'안착'],[120,'통 집기'],[180,'도착']]:[[30,'코인 집기'],[72,'도착']];document.getElementById('markers').innerHTML=marks.map(([f,s])=>'<button onclick="jump('+f+')">'+s+' · '+f+'f</button>').join(' ');show()}scrub.oninput=()=>jump(+scrub.value);function tick(t){if(running&&last){frame+=Math.min(t-last,100)*.06*+document.getElementById('speed').value;if(frame>=+scrub.max){frame=+scrub.max;running=false}show()}last=t;requestAnimationFrame(tick)}choose('scale');requestAnimationFrame(tick);</script></html>`;
fs.writeFileSync(path.join(output,'index.html'),html.replace('onclick="running=!running;last=0"','onclick="if(frame>=+scrub.max)frame=0;running=!running;last=0"'));
console.log(JSON.stringify({stats,report:path.join(output,'index.html')},null,2));
