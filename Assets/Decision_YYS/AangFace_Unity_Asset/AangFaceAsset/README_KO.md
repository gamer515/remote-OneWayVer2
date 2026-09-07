# Aang Face v2 — Unity용 3D 얼굴과 표정

첨부한 얼굴 시트를 참고하여 머리와 짧은 목의 3D 형태를 다시 구성한 수정본입니다. 턱선·두상·귀를 다듬고, 구형 눈알을 덮는 눈꺼풀, 입술과 구강 안쪽, 개별 치아, 홍채와 피부의 정점 색을 추가했습니다. 10개 블렌드셰이프로 표정을 조절합니다.

**`ActualModelPreview.png`는 이 패키지의 실제 메시를 렌더링한 이미지입니다.** 참고 이미지와 얼굴 비율·조형·재질의 완성도에는 여전히 차이가 있습니다. 이 파일은 수작업으로 구성한 형태 근사 모델이며, 참고 이미지를 정밀하게 복원한 최종 제작용 모델은 아닙니다.

## Unity에서 사용하기

1. ZIP을 풀고 Unity 프로젝트를 엽니다.
2. **Assets > Import Package > Custom Package…**에서 `AangFace.unitypackage`를 선택하고 전체 파일을 Import합니다.
3. 스크립트 컴파일이 끝나면 **Tools > Aang Face > Create Prefab**을 실행합니다.
4. 생성된 `Assets/AangFaceGenerated/AangFace.prefab`을 Hierarchy에 드래그합니다. 해당 폴더가 이미 있으면 이름에 번호가 붙은 새 폴더를 만듭니다.
5. 최상위 `AangFace`의 **Face Expression Controller**에서 슬라이더를 0–100으로 조절합니다. Neutral / Smile / Angry / Sad / Surprised / Blink 버튼도 있습니다.
6. Scene 뷰에서 오브젝트를 선택하고 `F`를 눌러 가까이 봅니다. 얼굴은 로컬 +Z 방향을 향하며, 머리와 목 전체 높이는 약 31cm입니다.

패키지 대신 `Assets/AangFace` 폴더를 프로젝트의 Assets 폴더에 복사하고 3번부터 진행해도 됩니다. 두 방법 중 하나를 사용하세요. 생성 메뉴가 보이지 않으면 Console의 스크립트 오류를 확인하세요.

**이전 버전을 사용했다면:** v2 패키지는 기존 소스 파일과 같은 경로·GUID로 갱신됩니다. 임포트 후 **Create Prefab을 다시 실행하고 새 프리팹을 사용하세요.** 이미 생성해 둔 이전 버전 프리팹의 메시가 자동으로 교체되지는 않습니다.

**Unity 에디터 임포트, C# 컴파일, 셰이더 컴파일 및 플레이 모드 실행은 이 작업 환경에서 검증하지 못했습니다.** 모델 배열, 10개 표정 데이터, GLB와 패키지 구조, 실제 메시 렌더를 확인했습니다. Unity 6의 문서화된 메시·블렌드셰이프 API를 사용한 생성 코드가 포함되어 있습니다.

## 표정 조절

| 이름 | 동작 |
| --- | --- |
| `Smile` | 입꼬리와 볼을 올리고 치아가 보이는 웃음 |
| `Angry` | 눈썹 안쪽을 내리고 위 눈꺼풀을 좁힘 |
| `Sad` | 눈썹 안쪽을 올리고 입꼬리를 내림 |
| `Surprised` | 눈과 입을 열고 눈썹을 올림 |
| `BlinkLeft` | 캐릭터 자신의 왼쪽 눈 감기 |
| `BlinkRight` | 캐릭터 자신의 오른쪽 눈 감기 |
| `JawOpen` | 입 벌리기와 아래 얼굴의 이동 |
| `MouthWide` | 입을 옆으로 넓힘 |
| `MouthPucker` | 입을 오므리고 앞으로 내밂 |
| `BrowRaise` | 양쪽 눈썹 올리기 |

모두 0이면 기본 표정입니다. 웃음·화남·슬픔·놀람은 한 종류씩 사용하고, 눈 감기나 입 조절은 낮은 값부터 섞어 보세요. 극단적인 표정을 여러 개 동시에 100으로 적용하는 조합은 보정하지 않았습니다. 양쪽 눈 감기의 0%, 50%, 100% 상태는 실제 메시를 렌더링해 확인했습니다.

눈 감기에는 눈꺼풀 이동과 함께 눈알의 깊이를 줄이는 보정이 들어 있습니다. 이는 표면 겹침을 줄이기 위한 단순화된 변형입니다. 시선 회전 본과 모든 표정 조합을 위한 보정 리그는 없습니다.

Unity 블렌드셰이프 값은 `Mesh.AddBlendShapeFrame`으로 정의한 100 프레임을 기준으로 적용합니다. [Unity 블렌드셰이프 문서](https://docs.unity3d.com/6000.5/Documentation/Manual/BlendShapes.html), [AddBlendShapeFrame](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.AddBlendShapeFrame.html), [SetBlendShapeWeight](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SkinnedMeshRenderer.SetBlendShapeWeight.html).

## 코드와 애니메이션

```csharp
using AangFaceAsset;
using UnityEngine;

public sealed class DialogueFaceExample : MonoBehaviour
{
    [SerializeField] private FaceExpressionController face;

    public void ShowHappy()
    {
        if (!face) return;
        face.ResetExpression();
        face.SetExpression(FaceExpressionController.Expression.Smile, 75f);
    }

    public void SetBlink(float amount01)
    {
        if (!face) return;
        face.blinkLeft = face.blinkRight = Mathf.Clamp01(amount01) * 100f;
        face.Apply();
    }

    public void SetMouthOpen(float amount01)
    {
        if (!face) return;
        face.jawOpen = Mathf.Clamp01(amount01) * 70f;
        face.Apply();
    }
}
```

`SetExpression()`은 네 가지 감정 값만 바꿉니다. 눈·입 값까지 초기화하려면 `ResetExpression()`을 먼저 호출하세요. 입 조절 예제는 외부에서 받은 수치로 입을 여닫는 동작입니다. 자동 립싱크나 자동 눈 깜빡임은 포함하지 않습니다.

Animation 창에서는 `FaceExpressionController`의 float 필드에 키프레임을 기록할 수 있습니다. `Face` 자식의 **Skinned Mesh Renderer > BlendShapes** 값을 직접 애니메이션으로 제어할 때는 컨트롤러 컴포넌트를 비활성화하세요. 활성 상태에서는 매 프레임 컨트롤러의 슬라이더 값이 적용됩니다.

## 재질과 조명

피부의 볼·코 색, 입술의 부드러운 색 경계, 홍채 무늬는 메시의 정점 색으로 들어 있습니다. 색을 표시하려면 정점 색을 읽는 셰이더가 필요합니다. [Unity Mesh.colors 문서](https://docs.unity3d.com/6000.5/Documentation/ScriptReference/Mesh-colors.html).

| 렌더 파이프라인 | 생성 메뉴가 적용하는 재질 |
| --- | --- |
| Built-in | 동봉한 Standard 기반 정점 색 셰이더 |
| URP | 동봉한 URP 조명 기반 정점 색 셰이더 |
| HDRP | 기본 HDRP/Lit의 단색 재질로 대체; 피부·홍채의 정점 색 디테일은 표시되지 않음 |

Built-in과 URP용 셰이더 원문은 `Editor/ShaderSources`에 들어 있습니다. Create Prefab 실행 시 현재 파이프라인에 맞는 파일만 `.shader`로 생성하고 컴파일 오류 여부를 확인합니다. HDRP에서 정점 색 디테일을 보려면 별도의 정점 색 Shader Graph 구성이 필요합니다. 사용자 정의 SRP는 지원 범위에 포함하지 않습니다.

실제 모습은 프로젝트의 조명·노출·색 공간에 따라 달라집니다. `ActualModelPreview.png`는 실제 메시를 별도 렌더러의 스튜디오 조명으로 표시한 결과이며, Unity 화면을 캡처한 이미지는 아닙니다. `Preview.html`은 빠른 회전과 표정 확인을 위해 그림자를 간소화합니다. 눈의 흰 반사점 일부는 고정된 메시 디테일입니다.

## 파일 구성

| 파일 | 용도 |
| --- | --- |
| `AangFace.unitypackage` | Unity 생성 메뉴·표정 컨트롤러·메시 데이터·셰이더 원문 |
| `AangFace.glb` | 실제 3D 메시, 정점 색, 재질, 10개 morph target |
| `ActualModelPreview.png` | 기본·측면·웃음·화남·놀람·눈 감기의 실제 메시 렌더 |
| `Preview.html` | 브라우저에서 회전·확대·표정 조절; 외부 통신 없음 |
| `Assets/AangFace/` | Unity 패키지와 같은 원본 파일 |
| `Source/build_head_v2.py` | 모델과 GLB를 다시 생성하는 파이썬 소스 |
| `Source/head_geometry.npz` | 정점·삼각형·색·노멀·표정 변형 배열 |
| `Source/render_v2.py`, `Source/ray_render.cpp` | 실제 메시 미리보기 렌더 소스 |
| `Source/viewer_template.html` | 오프라인 뷰어 템플릿 |
| `Source/validation.json` | 모델 수치와 검증 범위 |

GLB를 다른 3D 편집 도구에서 열어 추가 조형할 수 있습니다. Unity에는 동봉한 `.unitypackage`를 사용하면 별도 glTF 플러그인 없이 네이티브 Mesh와 Prefab을 생성합니다. GLB에는 얼굴 표정 morph target이 들어 있으며, 별도 골격은 없습니다. Unity 생성 메뉴가 전체 머리용 `HeadRoot` 본 하나를 만듭니다.

## 모델 수치와 남은 작업

- 정점 **67,765개**, 삼각형 **130,151개**, 재질 **11개**, 표정 **10개**입니다. 모바일용 LOD·드로콜 최적화는 하지 않았습니다.
- 머리와 짧은 목만 있습니다. 몸체, 얼굴 뼈 리깅, ARKit 52종, 음소별 입모양은 없습니다.
- 전체 머리는 `HeadRoot`로 회전할 수 있습니다. 몸의 목 본 아래에 프리팹을 붙이고 위치·회전·크기를 조절해 연결할 수 있습니다.
- 얼굴에 눈과 입의 개구부를 만들었지만, 귀·안구·눈썹·치아 등은 일부 겹치는 별도 표면입니다. 통합된 제작용 토폴로지와 구강 전체를 제공하는 모델은 아닙니다.
- UV 텍스처 아틀라스, 이미지 텍스처, 노멀맵, LOD는 없습니다. 정점 색과 재질로 색을 표현합니다.
- 참고 이미지와 같은 완성도에는 추가 조형, 특히 눈 주변·코·입술의 세부 비율과 재질 개선이 필요합니다.

## 소스 다시 실행하기

모델 생성에는 Python 3, NumPy, SciPy가 필요합니다. `Source/build_head_v2.py`를 실행하면 상위 `AangFaceAsset` 폴더의 JSON·GLB·NPZ와 기초 검증 정보를 다시 씁니다. 이것만으로 Unity 패키지와 기존 PNG 미리보기가 자동으로 갱신되지는 않습니다.

실제 메시 렌더에는 Pillow와 C++17/OpenMP 컴파일러도 필요합니다. `Source` 폴더에서 다음과 같이 실행합니다.

```bash
g++ -std=c++17 -O3 -fopenmp ray_render.cpp -o ray_render
python render_v2.py
```

오프라인 뷰어는 템플릿의 `__MESH_DATA__`를 `Assets/AangFace/Data/AangFaceMesh.json` 내용으로 바꾸어 생성할 수 있습니다. 동봉한 `Preview.html`에는 이미 v2 데이터가 들어 있습니다. 자바스크립트 문법은 검사했으나 브라우저 실행은 이 환경에서 확인하지 못했습니다.
