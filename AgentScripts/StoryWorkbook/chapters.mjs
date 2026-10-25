import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Workbook, SpreadsheetFile, FileBlob } from '@oai/artifact-tool';
const project=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const output=path.join(project,'Assets/StreamingAssets/LocalStory/Chapters');
const qa=path.join(project,'AgentScripts/StoryWorkbook/qa/chapters');
await fs.mkdir(qa,{recursive:true});
if(process.argv.includes('--preview-before')) {
 const wb=await SpreadsheetFile.importXlsx(await FileBlob.load(path.join(output,'Chapter_00.xlsx')));
 const blob=await wb.render({sheetName:'NPC설정',range:'A1:D4',scale:1,format:'png'});
 await fs.writeFile(path.join(qa,'before-numbering.png'),new Uint8Array(await blob.arrayBuffer()));
 const unlock=await wb.render({sheetName:'해금규칙',range:'A1:F3',scale:0.8,format:'png'});
 await fs.writeFile(path.join(qa,'before-unlocks.png'),new Uint8Array(await unlock.arrayBuffer()));
 console.log((await wb.inspect({kind:'sheet',include:'id,name',maxChars:1200})).ndjson);process.exit(0);
}
if(process.argv.includes('--verify-existing')) {
 const folder=process.argv[process.argv.indexOf('--verify-existing')+1];
 for(const file of (await fs.readdir(folder)).filter(file=>file.endsWith('.xlsx')).sort()) {
  const wb=await SpreadsheetFile.importXlsx(await FileBlob.load(path.join(folder,file)));
  console.log(file,(await wb.inspect({kind:'sheet',include:'id,name',maxChars:1100})).ndjson);
  const views=process.argv.includes('--verify-unlocks')?[['해금규칙','A1:E6']]:[['NPC설정','A1:G4'],['해금규칙','A1:E6'],['아이템보상','A1:G5']];
  console.log((await wb.inspect({kind:'table',range:'해금규칙!A1:D6',include:'values',tableMaxRows:6,tableMaxCols:4,maxChars:1500})).ndjson);
  for(const [name,range] of views) {
   const blob=await wb.render({sheetName:name,range,scale:0.8,format:'png'});
   await fs.writeFile(path.join(qa,`actual-${file}-${name}.png`),new Uint8Array(await blob.arrayBuffer()));
  }
 }
 process.exit(0);
}
const chapters=JSON.parse(await fs.readFile(path.join(project,'tmp/NotebookTemplates.json'),'utf8'));
const textWidth=value=>[...String(value??'')].reduce((n,c)=>n+(c.charCodeAt(0)>255?2:1),0);
for(const chapter of chapters) {
 const wb=Workbook.create();
 for(const spec of chapter.sheets) {
  const {name,headers,widths}=spec;
  const rows=(spec.rows.length?spec.rows:[headers.map(()=>null)]).map(row=>row.map((v,j)=>['자동생성','해금상태','체력','속도','지능','매력'].includes(headers[j])&&v!==''&&v!=null?Number(v):v));
  const sheet=wb.worksheets.add(name);sheet.showGridLines=false;sheet.tabColor='#496A91';
  const used=sheet.getRangeByIndexes(0,0,rows.length+1,headers.length);
  used.format={font:{name:'Malgun Gothic',size:11,color:'#25333D'},wrapText:true,verticalAlignment:'top',rowHeight:44};
  sheet.getRangeByIndexes(0,0,1,headers.length).values=[headers];
  sheet.getRangeByIndexes(1,0,rows.length,headers.length).values=rows;
  sheet.getRangeByIndexes(0,0,1,headers.length).format={fill:'#263E55',font:{name:'Malgun Gothic',size:11,bold:true,color:'#FFFFFF'},rowHeight:40,horizontalAlignment:'center',verticalAlignment:'center',wrapText:true};
  widths.forEach((width,i)=>sheet.getRangeByIndexes(0,i,rows.length+1,1).format.columnWidth=width);
  rows.forEach((row,i)=>{
   const range=sheet.getRangeByIndexes(i+1,0,1,headers.length);
   range.format.fill=i%2===0?'#FFF8E8':'#FFFFFF';
   range.format.borders={bottom:{style:'thin',color:'#CCD5DD'}};
   const lines=Math.max(...row.map((v,j)=>String(v??'').split('\n').reduce((n,line)=>n+Math.max(1,Math.ceil(textWidth(line)/Math.max(8,widths[j]-4))),0)));
   range.format.rowHeight=Math.min(409,Math.max(44,lines*18+20));
  });
  sheet.freezePanes.freezeRows(1);sheet.freezePanes.freezeColumns(1);
  if(name==='NPC설정') {
   sheet.getRange('F2:F100').dataValidation={rule:{type:'list',values:['0','1']}};
   sheet.getRange('G2:G100').dataValidation={rule:{type:'list',values:['Gemma','Qwen']}};
   sheet.getRangeByIndexes(1,5,rows.length,2).format.fill='#FFF0BD';
  }
  if(name==='해금규칙') {
   sheet.getRange('D2:D100').dataValidation={rule:{type:'list',values:['0','1']}};
   sheet.getRangeByIndexes(1,3,rows.length,1).format.horizontalAlignment='center';
  }
 }
 wb.recalculate();
 console.log(chapter.folder,(await wb.inspect({kind:'sheet',include:'id,name',maxChars:1300})).ndjson);
 for(const spec of chapter.sheets) {
  const range=spec.name==='NPC설정'?'A1:G4':spec.name==='해금규칙'?'A1:F3':'A1:G5';
  const blob=await wb.render({sheetName:spec.name,range,scale:0.8,format:'png'});
  await fs.writeFile(path.join(qa,`${chapter.folder}-${spec.name}.png`),new Uint8Array(await blob.arrayBuffer()));
 }
 await(await SpreadsheetFile.exportXlsx(wb)).save(path.join(output,chapter.folder+'.xlsx'));
}
