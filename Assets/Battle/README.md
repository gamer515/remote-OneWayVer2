# Battle stage authoring

Open `Assets/Scenes/BattleScene.unity`. The existing B1/B2/B3 buttons restart the selected stage, including after death. Their persistent callbacks are preserved and their canvas stays above transitions.

## Controls and defaults

- B1: Space/Z advances the opening dialogue. Move the mouse horizontally to steer; each left click advances the heart. Holding the button does not repeat. Forward limit is viewport Y=1/3 (two thirds from the top), about world Y=-3.33 with this camera, minus the heart extent. Rear contact freezes the survival timer, debris, HP invulnerability and physics for a five-second warning. A short backward-motion grace period follows; move away from the boundary to re-arm its warning. Survive 60 active seconds, then exit automatically.
- B2: arrow keys/WASD dodge; W/up jumps during gravity cases. Four enemies surround the player and the walls grow top, left, bottom, right. Case4 is followed by 2-a, then case5. Drag and release the left mouse button to slash the straight segment between endpoints. Escape cancels a drag. Five actual 3D sphere meshes approach from positive Z. Releasing cuts intersected meshes into capped fragments. Each uncut target crossing Z=0 deals one damage even during bullet invulnerability; cut fragments deal none. Arrival wins if arrival and release occur in the same frame.
- B3: white shards crack and fall, then arrow keys/WASD dodge radial, rain and aimed patterns. Invisible screen bounds include the whole heart.
- Completed stages return to DecisionScene by default. Disable `returnToDecision` on a stage asset to keep its completion screen during authoring.

## Edit the sequence

Select `Data/B1.asset`, `B2.asset` or `B3.asset`. Edit `flow.steps`: reorder, duplicate or add entries. Step labels are for authors. Each stage owns its own embedded flow and pattern settings; assets contain no runtime state.

Step kinds: Dialogue, Corridor, Mist, EnemyEntrance, BuildBox, Pattern, Slash, ScreenBreak, Exit. A Pattern step selects LegacyCase (1–9), Debris, Radial, Rain or Aimed. Duration/interval/speed/count affect the new configurable patterns. LegacyCase uses the original case implementation and timings; its generic duration/interval fields are not overrides. Dialogue duration is a minimum reading delay before Space/Z, rather than an automatic advance.

To insert another 2-a, add a Slash entry between completed Pattern entries. To change its count, approach speed or timing, edit the stage's slash settings. `slashStartZ` and speed must be positive. Targets use the supplied unlit material with shadow casting and receiving disabled. The shader has no lighting or AO input and no shadow pass. This does not alter the shared project rendering pipeline.

To add a fundamentally new attack rule, implement `IBattlePattern`, add its selector/settings to `BattlePatternData`, and connect it in `PatternSequenceStep`. For a reusable new presentation step, implement `IBattleStep` and add it to `BattleSteps.Create`. Stage flows prepare their environment; the sequence data, not the flow classes, owns ordering.

## Lifetime and pause rules

Every step receives a `BattleStepScope`. Parent transient objects to `scope.Root`, and register non-object cleanup with `OnDispose`. Put persistent stage presentation under `context.StageRoot`. Use `context.Clock.Delta` and `Clock.Wait` for gameplay time; use realtime only for UI/transition time. New movement and spawn logic must honor the clock. B1 recovery suspends its pattern coroutine and disables player physics while the realtime dialogue runs. Existing legacy cases are only entered/exited at pattern boundaries and do not support arbitrary mid-case pause.

The legacy attack manager's spawn helper assigns all old case bullets to the current scope. Normal completion, death and stage switching cancel its child coroutines and destroy its scoped objects. No global `Time.timeScale` changes are used.

`BattleArena` is independent of wall visibility. B3 uses camera bounds; legacy cases use wall bounds; B1 uses a screen-relative corridor. Programmatic entry/exit movement locks input and may bypass these bounds intentionally.

## Assets and validation

The scene directly references the existing `BattleScenePlayerHeart.png` sliced sprite and `mist.png` sprite. Original image/import files are retained. The runtime creates presentation objects under `BattleManager/Battle Runtime`; editable sequence assets and persistent reference wiring are saved in the scene.

Use **One-Way > Validate Battle Configuration** in Unity to check scene references, the three button callbacks, sequence ordering, timing settings, Z-plane crossing, finite slash segments, mesh clipping and screen bounds. It loads a preview scene without saving your open scene. It also runs once when these scripts are first imported. The report is written under the OS temp directory in `OneWayBattleValidation/UnityValidation.txt`.

Play verification: B1 rear warning must preserve debris locations and time; switching during that warning must work. B2 must clear previous bullets before 2-a and restore the box/camera before case5. Cut and missed targets must be handled once each. B3 must keep the full heart inside every edge, including after changing the Game view aspect ratio. Repeat each stage button during transitions and after death.
