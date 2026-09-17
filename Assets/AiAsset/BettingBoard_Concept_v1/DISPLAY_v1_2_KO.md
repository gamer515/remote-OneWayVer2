# 숫자 표시기 v1.2

기존 Display_024의 입체 숫자를 유지하면서 000~999를 표시합니다. TextMeshPro/폰트/추가 플러그인은 필요하지 않습니다.

## 적용

1. BettingBoard_v1_2_DynamicDisplay.unitypackage를 Import합니다. 기존 에셋 경로와 프리팹 GUID를 유지하는 업데이트이므로 패키지의 변경 항목을 모두 가져오세요.
2. 기존 프리팹 인스턴스의 Display_024를 선택합니다.
3. Betting Board Number Display 컴포넌트의 Value를 변경합니다. 예: 7 → 007, 24 → 024, 108 → 108. 편집 중에도 표시가 갱신됩니다.

기존 베팅판이 프리팹과 연결되어 있으면 업데이트가 반영됩니다. Unpack한 오브젝트라면 패키지의 Prefabs/BettingBoard_Concept_v1을 새로 배치해 사용하세요. 기존 컴포넌트 참조 슬롯은 그대로 유지하도록 원래 오브젝트 ID를 보존했습니다. 사용자가 만든 프리팹 오버라이드에 따라 확인이 필요할 수 있습니다.

## 게임 수치와 연결

기존 게임 스크립트에서 참조할 필드를 추가한 뒤 Display_024를 Inspector 슬롯에 연결합니다.

```csharp
using BettingBoardAssets;
using UnityEngine;

// 아래 필드와 메서드를 기존 MonoBehaviour 클래스 안에 넣습니다.
[SerializeField] private BettingBoardNumberDisplay countDisplay;

private void RefreshCount(int currentCount)
{
    countDisplay.SetValue(currentCount);
}
```

게임 시작 시 현재 수치를 한 번 전달하고, 그 수치가 바뀌는 지점에서도 RefreshCount(현재수치)를 호출하세요. 오브젝트 참조만으로 게임의 임의 변수를 자동으로 감지하지는 않습니다.

- `countDisplay.SetValue(125);` 또는 `countDisplay.Value = 125;` → 125
- UnityEvent<int>에서는 Display_024 → BettingBoardNumberDisplay → SetValue를 Dynamic int로 연결할 수 있습니다.
- 일반 Button.onClick에서는 SetValue에 지정한 고정 정수를 전달할 수 있습니다. 실시간 게임 수치는 위 코드 또는 UnityEvent<int>를 사용하세요.
- 음수는 000, 999 초과는 999로 제한됩니다. 4자리 이상/음수/소수는 이 버전의 표시 범위에 포함되지 않습니다.
- Play Mode에서 바꾼 값은 플레이를 종료하면 보통 이전 편집 값으로 돌아갑니다.

## 구조와 변경 범위

Display_024 아래의 원래 Ivory 숫자는 비활성화하고 보존했습니다. 세 자리 슬롯이 미리 만든 0~9 숫자 메시를 공유합니다. 값이 바뀔 때 필요한 메시 참조만 교체하며, 매 프레임 새 메시/머티리얼/문자열을 만들지 않습니다. 기존 재질, 보드, 버튼, 레버 및 콜라이더를 유지합니다.

이 패키지에는 숫자 표시 기능만 연결되어 있습니다. 잔여 코인·베팅 금액 중 어떤 수치를 표시할지는 게임 코드에서 전달하는 값으로 결정합니다.

제작 및 검증은 별도 프로젝트에서 수행하며, 사용자의 게임 프로젝트나 씬을 자동으로 수정하지 않습니다. 이 문서는 초기 README의 고정 숫자 및 미검증 설명보다 우선합니다.
