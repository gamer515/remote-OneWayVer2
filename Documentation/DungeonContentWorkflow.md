# Dungeon 3D Tile Palette workflow

## Current policy

- The old Castle, Farmhouse, House, Monument, and Pleasure House prefabs were deleted by the user.
- They are not restored by this setup.
- Their entries were removed from active Terrain/Transform JSON and missing prefab catalog references.
- The old runtime random floor/prop generator was removed.
- Environment geometry is now authored and saved in a prefab.
- JSON remains responsible for NPCs, future interactable buildings, positions, story links, and unlock data.

The old building encounter folders are currently retained as archived content. They are not loaded
because nothing in active Terrain JSON points to them.

## Open the authoring prefab

Open `Assets/Decision_YYS/Prafabs/Terrain_Initial_Village.prefab` in Prefab Mode.

```text
Terrain_Initial_Village
└─ DungeonAuthoringGrid       Grid: 2.8 / Rectangle / XZY
   ├─ FloorTiles
   ├─ Structures
   └─ Decorations
```

## Open and use the palette

1. Open `Window > 2D > Tile Palette`.
2. Select `Dungeon3DPalette` from the Palette dropdown.
3. Select a brush whose name starts with `Dungeon`.
4. Set the Active Tilemap/Target to `FloorTiles`, `Structures`, or `Decorations`.
5. Paint in Scene view and save the prefab.

Palette:
`Assets/Decision_YYS/TilePalettes/Dungeon3D/Dungeon3DPalette.prefab`

Brushes:
`Assets/Decision_YYS/TilePalettes/Dungeon3D/Brushes/`

`Dungeon Random Floor` chooses floor at weight 4 and floor-detail at weight 1, with random
90-degree rotation. Randomness happens only while painting. Painted GameObjects are saved and never
change during play.

Individual brushes exist for floor, floor-detail, walls, stairs, gate, column, wood structures,
banner, barrel, pot, rocks, stones, table, chair, chest, and trap.

## Controls

- Paint: place on the 2.8-unit Grid.
- Box: fill a rectangular region.
- Eraser: remove the object in a cell.
- Rotate: rotate the brush in 90-degree steps.
- Undo: `Ctrl+Z`.

Flood Fill is intentionally unsupported for the random 3D brush because an unrestricted fill can
create too many GameObjects.

## Environment versus JSON

Store floor, walls, stairs, gates, columns, and non-interactive decoration in the terrain prefab.

Store NPCs, interactable buildings, event objects, placement slots, story paths, requirements, and
unlock results in JSON:

`Assets/Decision_YYS/Resources/Story_Json_Data/<Chapter>/<Episode>/`

- `Terrain.json`: prefab/story/chunk connection.
- `Transform.json`: side, Z slot, offset, and rotation.
- `Encounters/<placeId>/Interaction.json`: encounter choices.
- `Encounters/<placeId>/Story.json`: dialogue/story cards.

When adding a new interactable building later, create its prefab, register a unique `prefabId` in
`DecisionScene > Environment > EnvController > Terrain Prefab Catalog`, then add matching Terrain,
Transform, Interaction, and Story JSON entries.

## Locks and unlocks

```json
{
  "text": "Open the sealed door",
  "action": "open",
  "requiresUnlocks": ["item:dungeon_key"],
  "grantsUnlocks": ["place:inner_room"],
  "relationshipId": "sage",
  "relationshipDelta": 1
}
```

- Remove `requiresUnlocks` or use an empty array to remove a lock.
- Add stable keys to `grantsUnlocks` to unlock later choices.
- Completing an encounter records `event:<placeId>`.

## Add or restore brushes

`AgentScripts/SetupDungeon3DPalette.cs` recreates missing standard brush assets and ensures the Grid
exists. It does not repaint or delete authored tiles. For a new FBX, add its filename without the
extension to the script's `names` array and rerun the setup.

## Play test

1. Save the terrain prefab.
2. Open `Assets/Scenes/MainMenuScene.unity`.
3. Enter Play Mode and start or load a game.
4. Confirm the authored environment stays unchanged.
5. Confirm the NPCs still spawn from JSON and their encounter UI works.

Do not test by opening DecisionScene directly; it requires session data prepared by the menu flow.
