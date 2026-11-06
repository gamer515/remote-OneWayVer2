# NewPlayer character convention

- Every new NewPlayer character must keep its right hand in a permanent fist. Leave the left hand unchanged unless requested.
- `Editor/NewPlayerRightFist.cs` applies this to models imported under this folder, using RightHand and RightLowerArm/RightForeArm bones. Derived meshes should inherit the imported mesh or call `NewPlayerRightFist.Apply` in editor authoring code. Its `_RightFist` mesh-name marker prevents repeated deformation.
- Keep sword direction curves, but place the grip inside the fist rather than at the wrist.
- Preserve the user's latest helmet and shoulder-pad transforms. Do not replace the gameplay Player when making previews.
