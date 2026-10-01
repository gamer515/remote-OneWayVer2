import fs from "node:fs/promises";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { Presentation, PresentationFile } from "@oai/artifact-tool";

const SKILL_DIR = "C:/Users/유영선/.codex/plugins/cache/openai-primary-runtime/presentations/26.909.12148/skills/presentations";
const workspaceDir = "C:/remote-OneWayVer2";
const TMP_DIR = "C:/remote-OneWayVer2/tmp/ppt_yys/build";
const FINAL_PPTX = "C:/remote-OneWayVer2/outputs/과제제안서_발표자료_유영선_3분발표본_v9.pptx";
const GAME_IMAGES = {
  main: "C:/remote-OneWayVer2/tmp/ppt_yys/assets/game/main_gameplay.png",
  battle: "C:/remote-OneWayVer2/tmp/ppt_yys/assets/game/battle_dialogue.png",
  minigame: "C:/remote-OneWayVer2/tmp/ppt_yys/assets/game/minigame.png",
  expression: "C:/remote-OneWayVer2/tmp/ppt_yys/assets/game/character_expression.png",
};
const RUNTIME_PYTHON = "C:/Users/유영선/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe";

const { finalizePresentation, makeNativeBulletParagraphs } = await import(
  pathToFileURL(path.join(SKILL_DIR, "container_tools/artifact_tool_utils.mjs")).href,
);

await fs.mkdir(TMP_DIR, { recursive: true });
await fs.mkdir(path.dirname(FINAL_PPTX), { recursive: true });

const font = "Noto Sans KR";
const C = {
  bg: "#F7F5F0",
  white: "#FFFFFF",
  navy: "#173042",
  teal: "#18788A",
  tealSoft: "#D9EEF0",
  amber: "#D9854F",
  amberSoft: "#F6E6DA",
  ink: "#1E2933",
  muted: "#5E6B75",
  line: "#CBD4D8",
  graySoft: "#E9ECEB",
};

const deck = Presentation.create({ slideSize: { width: 1280, height: 720 } });

function rect(slide, left, top, width, height, fill, radius = 0, line = "none") {
  return slide.shapes.add({
    geometry: radius ? "roundRect" : "rect",
    position: { left, top, width, height },
    fill,
    line: line === "none" ? { fill: "none", width: 0 } : { style: "solid", fill: line, width: 1 },
    ...(radius ? { borderRadius: radius } : {}),
  });
}

function textBox(slide, text, left, top, width, height, opts = {}) {
  const s = slide.shapes.add({
    geometry: "textbox",
    position: { left, top, width, height },
    fill: opts.fill ?? "none",
    line: { fill: "none", width: 0 },
  });
  s.text = text;
  s.text.style = {
    typeface: font,
    fontSize: opts.fontSize ?? 24,
    bold: opts.bold ?? false,
    color: opts.color ?? C.ink,
    alignment: opts.align ?? "left",
    verticalAlignment: opts.valign ?? "middle",
    autoFit: opts.autoFit ?? "shrink",
  };
  return s;
}

function baseSlide(title, number) {
  const slide = deck.slides.add();
  slide.background.fill = C.bg;
  textBox(slide, title, 64, 38, 1040, 56, { fontSize: 42, bold: true, color: C.navy });
  textBox(slide, String(number).padStart(2, "0"), 1160, 42, 54, 38, { fontSize: 20, bold: true, color: C.teal, align: "right" });
  rect(slide, 64, 108, 1152, 2, C.line);
  return slide;
}

function bullets(slide, items, left, top, width, height, opts = {}) {
  const s = textBox(slide, "", left, top, width, height, { fontSize: opts.fontSize ?? 24, color: opts.color ?? C.ink, valign: "top" });
  s.text = makeNativeBulletParagraphs(items, {
    marginLeftPoints: opts.marginLeftPoints ?? 18,
    hangingPoints: opts.hangingPoints ?? 9,
    spaceAfterPoints: opts.spaceAfterPoints ?? 9,
  });
  s.text.style = {
    typeface: font,
    fontSize: opts.fontSize ?? 24,
    color: opts.color ?? C.ink,
    autoFit: "shrink",
    verticalAlignment: "top",
  };
  return s;
}

function processBlock(slide, n, title, body, left, top, width, fill) {
  rect(slide, left, top, width, 152, fill, 16, C.line);
  textBox(slide, String(n), left + 18, top + 18, 38, 38, { fontSize: 24, bold: true, color: C.white, align: "center", fill: C.teal });
  textBox(slide, title, left + 68, top + 14, width - 84, 46, { fontSize: 25, bold: true, color: C.navy });
  textBox(slide, body, left + 22, top + 66, width - 44, 72, { fontSize: 18, color: C.muted, valign: "top" });
}

// 1. Cover
{
  const slide = deck.slides.add();
  slide.background.fill = C.navy;
  rect(slide, 0, 0, 22, 720, C.amber);
  textBox(slide, "AIML 응용프로젝트 2", 82, 72, 420, 38, { fontSize: 21, bold: true, color: C.tealSoft });
  textBox(slide, "LLM 기반의 동적 서사 생성\n로그라이크 게임", 80, 150, 820, 180, { fontSize: 58, bold: true, color: C.white, valign: "top" });
  textBox(slide, "한국어 LLM을 게임 내부에 내장하고 플레이 기록에 따라\n다음 회차의 이야기와 콘텐츠를 변화시키는 시스템", 84, 360, 840, 92, { fontSize: 26, color: "#DDE6EA", valign: "top" });
  rect(slide, 82, 520, 1116, 112, "#21465A", 18);
  textBox(slide, "발표자", 112, 540, 100, 28, { fontSize: 17, bold: true, color: C.tealSoft });
  textBox(slide, "C211201 유영선", 112, 568, 290, 42, { fontSize: 29, bold: true, color: C.white });
  textBox(slide, "핵심 범위", 518, 540, 120, 28, { fontSize: 17, bold: true, color: C.tealSoft });
  textBox(slide, "내장 LLM · Excel/JSON · terrain · 미니게임", 518, 568, 610, 42, { fontSize: 25, bold: true, color: C.white });
  slide.speakerNotes.textFrame.setText("발표 시간 10초. 안녕하세요, C211201 유영선입니다. 저는 선택형 로그라이크 게임에서 한국어 LLM과 콘텐츠 데이터 시스템을 담당하고 있습니다. 지금부터 제가 구현할 기능과 계획을 말씀드리겠습니다.\n출처: C:/remote-OneWayVer2/outputs/제안서_유영선_존댓말본.docx");
}

// 2. Player journey and personal scope
{
  const slide = baseSlide("플레이 경험과 구현 범위", 2);
  const mainBytes = await fs.readFile(GAME_IMAGES.main);
  const battleBytes = await fs.readFile(GAME_IMAGES.battle);
  const minigameBytes = await fs.readFile(GAME_IMAGES.minigame);
  const expressionBytes = await fs.readFile(GAME_IMAGES.expression);
  slide.images.add({
    blob: mainBytes,
    contentType: "image/png",
    alt: "실제 게임의 대표 탐색과 대화 화면",
    fit: "cover",
    geometry: "roundRect",
    borderRadius: 16,
    position: { left: 64, top: 130, width: 684, height: 430 },
  });
  slide.images.add({
    blob: battleBytes,
    contentType: "image/png",
    alt: "실제 게임의 대화와 전투 준비 화면",
    fit: "cover",
    geometry: "roundRect",
    borderRadius: 14,
    position: { left: 766, top: 130, width: 450, height: 180 },
  });
  rect(slide, 766, 310, 450, 30, C.navy);
  textBox(slide, "대화와 전투 준비", 780, 310, 422, 30, { fontSize: 15, bold: true, color: C.white, align: "center" });
  slide.images.add({
    blob: minigameBytes,
    contentType: "image/png",
    alt: "실제 게임의 능력치 조작 화면",
    fit: "cover",
    geometry: "roundRect",
    borderRadius: 12,
    position: { left: 766, top: 354, width: 280, height: 146 },
  });
  rect(slide, 766, 500, 280, 32, C.navy);
  textBox(slide, "능력치 조작", 778, 500, 256, 32, { fontSize: 15, bold: true, color: C.white, align: "center" });
  slide.images.add({
    blob: expressionBytes,
    contentType: "image/png",
    alt: "NPC가 느끼는 플레이어 선호도를 표정으로 표현하는 화면",
    fit: "cover",
    geometry: "roundRect",
    borderRadius: 12,
    position: { left: 1060, top: 354, width: 156, height: 146 },
  });
  rect(slide, 1060, 500, 156, 32, C.navy);
  textBox(slide, "NPC 선호도 표현", 1062, 500, 152, 32, { fontSize: 12, bold: true, color: C.white, align: "center" });
  rect(slide, 64, 574, 1152, 82, C.navy, 14);
  const flow = ["조작·탐색", "이야기·선택", "능력치 관리", "퀘스트·미니게임", "챕터 전투 준비"];
  flow.forEach((label, i) => {
    textBox(slide, `${i + 1}`, 84 + i * 225, 593, 28, 28, { fontSize: 17, bold: true, color: C.navy, align: "center", fill: i === 4 ? C.amber : C.tealSoft });
    textBox(slide, label, 118 + i * 225, 584, 174, 46, { fontSize: 18, bold: true, color: C.white, align: "left" });
  });
  slide.speakerNotes.textFrame.setText("발표 시간 25초. 첫 번째 이미지는 현재 구현 중인 게임의 대표 화면입니다. 플레이어는 terrain에서 인물과 대화하고, 선택을 통해 능력치를 관리합니다. 오른쪽 화면처럼 능력치를 조작하고, NPC가 느끼는 플레이어 선호도를 표정으로 확인하면서 퀘스트와 미니게임을 수행해 각 챕터의 전투를 준비합니다. 저는 이 흐름에서 이야기 변화, 환경 구성과 미니게임 결과의 연결을 구현합니다.\n이미지 출처: 유영선 님이 제공한 실제 게임 캡처 화면");
}

// 3. Differentiation from Reigns, current gap and solution
{
  const slide = baseSlide("Reigns와의 차별성과 LLM 개선", 3);
  textBox(slide, "비교 항목", 76, 138, 142, 34, { fontSize: 18, bold: true, color: C.muted });
  textBox(slide, "Reigns", 244, 138, 330, 34, { fontSize: 23, bold: true, color: C.amber });
  textBox(slide, "내 게임", 684, 138, 410, 34, { fontSize: 23, bold: true, color: C.teal });
  rect(slide, 76, 184, 1128, 2, C.line);
  const comparisons = [
    ["선택 방식", "정해진 카드에서 좌우 선택", "선택과 이전 플레이 상태를 함께 반영"],
    ["이야기", "미리 작성된 카드와 해금 콘텐츠", "허용 문장을 로컬 한국어 LLM으로 변화"],
    ["확장 방식", "카드 해금과 네 지표의 균형", "terrain·퀘스트·미니게임 데이터로 연결"],
  ];
  comparisons.forEach((item, i) => {
    const y = 198 + i * 60;
    textBox(slide, item[0], 76, y, 142, 38, { fontSize: 18, bold: true, color: C.navy });
    textBox(slide, item[1], 244, y, 360, 38, { fontSize: 19, color: C.muted });
    textBox(slide, item[2], 684, y, 500, 38, { fontSize: 19, bold: true, color: C.navy });
    rect(slide, 76, y + 48, 1128, 1, C.line);
  });

  rect(slide, 76, 410, 430, 166, C.amberSoft, 16, C.amber);
  textBox(slide, "현재 문제", 104, 428, 180, 32, { fontSize: 22, bold: true, color: "#A94725" });
  textBox(slide, "정규식 입력에 의존\n한국어 인물·장소 보호 부족\n문자열 유사도와 단일 결과에 의존", 104, 470, 362, 88, { fontSize: 19, color: C.navy, valign: "top" });
  slide.shapes.add({
    geometry: "rightArrow",
    position: { left: 530, top: 456, width: 98, height: 72 },
    fill: C.teal,
    line: { fill: "none", width: 0 },
  });
  rect(slide, 652, 410, 552, 166, C.tealSoft, 16, C.teal);
  textBox(slide, "개선 방향", 680, 428, 180, 32, { fontSize: 22, bold: true, color: C.teal });
  textBox(slide, "JSON 입력과 변경 금지 목록\n앞뒤 문맥을 포함한 후보 3개 생성\n규칙·의미·루트 검사 후 원문 복구", 680, 470, 484, 88, { fontSize: 19, bold: true, color: C.navy, valign: "top" });
  textBox(slide, "입력과 검증을 안정화한 뒤 매개변수 규모가 다른 두 모델을 비교해 상황에 맞게 적용합니다.", 76, 620, 1132, 40, { fontSize: 20, bold: true, color: C.navy, align: "center" });
  slide.speakerNotes.textFrame.setText("발표 시간 45초. Reigns와 저희 게임의 차이는 세 가지입니다. Reigns는 정해진 카드에서 좌우 선택을 하지만, 저희 게임은 선택과 이전 플레이 상태를 함께 반영합니다. Reigns의 이야기는 미리 작성된 카드와 해금 콘텐츠 중심이지만, 저희 게임은 허용된 문장을 로컬 한국어 LLM으로 변화시킵니다. 또한 카드 해금에 머물지 않고 terrain과 퀘스트, 미니게임 데이터까지 연결합니다. 현재 입력과 검증 방식이 부족하므로 JSON 입력, 주변 문맥, 후보 생성과 단계별 검사를 추가하겠습니다. 이후 매개변수 규모가 다른 Kanana 두 버전을 비교해 실행 환경과 품질에 맞는 모델을 적용하겠습니다.\nReigns 공식 설명: https://www.devolverdigital.com/games/reigns\nKanana-2-3B 공식 모델: https://huggingface.co/kakaocorp/kanana-2-3b-instruct\n코드 출처: C:/Users/유영선/.codex/attachments/5bfba1a3-0c58-4647-b892-df9d0e33c049/붙여넣은 텍스트.txt");
}

// 4. Personal scope
{
  const slide = baseSlide("담당 기능", 4);
  const scope = [
    ["01", "입력 표준화", "JSON 스키마와 변경 금지 항목 정의"],
    ["02", "모델 비교", "매개변수 규모가 다른 Kanana 두 버전을 동일 조건에서 평가"],
    ["03", "품질 검증", "규칙·의미·루트 검사와 원문 복구"],
    ["04", "게임 연동", "검증 결과를 terrain·대화·미니게임에 반영"],
  ];
  scope.forEach((item, i) => {
    const y = 154 + i * 124;
    textBox(slide, item[0], 82, y, 82, 68, { fontSize: 34, bold: true, color: i % 2 ? C.amber : C.teal, align: "center" });
    textBox(slide, item[1], 190, y, 310, 42, { fontSize: 29, bold: true, color: C.navy });
    textBox(slide, item[2], 520, y, 660, 42, { fontSize: 23, color: C.muted });
    if (i < scope.length - 1) rect(slide, 86, y + 82, 1094, 1, C.line);
  });
  textBox(slide, "모델을 바로 학습시키기보다 평가 가능한 구조와 승인 데이터를 먼저 만듭니다.", 76, 650, 1128, 34, { fontSize: 21, bold: true, color: C.navy, align: "center" });
  slide.speakerNotes.textFrame.setText("발표 시간 20초. 제 담당 업무는 네 단계입니다. 먼저 JSON 입력 형식과 변경 금지 항목을 정의합니다. 다음으로 매개변수 규모가 다른 Kanana 두 버전을 같은 데이터로 비교한 후, 실행 환경과 결과 품질에 맞는 모델을 적용하겠습니다. 규칙·의미·루트 검증과 원문 복구를 구현하고 검증된 결과만 게임에 반영하겠습니다.\n출처: C:/remote-OneWayVer2/outputs/제안서_유영선_존댓말본.docx; C:/Users/유영선/.codex/attachments/5bfba1a3-0c58-4647-b892-df9d0e33c049/붙여넣은 텍스트.txt");
}

// 5. Required technology and end-to-end process
{
  const slide = baseSlide("필요 기술과 전체 과정", 5);
  const stages = [
    ["상태 수집", "Excel·SaveData", "선택·관계·현재 루트"],
    ["입력 검증", "Pydantic(입력 검증)", "필수 필드와 변경 금지 목록"],
    ["후보 생성", "Kanana 1.3B·3B", "문맥을 포함해 3개 생성"],
    ["품질 판정", "규칙·의미·루트", "통과 결과 선택·원문 복구"],
    ["게임 반영", "Unity·C#", "대화·terrain·미니게임"],
  ];
  stages.forEach((item, i) => {
    const x = 54 + i * 244;
    textBox(slide, String(i + 1).padStart(2, "0"), x, 150, 64, 44, { fontSize: 25, bold: true, color: i === 4 ? C.amber : C.teal });
    textBox(slide, item[0], x, 196, 210, 42, { fontSize: 25, bold: true, color: C.navy });
    textBox(slide, item[1], x, 244, 214, 34, { fontSize: 18, bold: true, color: C.teal });
    textBox(slide, item[2], x, 286, 214, 64, { fontSize: 17, color: C.muted, valign: "top" });
    if (i < 4) rect(slide, x + 220, 206, 2, 106, C.line);
  });
  rect(slide, 66, 390, 1148, 2, C.line);
  textBox(slide, "지원 기술", 72, 420, 170, 32, { fontSize: 21, bold: true, color: C.teal });
  const tech = [
    ["모델", "매개변수 규모가 다른 Kanana 두 버전 비교"],
    ["검증", "Pydantic, JSON Schema, 의미 검사"],
    ["학습", "승인·거부 데이터를 축적한 뒤 LoRA 파인튜닝"],
    ["자동화", "Codex, CLI, MCP, Git"],
  ];
  tech.forEach((item, i) => {
    const y = 468 + i * 48;
    textBox(slide, item[0], 88, y, 112, 30, { fontSize: 19, bold: true, color: C.navy });
    textBox(slide, item[1], 214, y, 860, 30, { fontSize: 20, color: C.muted });
  });
  textBox(slide, "좋은 결과와 실패 이유를 함께 저장해 이후 LoRA 학습 데이터로 사용합니다.", 72, 658, 1136, 30, { fontSize: 20, bold: true, color: C.navy, align: "center" });
  slide.speakerNotes.textFrame.setText("발표 시간 25초. 전체 과정은 다섯 단계입니다. 플레이 상태를 수집하고 파이댄틱과 JSON Schema로 입력을 먼저 검사합니다. 파이댄틱은 Python에서 필수 값과 데이터 형식이 맞는지 확인하고 오류를 알려 주는 입력 검증 도구입니다. 이후 후보 세 개를 생성하고 규칙·의미·루트 검사를 통과한 결과만 Unity에 반영합니다. 좋은 결과와 실패 이유는 이후 LoRA 학습 데이터로 사용하겠습니다.\n출처: C:/remote-OneWayVer2/outputs/제안서_유영선_존댓말본.docx; C:/Users/유영선/.codex/attachments/5bfba1a3-0c58-4647-b892-df9d0e33c049/붙여넣은 텍스트.txt");
}

// 6. Validation first, then feasibility
{
  const slide = baseSlide("검증 기준과 계획 가능성", 6);
  textBox(slide, "먼저 통과해야 하는 기준", 74, 138, 360, 34, { fontSize: 22, bold: true, color: C.teal });
  const checks = [
    ["형식", "JSON·ID·필수 필드 검사와 한국어 인물·장소 보존"],
    ["이야기", "앞뒤 문맥과 핵심 사건을 유지하고 현재 루트를 이탈하지 않음"],
    ["품질", "두 모델의 자연스러움, 반복 표현과 원문 복구율 비교"],
    ["성능", "로딩 시간, 응답 시간, GPU 메모리와 캐시 효과 측정"],
  ];
  checks.forEach((item, i) => {
    const y = 188 + i * 78;
    textBox(slide, "✓", 84, y, 42, 42, { fontSize: 24, bold: true, color: C.white, align: "center", fill: i === 0 ? C.amber : C.teal });
    textBox(slide, item[0], 146, y - 2, 150, 34, { fontSize: 22, bold: true, color: C.navy });
    textBox(slide, item[1], 300, y - 2, 868, 46, { fontSize: 20, color: C.muted });
  });
  rect(slide, 72, 522, 1136, 130, C.tealSoft, 16, C.line);
  textBox(slide, "계획 가능성", 96, 542, 180, 32, { fontSize: 22, bold: true, color: C.teal });
  textBox(slide, "현재 재시도와 원문 복구가 구현되어 있습니다.\n입력 표준화와 후보 평가를 추가하고, 승인 데이터가 쌓인 뒤 파인튜닝합니다.", 292, 536, 874, 84, { fontSize: 23, bold: true, color: C.navy, valign: "top" });
  slide.speakerNotes.textFrame.setText("발표 시간 30초. 검증은 네 부분으로 진행합니다. 먼저 JSON 형식과 한국어 인물·장소 보존을 확인합니다. 다음으로 앞뒤 문맥, 핵심 사건과 현재 루트를 유지하는지 검사합니다. 같은 평가 데이터로 1.3B와 3B의 자연스러움, 반복 표현과 원문 복구율을 비교하고, 로딩 시간과 GPU 메모리도 측정합니다. 현재 재시도와 원문 복구가 구현되어 있으므로 입력 표준화와 후보 평가를 먼저 추가한 뒤, 승인 데이터가 충분히 쌓였을 때 파인튜닝하겠습니다.\nKanana-2-3B 공식 모델: https://huggingface.co/kakaocorp/kanana-2-3b-instruct\n출처: C:/Users/유영선/.codex/attachments/5bfba1a3-0c58-4647-b892-df9d0e33c049/붙여넣은 텍스트.txt");
}

// 7. Personal schedule table
{
  const slide = baseSlide("유영선 수행 계획", 7);
  textBox(slide, "D 진행·완료   P 계획", 930, 126, 278, 28, { fontSize: 17, bold: true, color: C.muted, align: "right" });
  const tasks = [
    ["요구사항 및 기존 구조 분석", {1:"D",2:"D"}],
    ["선택형 스토리와 저장 구조 정비", {2:"D",3:"D",4:"D"}],
    ["LLM 이야기 변형 및 응답 검증", {3:"D",4:"D",5:"P",6:"P",7:"P"}],
    ["terrain prefab과 JSON 환경 구성", {3:"D",4:"D",5:"P",6:"P",7:"P",8:"P"}],
    ["코인 및 튜토리얼 미니게임 구현", {4:"D",5:"P",6:"P",7:"P",8:"P"}],
    ["Excel JSON 콘텐츠 관리 절차", {5:"P",6:"P",7:"P",8:"P",9:"P",10:"P"}],
    ["terrain별 환경 이야기 미니게임", {6:"P",7:"P",8:"P",9:"P",10:"P",11:"P",12:"P"}],
    ["한국어 LLM 비교 및 파인튜닝", {8:"P",9:"P",10:"P",11:"P",12:"P",13:"P"}],
    ["통합 성능 사용성 테스트", {10:"P",11:"P",12:"P",13:"P",14:"P"}],
  ];
  const values = [["설계요소", ...Array.from({length:15},(_,i)=>String(i+1))]];
  for (const [label, months] of tasks) values.push([label, ...Array.from({length:15},(_,i)=>months[i+1] ?? "")]);
  const table = slide.tables.add({
    rows: 10,
    columns: 16,
    left: 54,
    top: 162,
    width: 1172,
    height: 450,
    columnWidths: [270, ...Array(15).fill(60.13)],
    values,
  });
  table.rows[0].height = 42;
  for (let rowIndex = 1; rowIndex < 10; rowIndex++) table.rows[rowIndex].height = 45;
  table.borders.assign({ style: "solid", fill: C.line, width: 1 });
  table.cells.block({ row: 0, column: 0, rowCount: 1, columnCount: 16 }).assign({
    fill: C.navy,
    textStyle: { typeface: font, fontSize: 16, bold: true, color: C.white, alignment: "center" },
    anchor: "middle",
    margins: { left: 4, right: 4, top: 3, bottom: 3 },
  });
  table.cells.block({ row: 1, column: 0, rowCount: 9, columnCount: 1 }).assign({
    fill: C.graySoft,
    textStyle: { typeface: font, fontSize: 13, bold: true, color: C.navy, alignment: "left" },
    anchor: "middle",
    margins: { left: 7, right: 4, top: 3, bottom: 3 },
  });
  table.cells.block({ row: 1, column: 1, rowCount: 9, columnCount: 15 }).assign({
    fill: C.white,
    textStyle: { typeface: font, fontSize: 15, bold: true, color: C.muted, alignment: "center" },
    anchor: "middle",
    margins: { left: 2, right: 2, top: 2, bottom: 2 },
  });
  for (let r = 1; r <= 9; r++) {
    for (let c = 1; c <= 15; c++) {
      const value = values[r][c];
      if (value === "D") {
        table.getCell(r, c).fill = C.amberSoft;
        table.getCell(r, c).text.style = { typeface: font, fontSize: 15, bold: true, color: "#B34F2B", alignment: "center" };
      } else if (value === "P") {
        table.getCell(r, c).fill = C.tealSoft;
        table.getCell(r, c).text.style = { typeface: font, fontSize: 15, bold: true, color: C.teal, alignment: "center" };
      }
    }
  }
  textBox(slide, "전반부에는 기존 구조와 데이터 흐름을 정비하고, 후반부에는 한국어 LLM과 콘텐츠 통합 검증에 집중합니다.", 66, 636, 1148, 34, { fontSize: 18, bold: true, color: C.navy, align: "center" });
  slide.speakerNotes.textFrame.setText("발표 시간 15초. 전반부에는 기존 구조와 데이터 흐름을 정비하고, 후반부에는 콘텐츠 확장, 한국어 LLM 파인튜닝과 통합 테스트를 진행합니다. 유영선 개인 계획만 반영한 표입니다.\n출처: C:/remote-OneWayVer2/outputs/제안서_유영선_존댓말본.docx");
}

const requirements = {
  explicitTotalSlideCount: 7,
  requiredNativeTableOwnerSlides: [7],
  requiredNativeChartOwnerSlides: [],
  workspaceDir,
};
const fontPolicy = { basis: "design", families: [font] };
const expectedSlideSizeEmu = "12192000,6858000";
const stagingDir = path.join(workspaceDir, ".codex-finalizer-yys-ppt-v3");
await fs.mkdir(stagingDir, { recursive: true });
const candidatePath = path.join(stagingDir, "candidate-yys-3min.pptx");
await (await PresentationFile.exportPptx(deck)).save(candidatePath);

await finalizePresentation({
  ...requirements,
  candidatePath,
  finalPath: FINAL_PPTX,
  pythonExecutable: RUNTIME_PYTHON,
  integrityValidatorPath: path.join(SKILL_DIR, "container_tools/inspect_presentation_package_integrity.py"),
  layoutValidatorPath: path.join(SKILL_DIR, "container_tools/inspect_presentation_layout_geometry.py"),
  layoutArgs: [
    "--expected-slide-size-emu", expectedSlideSizeEmu,
    "--validate-bullet-geometry",
    "--validate-heading-fit",
    "--require-native-table-slide", "7",
  ],
  requiredNativeTableOwnerSlides: [7],
  fontPolicy,
  verifyArtifactToolImport: true,
  receiptPath: path.join(stagingDir, "과제제안서_발표자료_유영선_3분발표본_v9.validation.json"),
});

console.log(FINAL_PPTX);
