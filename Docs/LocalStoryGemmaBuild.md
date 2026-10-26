# Gemma Windows 빌드

2026-10-26. 기본 모델은 Gemma 3 4B IT QAT Q4_0, 현재 지원 대상은 Windows x64 Mono다. Editor 전용 모델이 아니며 실행 파일과 함께 데이터 폴더로 배포된다.

## 설치와 모델 경로

- Editor: Tests/LocalStoryBenchmark/Models/gemma-3-4b-it-q4_0.gguf.
- Windows Player: OneWay_Data/StreamingAssets/LocalStory/gemma-3-4b-it-q4_0.gguf.
- 배포 모델 명세: BuildSupport/LocalStory/Gemma/ModelManifest.json. StreamingAssets 명세도 Gemma다.
- 비교용 Qwen 모델은 같은 Tests 모델 폴더, 비교 명세는 BuildSupport/LocalStory/Qwen에 따로 보관한다. 배포본에는 포함하지 않는다.
- 설치 스크립트의 -Part Model 기본값은 Gemma다. 비교 모델을 설치할 때만 -Model Qwen을 명시한다. 설치 다운로드와 실행 중 추론은 별개며 추론에는 외부 서버가 필요하지 않다.

## 빌드 검증

LocalStoryBuild는 일반 Unity Build와 연결된 CLI 빌드에 적용된다. 파일 크기 3,155,051,328바이트와 SHA256, GPU/CPU DLL, 모델 고지·약관·정책과 NPC의 Gemma 설정을 확인하고 배포본 모델을 다시 검사한다. 모델이 없으면 성공 빌드로 처리하지 않는다. 알 수 없는 추가 모델 파일은 자동 삭제하지 않고 실패시킨다.

```text
OneWay.exe
OneWay_Data/StreamingAssets/LocalStory/
  gemma-3-4b-it-q4_0.gguf
  ModelManifest.json
  Chapters/
    Chapter_00.xlsx
    Chapter_01.xlsx
    Chapter_02.xlsx
  runtimes/win-x64/native/
    llamalib_win-x64_tinyblas.dll
    llamalib_win-x64_avx2.dll
  Gemma/
    Notice.txt
    GemmaTerms.html
    GemmaProhibitedUsePolicy.html
  LICENSE
  third_party_licenses/llama.cpp.LICENSE.txt
```

현재 실행 파일: Builds/GemmaWindows/OneWay.exe. 2026-10-26 `build_f4767390f21e` 성공, 약 56.9초, 오류 0개·경고 2개. 테스트 창과 자동 생성 모두 공통 단락 생성기를 사용한다. 문구 검사 실패도 ReviewRequired로 다음 회차에 반영하며 기존 Failed 결과를 다시 읽는 기능을 포함했다. Gemma 단독 포함과 새 기능이 들어간 게임 DLL을 확인했다. 경고는 Player 자동화 설정 없음과 기존 미사용 colour 변수다. 실제 확인 절차와 실패본 상태는 LocalStoryTestWorkflow.md에 적었다.

개발 빌드에 -localStorySmoke -batchmode -nographics 인수를 주면 LocalStorySmoke/Saves를 별도 사용한다. 실제 사용자 저장소를 사용하지 않고 메뉴 파일 검사, 검증 캐시, 모델 세션 재사용, 표식 이야기 2구간 생성을 검사한다. 결과는 LocalStorySmoke/Result.json이다. 일반 실행에는 이 인수를 주지 않는다.

최종 구조 수정 빌드: 오류 0, 경고 2. Player 검사: passed=true, qwenBundled=false, storyValid=true, sectionsGenerated=2, authoredCards=0, 약 43.07초. 문장 품질 승인은 별도다. 새 숨은 대화의 Editor Gemma 검사에서는 내용 검사 실패도 확인했으며, 실패 결과는 적용하지 않고 해당 원문 분기를 사용한다.

사용자 저장은 기존 Application.persistentDataPath/Saves를 사용한다. 실행 파일 위치를 바꾼다고 사용자의 Saves 경로를 바꾸지는 않는다. 데이터 구조와 실제 플레이 확인은 LocalStoryTestWorkflow.md를 참조한다.
