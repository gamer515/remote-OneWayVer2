현재 전투 캐릭터 자산 정리 (2026-11-23)

B1: Assets/3DBattle/Boss1/Runtime
B2: Assets/3DBattle/Boss2/Runtime
플레이어: Assets/3DBattle/NewPlayer/Runtime

Model: 현재 몸체 메시, 필요한 모델 원본, 장비, 재질과 텍스처
Animations: 현재 연결된 모션
Animator: 실제 사용하는 Animator Controller
Data: 공격 순서/판정/타이밍 등의 연결 데이터

1차 정리에서는 모델과 모션 내용은 바꾸지 않고 현재 사용하는 90개 파일과 .meta를 함께 이동했습니다.
2차 정리에서는 MainMenuScene, DecisionScene, 3DBattleScene, 3DBattleDebugScene의 참조 및 열린 씬의 미저장 참조를 검사한 뒤, 미사용 모델·애니메이션과 관련 프리팹/Controller 등 148개(약 1.14 GiB)를 .meta와 함께 휴지통으로 옮겼습니다. 씬 파일은 저장하거나 변경하지 않았습니다.
다른 설정과 샘플 씬이 공유하는 7개 파일은 연결을 보존하기 위해 남겼습니다. 현재 Runtime 자산 90개와 네 씬의 참조, 열린 씬의 편집 상태 보존을 검증했습니다.
B1 Animations/SourceModels의 FBX는 현재 모션을 직접 참조하므로 유지해야 합니다. B1 Model의 FBX에는 대기 모션도 함께 들어 있습니다.
플레이어 Model의 AttackBody_BothFists.asset는 현재 몸체 메시입니다. 뼈대와 장비 배치, 헬멧/어깨 수정은 기존 씬 오브젝트에 그대로 있습니다. 이 메시 파일 자체가 완성형 캐릭터 프리팹은 아닙니다.

CurrentCharacterAssets.csv: 이동한 90개 파일의 현재/이전 위치와 GUID
OtherAnimationModelReferences.csv: 삭제 후 남은 모션/모델의 씬·프리팹·Controller/설정 참조 여부와 보존 이유. Runtime 밖이라는 이유만으로 미사용 파일은 아닙니다.
3DBattleDebug에서 사용하는 사용자 편집본과 미리보기는 유지했고, 참조되지 않는 과거 백업은 정리했습니다. Decision의 BossRevealPreparedChunk 프리팹도 B1 자산을 계속 공유합니다.
Authoring~/UnusedBattleAssetCleanup.txt: 삭제 목록, 공유 참조로 보존한 7개 파일, 복원 안내
Authoring~/UnusedBattleAssetDeletionPlan.json: 삭제 파일의 GUID, 내용 해시, 과거 제작 도구의 참조 기록
Authoring~/UnusedBattleAssetDeletionVerification.json: 삭제 직후 씬 참조·보존 파일·편집 상태 검증 결과 (목록/안내문 갱신 전)
과거 제작 도구를 다시 실행하려면 해당 도구가 요구하는 삭제 원본과 .meta를 휴지통에서 함께 복원해야 할 수 있습니다.
Authoring~/CharacterAssetMovePlan.json: 상세 이동표와 자산별 참조 목록. Editor/BattleCharacterAssetPaths.cs는 과거 제작 도구의 조합형 경로를 GUID로 해석합니다.
Authoring~/CharacterAssetOrganizationVerification.json: 자산 내용·하위 객체 ID·씬 및 범위 밖 파일 검증 결과

플레이어가 참조하지만 이번 범위 밖이라 그대로 둔 원본:
Assets/UnityAssets/KnightAsset/Helmet3.fbx
Assets/UnityAssets/KnightAsset/Sword.fbx
Assets/UnityAssets/KnightAsset/ShoulderPads.fbx
Assets/UnityAssets/KnightAsset/KnightCharacter.fbx
