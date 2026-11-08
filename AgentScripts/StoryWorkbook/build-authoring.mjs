import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Workbook, SpreadsheetFile } from '@oai/artifact-tool';

const project = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const source = path.join(project, 'Assets/Decision_YYS/Resources/Chapters');
const output = path.join(project, 'Assets/StreamingAssets/ContentAuthoring');
const qa = path.join(project, 'AgentScripts/StoryWorkbook/qa/content-authoring');
const descriptions = {
  Chapters: '챕터 순서, 첫 챕터, 에피소드 및 챕터 설정',
  Maps: '지형 ID, 청크 길이와 개수',
  Places: '장소 ID, 연결 이야기, 좌표와 스폰 지점',
  NPCs: 'NPC ID와 인물 설정',
  Story: '이야기 등록, 대사 원문과 설명',
  Choices: '선택지, 비용, 보상 및 결과 분기',
  Rules: '챕터 규칙과 시험 수치',
  Taxes: '징수액, 대납 배율, 준비 이동과 NPC 대기 시간',
  TaxCatalog: '세목, 부과 방식, 단계별 징수 주기와 부담',
  Books: '네 능력별 기본 책 이름, 비용, 보상 및 학습 시간',
  Learning: '지능별 학습 용량, 학습 시간 범위와 배출 시간',
};
const rows = Object.fromEntries(Object.keys(descriptions).map(name => [name, []]));
const escape = value => value.replaceAll('~', '~0').replaceAll('/', '~1');

function workbookFor(document, field) {
  const name = document.split('/').at(-1);
  if (name === 'Omnibus' || name === 'Catalog' && field.startsWith('/chapters')) return 'Chapters';
  if (name === 'Characters') return 'NPCs';
  if (name === 'Taxes') return 'TaxCatalog';
  if (name === 'Rules' && /^\/(baseTax|alternatePaymentMultiplier|taxCostScale|encounterDistance|collector[^/]*)(\/|$)/.test(field)) return 'Taxes';
  if (name === 'Rules' || name === 'Books' || name === 'Learning') return name;
  if (name === 'Transform') return 'Places';
  if (name === 'Terrain') {
    if (field.startsWith('/actions')) return 'Choices';
    return /\/(places|facility[A-Z][^/]*)(?:\/|$)/.test(field) ? 'Places' : 'Maps';
  }
  if (name === 'Story' && /\/(options|choiceActions|choiceKind)(\/|$)/.test(field)) return 'Choices';
  return 'Story';
}

function flatten(document, value, field = '') {
  if (Array.isArray(value) && value.some(item => item !== null && typeof item === 'object')) {
    value.forEach((item, index) => flatten(document, item, `${field}/#${index}`));
  } else if (value && !Array.isArray(value) && typeof value === 'object' && Object.keys(value).length) {
    for (const [key, item] of Object.entries(value)) flatten(document, item, `${field}/${escape(key)}`);
  } else {
    const type = { string: 'text', number: 'number', boolean: 'flag' }[typeof value]
      ?? (value === null ? 'null' : 'json');
    const cell = type === 'json' ? JSON.stringify(value) : type === 'flag' ? Number(value) : value;
    rows[workbookFor(document, field)].push([document, field, type, cell]);
  }
}

for (const file of (await fs.readdir(source, { recursive: true })).filter(file => file.endsWith('.json')).sort()) {
  const document = file.replaceAll('\\', '/').slice(0, -5);
  flatten(document, JSON.parse((await fs.readFile(path.join(source, file), 'utf8')).replace(/^\uFEFF/, '')));
}

await fs.mkdir(output, { recursive: true });
await fs.mkdir(qa, { recursive: true });
for (const [name, values] of Object.entries(rows)) {
  if (process.argv.length > 2 && !process.argv.slice(2).includes(name)) continue;
  const workbook = Workbook.create();
  const sheet = workbook.worksheets.add('Data');
  sheet.showGridLines = false;
  sheet.freezePanes.freezeRows(1);
  const matrix = [['Document', 'Field', 'Type', 'Value'], ...values];
  const range = sheet.getRangeByIndexes(0, 0, matrix.length, 4);
  range.values = matrix;
  range.format = { font: { name: 'Malgun Gothic', size: 11, color: '#213749' }, rowHeight: 21 };
  sheet.getRange('A1:D1').format = {
    fill: '#223F53', font: { name: 'Malgun Gothic', bold: true, color: '#FFFFFF' }, rowHeight: 28,
  };
  [40, 68, 12, 83].forEach((width, i) => {
    sheet.getRangeByIndexes(0, i, matrix.length, 1).format.columnWidth = width;
  });
  if (values.length) {
    const editCells = sheet.getRangeByIndexes(1, 3, values.length, 1);
    editCells.format.fill = '#FFF4CC';
    editCells.format.wrapText = true;
    values.forEach((row, index) => {
      const text = String(row[3] ?? '');
      const lines = text.split('\n').reduce((sum, line) => sum + Math.max(1, Math.ceil(line.length / 44)), 0);
      if (lines > 1) sheet.getRangeByIndexes(index + 1, 0, 1, 4).format.rowHeight = Math.min(240, 8 + 17 * lines);
    });
  }
  const guide = workbook.worksheets.add('안내');
  const instructions = [
    ['콘텐츠 분류', descriptions[name]],
    ['편집할 열', 'Data의 노란 Value 열. Document, Field, Type은 데이터 구조입니다.'],
    ['경로', '#0부터 시작하는 배열 인덱스. ~1은 슬래시, ~0은 물결표입니다.'],
    ['값', 'text는 일반 문자열, number는 숫자, flag는 0/1, json은 JSON 배열/객체입니다.'],
    ['적용', 'Saves/ContentAuthoring의 파일을 편집하고 게임을 다시 실행합니다.'],
  ];
  guide.getRange('A1:B5').values = instructions;
  guide.getRange('A1:A5').format.columnWidth = 22;
  guide.getRange('B1:B5').format.columnWidth = 82;
  guide.getRange('A1:B5').format.wrapText = true;
  guide.getRange('A1:B5').format.rowHeight = 32;
  guide.getRange('A1:A5').format.fill = '#E5EEF4';
  workbook.recalculate();
  if (name === 'TaxCatalog') {
    const preview = await workbook.render({ sheetName: '안내', range: 'A1:B5', scale: 0.8, format: 'png' });
    await fs.writeFile(path.join(qa, 'TaxCatalog-guide.png'), new Uint8Array(await preview.arrayBuffer()));
  }
  const file = await SpreadsheetFile.exportXlsx(workbook);
  const destination = path.join(output, `${name}.xlsx`);
  await file.save(destination);
  await fs.rename(`${destination}.inspect.ndjson`, path.join(qa, `${name}.inspect.ndjson`)).catch(error => {
    if (error.code !== 'ENOENT') throw error;
  });
  if (['Chapters', 'Story', 'Books', 'TaxCatalog'].includes(name)) {
    const preview = await workbook.render({ sheetName: 'Data', range: 'A1:D8', scale: 0.8, format: 'png' });
    await fs.writeFile(path.join(qa, `${name}.png`), new Uint8Array(await preview.arrayBuffer()));
  }
  console.log(`${name}: ${values.length} rows`);
}
