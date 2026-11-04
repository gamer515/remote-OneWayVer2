# Knight desk dismount study

Unity에 가져올 파일: **Knight_DeskDismount.fbx**. Blender 편집 원본은
`Knight_DeskDismount.blend`이며, 영상과 PNG는 확인용이다.
Unity 연결 현황(2026-11-04): DecisionScene의 `Knight_For_Ani`에 별도 Humanoid
Avatar와 `KnightPresentation.controller`를 연결했다. Mixamo 앉기·걷기·Warrior Idle과
이 내려오기 클립을 같은 기사에서 재생한다. 실제 실행·검증과 Inspector 조정 위치는
[개발 보고서](../../../Reports/development-report.html#knight-animation-integration)를 참조한다.
아래 내용은 독립 애니메이션 원본 제작 당시의 기준이다.

정리 마무리(2026-11-05): 이 폴더의 제작용 FBX와 Unity의
`Assets/Decision_YYS/Animation/MaximoAni/Knight_DeskDismount.fbx`는 현재 바이트가 다르다.
Unity 적용본·가져오기 메타에는 후속 리깅 설정이 있으므로 정리 시 덮어쓰지 않았다.
원본을 재생성해도 Unity 적용본은 자동 갱신되지 않는다. 적용본 교체 시 Avatar와
실제 재생을 다시 검증해야 한다. 임시 코드·로그·번호 백업의 정리 대기 항목은 없다.

Open `Knight_DeskDismount.blend` in Blender and press Space to preview.

- 30 FPS, frames 1–50 (about 1.67 seconds), one non-looping action.
- Twelve reference poses with continuous-velocity monotone cubic interpolation;
  the actual skeleton is baked on every frame. Quaternion signs stay continuous.
- Frames 1–8: seated; 15: weight shift; 22: leaving desk; 27: first contact;
  31: landing compression; 45–50: standing.
- Legs have fixed bone lengths and unit scale. Feet follow the actual ankle joint
  instead of being translated to unreachable targets, which stretched the boots
  in the first version. FBX roundtrip verifies all 50 frames for leg translation
  and scale; see `validation.json`.
- `motion_preview.png`: twelve rendered samples, read left to right, top to bottom.
- `Knight_DeskDismount_preview.mp4`: full 30 FPS motion, using viewport shading.
- Character is approximately 1.8 m tall. Preview tabletop is 0.746 m high.
- The reference desk is included in the Blender preview only.
- `Knight_DeskDismount.fbx` contains the character, existing HumanArmature rig,
  materials, and baked animation. It does not contain the preview desk or lights.
- Based on `KnightCharacter_RigFixed_ColorFixed.blend`, because the supplied
  `KnightCharacter_Standing_ColorFixed.fbx` contains a mesh without a skeleton.
- Translation is baked into the Body bone. The character travels about 0.45 m
  forward. Do not add that travel a second time through a movement script.
- This folder is the isolated authoring source. Subsequent Unity integration is
  documented above; rebuilding this folder does not update scenes/controllers.
  The existing Mixamo Avatar uses a different skeleton; do not assign that Avatar
  directly to this FBX. Unity Humanoid retargeting requires a separately configured
  Avatar, or use this model's own Generic rig.
- Hand contact, starting height and placement depend on the game desk.
  The later integration report records its own Unity playback verification;
  this cleanup does not rerun it.

Rebuild with Blender:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --python AssetBuild/KnightRigFix/create_desk_dismount.py
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --python AssetBuild/KnightRigFix/validate_desk_dismount.py
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --python AssetBuild/KnightRigFix/render_dismount_video.py
```

Run from the project root. To regenerate only the Blend/FBX without rendering
the twelve stills, append `-- --skip-stills` to the first command.
The build overwrites the generated Blend/FBX in this folder; save manual edits
under another name before rebuilding. Change the twelve `keys` entries and
`tangent`/`limb` in `create_desk_dismount.py` to adjust timing, motion and reach.
Keep the scene frame range/FPS and validation expectations synchronized.

Version control keeps the source Blend, generated delivery Blend/FBX, this guide
and all three reusable tools. Individual PNGs, contact sheet, MP4, validation JSON,
logs and Blender numbered backups are reproducible local outputs, excluded from
Git. There is no disposable script awaiting user satisfaction; all three Python
files are maintained rebuild/verification tools. See
[`Reports/development-report.html`](../../../Reports/development-report.html)
for the cumulative work status, checks and cleanup record.
