# v1.1 수정 내용

Paper 및 지도 스케치 스프라이트의 빈 내부 이름을 파일명과 일치하도록 수정했습니다. 기존 GUID를 유지하므로 기존 프리팹 참조는 유지됩니다.

기존 패키지 위에 수정본을 Import하세요. Import 창에서 수정된 Sprites 에셋을 선택하세요. 기존 콘솔 메시지는 Clear 후 다시 확인하세요.

Input Manager deprecation 안내는 프로젝트의 Active Input Handling 설정과 관련됩니다. 이 패키지는 ProjectSettings를 포함하거나 변경하지 않습니다. 기존 게임의 UnityEngine.Input 사용을 마이그레이션하기 전에는 Both 설정을 유지하세요. 데모 장면도 StandaloneInputModule을 사용하므로 새 입력만 사용하려면 InputSystemUIInputModule로 전환해야 합니다.
