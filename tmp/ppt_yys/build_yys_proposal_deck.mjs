import fs from "node:fs/promises";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { Presentation, PresentationFile } from "@oai/artifact-tool";

const SKILL_DIR = "C:/Users/유영선/.codex/plugins/cache/openai-primary-runtime/presentations/26.909.12148/skills/presentations";
const workspaceDir = "C:/remote-OneWayVer2";
const TMP_DIR = "C:/remote-OneWayVer2/tmp/ppt_yys/build";
const FINAL_PPTX = "C:/remote-OneWayVer2/outputs/과제제안서_발표자료_유영선_수정본.pptx";
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
  slide.speakerNotes.textFrame.setText("발표에서는 프로젝트 전체 설명을 짧게 제시한 뒤, 제가 담당할 내장형 한국어 LLM과 콘텐츠 데이터 제작 과정을 중심으로 설명합니다.\n출처: C:/remote-OneWayVer2/outputs/제안서_유영선_존댓말본.docx");
}

// 2. Goal and differentiation
{
  const slide = baseSlide("프로젝트 목표와 차별성", 2);
  textBox(slide, "목표", 70, 138, 130, 34, { fontSize: 20, bold: true, color: C.teal });
  textBox(slide, "플레이어의 선택과 이전 회차 기록을 사용해 다음 구간의 이야기 표현을 바꾸고,\nterrain 환경과 미니게임까지 하나의 반복 플레이 구조로 연결합니다.", 70, 176, 1110, 80, { fontSize: 27, bold: true, color: C.navy, valign: "top" });
  processBlock(slide, 1, "기록", "선택, 방문 사건, 인물 관계, 아이템과 해금 상태를 저장합니다.", 74, 310, 346, C.white);
  processBlock(slide, 2, "이야기 변화", "내장형 한국어 LLM이 허용된 카드 문장과 세부 전개를 변경합니다.", 467, 310, 346, C.tealSoft);
  processBlock(slide, 3, "콘텐츠 연결", "변경된 이야기를 terrain, Encounter와 미니게임 결과에 반영합니다.", 860, 310, 346, C.amberSoft);
  textBox(slide, "차별성", 74, 516, 110, 28, { fontSize: 20, bold: true, color: C.amber });
  bullets(slide, [
    "외부 서버 없이도 한국어 이야기 변경 기능을 실행합니다.",
    "자유 생성 범위를 JSON 구조와 게임 상태 안으로 제한해 진행 안정성을 확보합니다.",
    "문장 변화에 그치지 않고 환경, 선택 조건과 미니게임의 연결까지 관리합니다.",
  ], 74, 552, 1120, 124, { fontSize: 21, spaceAfterPoints: 7 });
  slide.speakerNotes.textFrame.setText("평가 항목 중 설계목표의 명료성과 아이디어의 창의성에 해당하는 내용입니다. Reigns의 선택 방식과 생성형 서사의 장점을 참고하되, 구조화된 상태와 로컬 LLM을 결합하는 점을 차별점으로 설명합니다.\n출처: C:/Users/유영선/Downloads/프로젝트_평가표양식_2026.pdf; C:/remote-OneWayVer2/outputs/제안서_유영선_존댓말본.docx");
}

// 3. Personal scope
{
  const slide = baseSlide("유영선 담당 범위", 3);
  textBox(slide, "프로젝트에서 제가 직접 설계하고 구현할 네 가지 범위입니다.", 70, 130, 900, 40, { fontSize: 23, color: C.muted });
  processBlock(slide, 1, "한국어 LLM 내장", "모델 선정, 파인튜닝, 경량화와 로컬 추론 환경을 구성합니다.", 72, 204, 540, C.tealSoft);
  processBlock(slide, 2, "이야기 데이터 검증", "입력·출력 JSON을 정의하고 문맥, 식별자와 변경 범위를 검사합니다.", 668, 204, 540, C.white);
  processBlock(slide, 3, "terrain 콘텐츠 관리", "Excel 원본과 JSON 실행 데이터를 연결해 환경과 사건을 구성합니다.", 72, 396, 540, C.white);
  processBlock(slide, 4, "미니게임과 진행 연결", "코인, 튜토리얼과 후속 미니게임의 결과를 보상과 이야기로 반영합니다.", 668, 396, 540, C.amberSoft);
  textBox(slide, "기존 선택형 스토리 UI, 세이브 데이터와 인벤토리 구조를 발전시키며 새로운 콘텐츠를 추가합니다.", 74, 614, 1130, 42, { fontSize: 22, bold: true, color: C.navy, align: "center" });
  slide.speakerNotes.textFrame.setText("공통 개발 내용보다 제가 맡은 구현 범위를 중심으로 설명합니다. 역할 분담의 적합성과 실제 수행 가능성을 보여 주는 슬라이드입니다.\n출처: C:/remote-OneWayVer2/outputs/제안서_유영선_존댓말본.docx");
}

// 4. Embedded LLM design
{
  const slide = baseSlide("내장형 한국어 LLM 설계", 4);
  textBox(slide, "게임 내부 추론 흐름", 72, 132, 420, 34, { fontSize: 22, bold: true, color: C.teal });
  const steps = [
    ["플레이 기록", "선택·사건·관계·현재 terrain"],
    ["입력 JSON", "변경 대상 카드와 허용 범위"],
    ["로컬 LLM", "한국어 이야기 변경안 생성"],
    ["검증과 저장", "문맥·형식·식별자 검사"],
  ];
  steps.forEach((item, i) => {
    const y = 182 + i * 112;
    rect(slide, 74, y, 586, 88, i === 2 ? C.tealSoft : C.white, 14, C.line);
    textBox(slide, String(i + 1), 92, y + 22, 38, 38, { fontSize: 22, bold: true, color: C.white, align: "center", fill: i === 2 ? C.amber : C.teal });
    textBox(slide, item[0], 150, y + 10, 185, 32, { fontSize: 23, bold: true, color: C.navy });
    textBox(slide, item[1], 150, y + 42, 474, 30, { fontSize: 18, color: C.muted });
  });
  textBox(slide, "검증 기준", 728, 132, 220, 34, { fontSize: 22, bold: true, color: C.teal });
  bullets(slide, [
    "인터넷 연결 없이 모델이 정상적으로 로드되는지 확인합니다.",
    "한국어 문장의 자연스러움과 인물·장소의 일관성을 비교합니다.",
    "요청한 JSON 필드와 카드 순서를 유지하는지 검사합니다.",
    "메모리 사용량, 초기 로딩과 추론 시간을 측정합니다.",
  ], 716, 182, 494, 300, { fontSize: 22, spaceAfterPoints: 12 });
  rect(slide, 716, 510, 492, 112, C.navy, 16);
  textBox(slide, "파인튜닝 목표", 742, 526, 180, 30, { fontSize: 20, bold: true, color: C.tealSoft });
  textBox(slide, "핵심 사건은 유지하면서 허용된 문장과 세부 전개만 변경하는 한국어 모델", 742, 562, 434, 48, { fontSize: 22, bold: true, color: C.white, valign: "top" });
  slide.speakerNotes.textFrame.setText("외부 API가 아니라 게임 내부에서 동작하는 한국어 LLM을 담당합니다. 모델 크기와 성능을 비교하고, 파인튜닝으로 허용된 부분만 변경하도록 학습시킬 계획입니다.\n출처: C:/remote-OneWayVer2/outputs/제안서_유영선_존댓말본.docx");
}

// 5. Excel / JSON pipeline
{
  const slide = baseSlide("Excel·JSON 콘텐츠 제작", 5);
  textBox(slide, "기획자가 전체 관계를 확인하고 Unity가 검증된 데이터만 읽도록 제작 절차를 분리합니다.", 72, 132, 1120, 42, { fontSize: 23, color: C.muted });
  const blocks = [
    ["Excel 콘텐츠 맵", "terrain, 인물, Story, Interaction,\n아이템과 미니게임 관계"],
    ["변환·참조 검사", "placeId, prefabId, 이야기 경로,\n해금 키의 누락과 중복 확인"],
    ["JSON 실행 데이터", "검증된 배치, 조건, 보상과\n후속 이야기 연결 정보"],
    ["Unity 콘텐츠", "terrain prefab, NPC, Encounter와\n미니게임을 실행 시점에 구성"],
  ];
  blocks.forEach((item, i) => {
    const x = 60 + i * 304;
    rect(slide, x, 224, 272, 176, i === 0 ? C.amberSoft : i === 3 ? C.tealSoft : C.white, 16, C.line);
    textBox(slide, item[0], x + 18, 244, 236, 40, { fontSize: 23, bold: true, color: C.navy, align: "center" });
    textBox(slide, item[1], x + 20, 298, 232, 80, { fontSize: 18, color: C.muted, align: "center", valign: "top" });
  });
  textBox(slide, "콘텐츠 단위", 72, 464, 180, 34, { fontSize: 22, bold: true, color: C.teal });
  const unitLabels = ["환경 prefab", "등장 인물·사건", "선택·해금 조건", "미니게임 결과", "후속 이야기"];
  unitLabels.forEach((label, i) => {
    rect(slide, 72 + i * 228, 520, 202, 72, i % 2 ? C.white : C.graySoft, 12, C.line);
    textBox(slide, label, 84 + i * 228, 536, 178, 40, { fontSize: 20, bold: true, color: C.navy, align: "center" });
  });
  textBox(slide, "새로운 terrain과 미니게임을 기존 코드를 크게 바꾸지 않고 데이터와 모듈 추가 방식으로 확장합니다.", 72, 624, 1136, 38, { fontSize: 22, bold: true, color: C.navy, align: "center" });
  slide.speakerNotes.textFrame.setText("Excel은 기획 검토용 원본, JSON은 Unity 실행 데이터로 사용합니다. 참조 오류를 먼저 검사해 terrain, 사건과 미니게임을 반복해서 추가할 수 있는 구조를 만들 계획입니다.\n출처: C:/remote-OneWayVer2/outputs/제안서_유영선_존댓말본.docx");
}

// 6. Environment and validation
{
  const slide = baseSlide("개발 환경과 검증 계획", 6);
  textBox(slide, "개발 환경", 72, 136, 180, 34, { fontSize: 22, bold: true, color: C.teal });
  rect(slide, 72, 184, 512, 410, C.white, 16, C.line);
  const env = [
    ["게임", "Unity 6.3 LTS · C# · Visual Studio 2022"],
    ["데이터", "Excel 콘텐츠 맵 · JSON · ScriptableObject · prefab"],
    ["AI", "한국어 LLM 파인튜닝 · 경량화 · 로컬 추론"],
    ["자동화", "Codex · CLI · MCP · 플러그인 · skill"],
    ["협업", "Git · GitHub Desktop"],
  ];
  env.forEach((item, i) => {
    const y = 210 + i * 70;
    textBox(slide, item[0], 96, y, 92, 32, { fontSize: 19, bold: true, color: C.teal });
    textBox(slide, item[1], 194, y, 360, 42, { fontSize: 20, color: C.ink });
    if (i < env.length - 1) rect(slide, 96, y + 52, 440, 1, C.graySoft);
  });
  textBox(slide, "검증 계획", 642, 136, 180, 34, { fontSize: 22, bold: true, color: C.teal });
  const tests = [
    ["내장 모델", "오프라인 로드, 메모리와 추론 시간"],
    ["이야기 품질", "한국어 자연스러움, 사건과 인물의 일관성"],
    ["데이터", "Excel·JSON 대응, 식별자와 경로 참조"],
    ["플레이", "terrain 배치, 저장 복원과 미니게임 결과 반영"],
  ];
  tests.forEach((item, i) => {
    const y = 184 + i * 102;
    rect(slide, 642, y, 566, 84, i === 0 ? C.tealSoft : C.white, 14, C.line);
    textBox(slide, item[0], 664, y + 14, 142, 32, { fontSize: 21, bold: true, color: C.navy });
    textBox(slide, item[1], 808, y + 14, 374, 50, { fontSize: 19, color: C.muted });
  });
  textBox(slide, "생성 결과는 실행과 렌더링 결과로 확인하고, 실패 시 검증된 기본 데이터로 복구합니다.", 642, 616, 566, 42, { fontSize: 20, bold: true, color: C.navy, align: "center" });
  slide.speakerNotes.textFrame.setText("도구를 단순 코드 생성에만 쓰지 않고 프로젝트 탐색, Unity 조작, 문서화와 반복 테스트에 활용합니다. 평가 기준의 기술 활용성과 계획의 실현 가능성을 설명합니다.\n출처: C:/remote-OneWayVer2/outputs/제안서_유영선_존댓말본.docx; C:/Users/유영선/Downloads/프로젝트_평가표양식_2026.pdf");
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
  slide.speakerNotes.textFrame.setText("Word 제안서에서 확정한 유영선 개인 계획만 반영한 표입니다. 기존 구조 분석과 구현을 먼저 진행한 뒤 콘텐츠 확장, LLM 파인튜닝과 통합 테스트로 이어집니다.\n출처: C:/remote-OneWayVer2/outputs/제안서_유영선_존댓말본.docx");
}

const requirements = {
  explicitTotalSlideCount: 7,
  requiredNativeTableOwnerSlides: [7],
  requiredNativeChartOwnerSlides: [],
  workspaceDir,
};
const fontPolicy = { basis: "design", families: [font] };
const expectedSlideSizeEmu = "12192000,6858000";
const stagingDir = path.join(workspaceDir, ".codex-finalizer-yys-ppt");
await fs.mkdir(stagingDir, { recursive: true });
const candidatePath = path.join(stagingDir, "candidate-yys-proposal.pptx");
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
  receiptPath: path.join(stagingDir, "과제제안서_발표자료_유영선_수정본.validation.json"),
});

console.log(FINAL_PPTX);
