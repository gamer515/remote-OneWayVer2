import fs from "node:fs/promises";
import path from "node:path";
import { pathToFileURL } from "node:url";

import { FileBlob, PresentationFile } from "@oai/artifact-tool";


const workspaceDir = "C:/remote-OneWayVer2";
const sourcePath = "C:/Users/유영선/Desktop/C211201_유영선.pptx";
const buildDir = path.join(workspaceDir, "tmp/pptx_yys/build_week15");
const outputDir = path.join(workspaceDir, "outputs");
const finalPath = path.join(outputDir, "C211201_유영선_15주차반영본_v2.pptx");
const skillDir = "C:/Users/유영선/.codex/plugins/cache/openai-primary-runtime/presentations/26.909.12148/skills/presentations";
const runtimePython = "C:/Users/유영선/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe";

await fs.mkdir(buildDir, { recursive: true });
await fs.mkdir(outputDir, { recursive: true });

const presentation = await PresentationFile.importPptx(await FileBlob.load(sourcePath));
const slide7 = presentation.resolve("sl/gnmp4jqx");
const schedule = presentation.resolve("tb/bm98jid4");

// 변경 전 원본 슬라이드와 배치를 보존해 시각 비교에 사용합니다.
const beforePng = await slide7.export({ format: "png", scale: 2 });
await fs.writeFile(path.join(buildDir, "slide-7-before.png"), new Uint8Array(await beforePng.arrayBuffer()));
const beforeLayout = await slide7.export({ format: "layout" });
await fs.writeFile(path.join(buildDir, "slide-7-before.layout.json"), await beforeLayout.text());

if (schedule.getCell(0, 15).value !== "15") {
  throw new Error("The final schedule column is not week 15.");
}
if (schedule.getCell(9, 0).value !== "통합 성능 사용성 테스트") {
  throw new Error("The final schedule row did not match the expected item.");
}

// 마지막 계획 항목을 15주차까지 이어지도록 표시합니다.
schedule.cells.set(9, 15, "P");
const week15Cell = schedule.getCell(9, 15);
week15Cell.fill = "#D9EEF0";
week15Cell.text.bold = true;
week15Cell.text.fontSize = 15;
week15Cell.text.typeface = "Noto Sans KR";
week15Cell.text.color = "#18788A";
week15Cell.text.alignment = "center";
week15Cell.text.verticalAlignment = "middle";

const afterPng = await slide7.export({ format: "png", scale: 2 });
await fs.writeFile(path.join(buildDir, "slide-7-after.png"), new Uint8Array(await afterPng.arrayBuffer()));
const afterLayout = await slide7.export({ format: "layout" });
await fs.writeFile(path.join(buildDir, "slide-7-after.layout.json"), await afterLayout.text());

const candidatePath = path.join(buildDir, "candidate.pptx");
await (await PresentationFile.exportPptx(presentation)).save(candidatePath);

const { finalizePresentation } = await import(pathToFileURL(
  path.join(skillDir, "container_tools/artifact_tool_utils.mjs"),
).href);

const result = await finalizePresentation({
  workspaceDir,
  candidatePath,
  finalPath,
  pythonExecutable: runtimePython,
  integrityValidatorPath: path.join(skillDir, "container_tools/inspect_presentation_package_integrity.py"),
  layoutValidatorPath: path.join(skillDir, "container_tools/inspect_presentation_layout_geometry.py"),
  layoutArgs: [
    "--expected-slide-size-emu", "12192000,6858000",
    "--validate-bullet-geometry",
    "--validate-heading-fit",
    "--require-native-table-slide", "7",
  ],
  explicitTotalSlideCount: 7,
  requiredNativeTableOwnerSlides: [7],
  requiredNativeChartOwnerSlides: [],
  sourceTemplatePath: sourcePath,
  requiredTemplateReferenceSlides: [1, 2, 3, 4, 5, 6, 7],
  minimumTemplateCoverageRatio: 1,
  fontPolicy: {
    basis: "reference",
    families: ["Noto Sans KR"],
    referencePath: sourcePath,
    referenceSha256: "1b6557701bc7a822a57af776ce5bd24ecbe8212f235fa6d3e96f5b9dcd855d96",
  },
  verifyArtifactToolImport: true,
  receiptPath: path.join(buildDir, "validation-v2.json"),
});

const verify = await PresentationFile.importPptx(await FileBlob.load(finalPath));
const finalTable = verify.resolve("tb/bm98jid4");
if (finalTable.getCell(9, 15).value !== "P") {
  throw new Error("Week 15 plan marker was not preserved in the final deck.");
}
const finalWeek15 = finalTable.getCell(9, 15);
if (finalWeek15.fill.color.hex !== "#D9EEF0" || finalWeek15.text.color.hex !== "#18788A") {
  throw new Error("Week 15 plan marker style does not match the adjacent plan cells.");
}

const snapshot = await verify.inspect({
  target: { id: "tb/bm98jid4", beforeLines: 0, afterLines: 0 },
  kind: "table",
  maxChars: 4000,
});
await fs.writeFile(path.join(buildDir, "final-table-inspect.ndjson"), snapshot.ndjson, "utf8");

console.log(JSON.stringify({ finalPath, result }, null, 2));
