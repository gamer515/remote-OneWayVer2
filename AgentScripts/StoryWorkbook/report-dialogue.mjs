import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const project = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const source = path.join(project, 'Assets/Decision_YYS/Resources/Chapters');
const [input, savePath] = process.argv.slice(2);
if ((input === '--saves' && !savePath) || (input === '--game-saves' && !process.env.LOCALAPPDATA)) {
  throw new Error('저장 폴더를 지정하세요. --saves <Saves 폴더>');
}
const saves = input === '--game-saves'
  ? path.resolve(process.env.LOCALAPPDATA, '../LocalLow/DefaultCompany/OneWayInVc/Saves')
  : input === '--saves' ? path.resolve(savePath) : null;
const snapshot = saves ? await readOptionalJson(path.join(saves, 'ContentAuthoring/AppliedContent.json'))
  : input ? await readJson(path.resolve(input)) : null;
const cache = new Map();

async function readJson(file) {
  return JSON.parse((await fs.readFile(file, 'utf8')).replace(/^\uFEFF/, ''));
}

async function readOptionalJson(file) {
  try {
    return await readJson(file);
  } catch (error) {
    if (error.code !== 'ENOENT') throw error;
    return null;
  }
}

async function document(key) {
  if (!cache.has(key)) cache.set(key, snapshot?.[key] ?? await readJson(path.join(source, key + '.json')));
  return cache.get(key);
}

function escape(value) {
  return String(value ?? '').replace(/[&<>"']/g, character => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
  })[character]);
}

async function savedDialogue(root) {
  const directory = path.join(root, 'LocalStoryTraining');
  let files;
  try {
    files = await fs.readdir(directory, { recursive: true, withFileTypes: true });
  } catch (error) {
    if (error.code !== 'ENOENT') throw error;
    return [];
  }
  const rows = [];
  for (const file of files.filter(entry => entry.isFile() && entry.name.endsWith('.jsonl'))) {
    const filename = path.join(file.parentPath, file.name);
    const [chapter, episode, folder, ...story] = path.relative(directory, filename).split(path.sep);
    const run = /^For_Run_(\d+)$/.exec(folder)?.[1];
    if (!run || !story.length) continue;
    const lines = (await fs.readFile(filename, 'utf8')).split(/\r?\n/);
    for (const line of lines.filter(line => line.trim())) {
      const { messages } = JSON.parse(line);
      const prompt = messages[0].content;
      const { emotion, npc, original } = JSON.parse(prompt.slice(prompt.lastIndexOf('\n\n') + 2));
      const { text } = JSON.parse(messages[1].content);
      if (typeof original !== 'string' || typeof text !== 'string' || original === text) continue;
      rows.push({ chapter, episode, run: Number(run), story: story.join('/').replace(/\.jsonl$/, ''),
        emotion, npc: npc.name, original, text });
    }
  }
  return rows.sort((a, b) => b.run - a.run || a.episode.localeCompare(b.episode) || a.story.localeCompare(b.story));
}

function dialoguePair(row) {
  const label = [row.run && row.run + '회차', row.story, row.npc, row.emotion].filter(Boolean).join(' · ');
  return `<article class="pair"><small>${escape(label)}</small>
<p><strong>원문</strong> ${escape(row.original)}</p>
<p><strong>변경 대사</strong> ${escape(row.text)}</p></article>`;
}

const verifiedExamples = [
  {
    story: 'brook', npc: '브룩', emotion: '겁',
    original: '이상하네. 어제까진 이 곡괭이가 이렇게 무겁지 않았는데.',
    text: '으으, 이상하네. 어제까진 이 곡괭이가 이렇게 무겁지 않았는데…',
  },
  {
    story: 'brook', npc: '브룩', emotion: '화',
    original: '안쪽으로 들어가려면 통행세부터 내.',
    text: '쯧, 안쪽으로 들어가려면 통행세부터 내!',
  },
];
const paraphrasedExample = {
  story: 'brook', npc: '브룩', emotion: '겁',
  original: '이상하네. 어제까진 이 곡괭이가 이렇게 무겁지 않았는데.',
  text: '어… 갑자기 왜 이렇게 무거운 건가? 어제까지만 해도 이 곡괭이가 이렇게 묵직하지 않았는데…',
};

const catalog = await document('Catalog');
const omnibus = await document('Omnibus');
const changes = saves ? await savedDialogue(saves) : [];
const reportFile = saves && path.join(saves, 'LocalStoryTraining', 'DialogueChanges.html');
const relativeReport = reportFile && path.relative(path.join(project, 'Reports'), reportFile);
const reportLink = relativeReport && escape(path.isAbsolute(relativeReport) ? pathToFileURL(reportFile).href
  : relativeReport.split(path.sep).map(encodeURIComponent).join('/'));
const chapterTitles = { Chapter_1: '갱도', Chapter_Coast: '해안' };
const sections = [];
let storyCount = 0;
let cardCount = 0;
for (const chapter of omnibus.chapters) {
  const entry = catalog.chapters.find(item => item.id === chapter.chapterId);
  const name = escape(entry.name ?? chapterTitles[entry.id] ?? entry.id);
  const { characters } = await document(entry.folder + '/Characters');
  const npcs = new Map(characters.map(npc => [npc.characterId, npc]));
  const episodes = [];
  for (const episode of chapter.episodeIds) {
    const stories = [];
    for (const story of catalog.stories.filter(item => item.chapterId === entry.id && item.episodeId === episode)) {
      const authored = await document(story.storyFile.replace(/\.json$/, ''));
      const rows = [];
      for (const card of authored[story.key].MainStory) {
        const npc = card.speakID?.toLowerCase() !== 'player' ? npcs.get(card.speakID) : null;
        const isPlayer = card.speakID?.toLowerCase() === 'player';
        const otherSpeaker = isPlayer ? '플레이어' : card.speakID || '지문';
        const choices = (card.options ?? []).map((choice, index) => {
          const branch = card.choiceActions?.[index]?.storyPath;
          return '<li>' + escape(choice) + (branch ? '<br><code>' + escape(branch) + '</code>' : '') + '</li>';
        }).join('');
        rows.push(`<tr><td>${escape(card.ID)}</td><td>${npc ? `<strong>${escape(npc.name || card.speakID)}</strong><br>${escape(card.Text)}` : ''}</td>
<td>${npc ? '' : `<strong>${escape(otherSpeaker)}</strong><br>${escape(card.Text)}`}</td>
<td>${choices ? '<ol>' + choices + '</ol>' : ''}</td></tr>`);
      }
      storyCount++;
      cardCount += rows.length;
      const target = story.rewriteAllowed === false ? '원문 유지' :
        story.rewriteEmotions?.length ? story.rewriteEmotions.join(' · ') : '기본 감정 목록';
      stories.push(`<details><summary>${escape(story.key)} <small>${escape(npcs.get(story.characterId)?.name ?? story.characterId)} · ${rows.length}장 · ${escape(target)}</small></summary>
<p><code>${escape(story.path)}</code></p><table><thead><tr><th>카드 ID</th><th>NPC 발화</th><th>플레이어·지문·기타</th><th>플레이어 선택지와 분기</th></tr></thead>
<tbody>${rows.join('\n')}</tbody></table></details>`);
    }
    episodes.push(`<h3>${escape(episode)}</h3>${stories.length ? stories.join('\n') : '<p>등록된 대화가 없습니다.</p>'}`);
  }
  const npcRows = characters.map(npc => `<tr><td>${escape(npc.name)} <code>${escape(npc.characterId)}</code></td>
<td>${escape(npc.role)}</td><td>${escape(npc.speechStyle || npc.personality || '원문의 말투 참고')}</td>
<td>${npc.autoGenerate === false ? '원문 유지' : '대상'}</td></tr>`).join('\n');
  sections.push({
    id: escape(entry.id), name, html: `<section id="${escape(entry.id)}"><h2>${name} <small>${escape(entry.id)}</small></h2>
${entry.id === 'Chapter_1' ? '<h3 id="paraphrased-pair">새 프롬프트의 실제 Gemma 검증 예시</h3>' +
    dialoguePair(paraphrasedExample) + '<h3 id="verified-pairs">이전 프롬프트의 검증 예시</h3>' +
    verifiedExamples.map(dialoguePair).join('\n') : ''}
${input === '--saves' ? changes.filter(row => row.chapter === entry.id).map(dialoguePair).join('\n') : ''}
${npcRows ? '<details><summary>NPC 설정</summary><table><thead><tr><th>NPC</th><th>역할</th><th>말투·성격</th><th>감정 재작성</th></tr></thead><tbody>' + npcRows + '</tbody></table></details>' : ''}
${episodes.join('\n')}</section>`,
  });
}

const html = `<!doctype html>
<html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>챕터별 이야기와 변경 대사</title>
<style>
:root{color-scheme:light;background:#faf8f2;color:#23303a;font-family:system-ui,sans-serif;line-height:1.6}
body{max-width:1050px;padding:0 20px 40px;margin:32px auto}section{margin:38px 0}
nav a{display:inline-block;margin-right:16px}small{font-size:.82em;color:#52636f;font-weight:normal}
details{border:1px solid #cbc3b4;border-radius:6px;padding:10px 14px;margin:8px 0;background:#fffdf8;overflow:auto}
summary{cursor:pointer;font-weight:650}table{width:100%;border-collapse:collapse;margin:10px 0}
td,th{text-align:left;vertical-align:top;padding:8px;border:1px solid #d4ccbf;overflow-wrap:anywhere}
th{background:#efe9dd}td:first-child{min-width:64px}td{white-space:pre-line}code{overflow-wrap:anywhere}ol{padding-left:20px;margin:0}
.pair{border:1px solid #cbc3b4;border-radius:8px;padding:10px 16px;margin:10px 0;background:#fff}.pair p{margin:6px 0;white-space:pre-wrap}.pair strong{display:block}.pair p:last-child{background:#e5f2dc;padding:7px 9px;border-radius:4px}
.live-report{width:100%;height:65vh;min-height:360px;border:1px solid #cbc3b4;border-radius:8px}
</style></head>
<body><p><a href="development-report.html#gemma-emotion-dialogue">Gemma 적용 보고서</a></p>
<h1>챕터별 이야기와 변경 대사</h1>
<p>${snapshot ? '게임이 저장한 적용본' : '프로젝트 원본'} · 이야기 ${storyCount}개 · 카드 ${cardCount}개</p>
${reportLink ? `<h2 id="live-dialogue">게임에서 저장한 변경 대사</h2>
<p>생성 완료 로그가 나온 뒤 이 페이지를 새로고침하세요. <a href="${reportLink}">게임이 저장한 HTML 직접 열기</a></p>
<iframe class="live-report" src="${reportLink}" title="게임에서 저장한 원문과 변경 대사"></iframe>` :
    '<p>게임이 회차 종료 후 검사를 통과한 대사를 저장하면 <code>Saves/LocalStoryTraining/DialogueChanges.html</code>을 갱신합니다.</p>'}
<nav aria-label="챕터">${sections.map(chapter => `<a href="#${chapter.id}">${chapter.name}</a>`).join('')}</nav>
${sections.map(chapter => chapter.html).join('\n')}
</body></html>
`;
await fs.writeFile(path.join(project, 'Reports/chapter-dialogue.html'), html, 'utf8');
console.log(`Chapters: ${sections.length}, stories: ${storyCount}, cards: ${cardCount}`);
