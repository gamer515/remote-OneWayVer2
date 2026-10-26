# 이야기 생성 요청과 저장 결과

같은 sourceRun + storyPath는 대화 종료 시 한 번 요청한다. 이후 선택·회차 완료·다음 회차 진입·프롬프트 설정 변경으로 같은 요청을 개정하거나 새 결과를 만들지 않는다. revision은 기존 저장 호환과 작업 식별을 위해 남고, 새 요청은 1로 고정하며 로그에 개정 번호를 표시하지 않는다.

완료된 결과는 정책 해시가 달라져도 유지한다. 실제 원문/행동 연결이 달라져 적용할 수 없으면 원문으로 진행하며 자동으로 모델을 다시 실행하지 않는다. 취소된 미완료 작업은 저장된 구간 체크포인트 이후부터 이어갈 수 있다. 새 회차에서 실제 새 대화를 마치면 그 이후 회차용 요청은 별도로 생성한다.

## 실패 결과 확인

PromptPolicy.runtime.allowFailedOutput=true로 다음 회차에서 실패 문구도 확인한다. 상태는 ReviewRequired이며 valid/correctionValid를 성공으로 바꾸지 않는다. 생성된 글만 사용하고 화자·선택 ID·분기·행동은 원문을 복제한다. 중단된 보고서의 incompleteSections에서도 구조가 읽히는 카드만 복원한다. 문장이 전혀 없거나 형식이 깨진 부분은 원문을 유지하며 실패 원출력은 보고서에 남는다.

이미 저장된 완료/실패 보고서를 가진 옛 Pending 개정은 모델 호출 없이 결과를 재사용한다. 모델 출력이 전혀 없는 취소 또는 미완료 체크포인트는 이어하기 대상이다.

## 파일별 역할과 중복 제거

| 저장 파일 | 보관하는 내용 |
|---|---|
| Runs/.../Progress.json | 실제 플레이 사건과 읽은 대사 이력의 원본 |
| GeneratedContent/.../Queue.json | 요청·고정 입력·진행 상태·결과 경로. 전체 플레이 이력 사본은 넣지 않음 |
| GeneratedContent/.../Story.json | 게임에 적용하는 문구 변경분과 원문 연결 검증 정보 |
| GeneratedContent/.../Features.json | NPC별 요약. impressions를 문장으로 재복사하는 speechChange 필드는 제거 |
| LocalStoryTests/Results/...Result.json | 원출력·원문 연결·오류·실제 입력과 시간 기록 |
| LocalStoryTests/Edits/...json | 사람이 편집할 수 있는 대사 사본. 결과를 덮어쓰지 않는 별도 작업물 |
| Records/...xlsx | NPC·해금·아이템 설정. 결과 대사나 생성 상태를 중복 기록하지 않음 |

결과 JSON schemaVersion=4는 rawOutput을 한 번 저장한다. draft/correction 표시 배열은 읽을 때 계산한다. 합본 교정이 canonical JSON이면 초안에서 달라진 카드만 correctionPatch에 저장하고 읽을 때 correctionRawOutput을 정확히 복원한다. 깨진 JSON이나 비정규 원출력은 손실 없이 원문 문자열로 남긴다. saved/runtimeStage 등의 적용 정보도 application 객체와 최상위에 이중 저장하지 않는다.

기존 v3 결과는 원본 바이트를 Legacy.zip에 검증 백업한 뒤 v4로 변환한다. 기존 Edit 파일은 수정하지 않는다. 서로 다른 두 번의 실제 생성 시도는 별도 이력으로 유지하며 같은 문장처럼 보인다는 이유로 삭제하지 않는다. previousChanges는 사람이 적용한 내용을 되돌리는 용도이므로 보존한다.

검증: AgentScripts/StorageLifecycleCheck.cs와 기존 대화 큐·구간·실패 재생·저장 이전 검사. 실제 저장소 정리는 전체 백업과 Profile/Runs/Edits/Records 해시 확인 후 진행한다.
