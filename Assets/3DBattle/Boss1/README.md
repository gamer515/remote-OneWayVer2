# Boss1

- Boss1_AllAnimations.prefab: one skinned character with all 15 individual FBX clips.
- Boss1Surface.mat: shared base-color and normal-map material, assigned to all 15 FBX models.
- Boss1Animations.controller: one state per clip; default Axe_Breathe_and_Look_Around.
- 3DBattleScene: Boss1_AllAnimations is placed at (8, 0, 4).

Press Play and select Boss1_AllAnimations. MeshyAnimationDebugger plays all clips in numbered source-folder order. Use its Inspector controls to pause, restart, change clips, scrub time, or change speed.

For gameplay Animator control, disable MeshyAnimationDebugger first (it disables the Animator while debugging). Then call Animator.Play with the state name, for example Animator.Play("Basic_Jump"). Boss AI, damage and battle spawning are not connected to this preview character.

Validation: all 15 clips move on the shared skeleton with no missing animation paths. Play-mode checks passed for all 15 Animator states and material textures.

Extension: folder 17 uses Jump_with_Arms_Open_Boss1.anim, converted from its Armature coordinate system to the existing target_character rig. The controller state is Jump_with_Arms_Open_Boss1. Folder 16 is connected through Run_03_Boss1.anim (controller state Run_03_Boss1). It is appended after folder 17 in the debug playlist. The prefab and scene playlists now contain 17 clips. Existing debugger enable/play settings were preserved. Runtime validation passed for all 17 Animator states; ground contact was not adjusted.

