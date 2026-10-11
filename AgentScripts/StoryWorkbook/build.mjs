import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Workbook, SpreadsheetFile, FileBlob } from '@oai/artifact-tool';

const project = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const qa = path.join(project, 'AgentScripts/StoryWorkbook/qa');
await fs.mkdir(qa, { recursive: true });
const checkFile = process.argv[2];
const wb = checkFile ? await SpreadsheetFile.importXlsx(await FileBlob.load(checkFile)) : Workbook.create();
const specs = [
  ['NPC설정', ['NPC_ID','이름','역할','성격','공개설정','숨은설정','숨은설정_필요해금','자동생성','모델'], [16,18,34,62,45,45,30,14,16]],
  ['해금규칙', ['Rule_ID','NPC_ID','기본이야기','필요해금_ALL','열릴이야기','공개사실','활성'], [18,16,55,32,62,55,12]],
  ['플레이기록', ['회차','순서','NPC_ID','NPC','지형','이야기','종류','선택ID','실제로 본 말','결과','관계변화','능력치 전','능력치 후','코인 전','코인 후','아이템_ID','수량','방향','획득 해금','기록_KEY'], [10,10,16,18,18,50,16,30,65,20,13,22,22,22,22,25,10,15,30,30]],
  ['생성기록', ['이전회차','다음회차','챕터','이야기','모델','상태','이유','종료사유','결과 확인'], [12,12,18,50,18,22,65,42,45]],
];
if (!checkFile) {
  for (const [name, headers, widths] of specs) {
    const sheet = wb.worksheets.add(name);
    sheet.showGridLines = false;
    const area = sheet.getRangeByIndexes(0, 0, 2, headers.length);
    area.format.font = { name: 'Malgun Gothic', size: 11 };
    area.format.wrapText = true;
    area.format.verticalAlignment = 'center';
    area.format.rowHeight = 58;
    sheet.getRangeByIndexes(0,0,1,headers.length).values = [headers];
    sheet.getRangeByIndexes(0,0,1,headers.length).format = {
      fill: '#153B50', font: { name: 'Malgun Gothic', size: 11, bold: true, color: '#FFFFFF' },
      rowHeight: 32,
    };
    sheet.getRangeByIndexes(1,0,1,headers.length).format.fill = name === 'NPC설정' || name === '해금규칙' ? '#EAF4FD' : '#F3F6F7';
    widths.forEach((width,i) => sheet.getRangeByIndexes(0,i,4,1).format.columnWidth = width);
    sheet.freezePanes.freezeRows(1);
    sheet.freezePanes.freezeColumns(name === '플레이기록' ? 4 : 1);
    sheet.tabColor = name === 'NPC설정' || name === '해금규칙' ? '#1976A2' : '#47666B';
  }
  const people=[];
  for(const id of ['Guide','Duelist','Gambler']) {
    const p=JSON.parse(await fs.readFile(path.join(project, 'Assets/Decision_YYS/Resources/Story_Json_Data/Characters',id+'.json'),'utf8'));
    people.push([id,p.name,p.role,p.personality,'','','',id==='Guide'?'1':'0','Gemma']);
  }
  const npc=wb.worksheets.getItem('NPC설정');
  npc.getRange('A2:I4').values=people;
  npc.getRange('A2:I4').format = {fill:'#EAF4FD',font:{name:'Malgun Gothic',size:11,color:'#174FA4'},wrapText:true,verticalAlignment:'center',rowHeight:102};
  npc.getRange('H2:H30').dataValidation={rule:{type:'list',values:['0','1']}};
  npc.getRange('I2:I30').dataValidation={rule:{type:'list',values:['Gemma','Qwen']}};
  const rules=wb.worksheets.getItem('해금규칙');
  rules.getRange('A2:G2').values=[['','','','','','','0']];
  rules.getRange('G2:G100').dataValidation={rule:{type:'list',values:['0','1']}};
}
wb.recalculate();
for (const [name] of specs) {
  console.log((await wb.inspect({kind:'sheet', sheetId:name, range:name==='플레이기록'?'A1:K6':'A1:I5', maxChars:1800, include:'id,name,values'})).ndjson);
  const preview=await wb.render({sheetName:name,autoCrop:'all',scale:0.7,format:'png'});
  await fs.writeFile(path.join(qa, (checkFile?'actual-':'template-')+name+'.png'),new Uint8Array(await preview.arrayBuffer()));
}
console.log((await wb.inspect({kind:'match', searchTerm:'#REF!|#VALUE!|#DIV/0!|#NAME\\?|#N/A', options:{useRegex:true,maxResults:20},summary:'error check'})).ndjson);
if (!checkFile) {
  const output=await SpreadsheetFile.exportXlsx(wb);
  await output.save(path.join(project,'Assets/StreamingAssets/LocalStory/NpcStoryData.xlsx'));
  const inspection=path.join(project,'Assets/StreamingAssets/LocalStory/NpcStoryData.xlsx.inspect.ndjson');
  try { await fs.rename(inspection,path.join(qa,'template-inspect.ndjson')); } catch(e) { if(e.code!=='ENOENT') throw e; }
}
console.log(checkFile ? 'Verified runtime workbook: '+checkFile : 'Created NPC workbook template');
