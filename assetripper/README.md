# AssetRipper export - generate this yourself

**This folder is intentionally empty in the repository.** It holds AssetRipper exports of
the game's data - the developer's copyrighted assets, not ours to redistribute. Everything
here except this file is gitignored. What each export answers: [docs/game-sources.md](../docs/game-sources.md).

| Folder | AssetRipper export mode | Holds |
| --- | --- | --- |
| `unity/` | Unity project | scenes, prefabs and ScriptableObjects as Unity YAML with every field named |
| `primary/` | Primary content | meshes as `.glb`, textures as `.png` |

## Generating it

1. Open `<game folder>/Isolated Inhale_Data` in AssetRipper.
2. Export as a Unity project into `assetripper/unity/`.
3. Export primary content into `assetripper/primary/`.
4. Run `python tools/cleanexports.py --apply`.

The cleanup deletes what the exports duplicate (about 160 MB): the copies of the game's
DLLs, both exports' decompiled scripts and the primary export's shader dumps. It keeps the
`.cs.meta` files, which carry the script GUIDs the scene references. It also moves an old
`assetripper_unity/` or `assetripper_primary/` folder, or an export dropped straight into
`assetripper/`, into this layout. Without `--apply` it only prints what it would do.

Re-export after a game update, the same as the decompile.

## Viewing the models

Open the `primary/` `.glb` files in Blockbench with the glTF Importer plugin
(File > Plugins, search "glTF Importer"; needs Blockbench 4.12.6 or newer).

- The plugin adds a glTF / GLB import to Blockbench's Generic Model format.
- It imports meshes, textures, groups and animations, but not armatures, so skinned models
  come in unrigged.
- Single models: `primary/Assets/Resources/models/<name>/`.

## Reading the Unity export

```yaml
# unity/ExportedProject/Assets/Scenes/GameScene.unity - the OxygenStation SpaceObject
  m_GameObject: {fileID: 427}
  m_Script: {fileID: 11500000, guid: 9fac49c410e1d08befe20f3cb1f4715b, type: 3}
  richPresenceName: Oxygen station
  contentParent: {fileID: 725}
  rooms:
  - {fileID: 89168}
```

- Scenes: `unity/ExportedProject/Assets/Scenes/GameScene.unity` (the `level1` scene,
  ~69 MB) and `MainMenu.unity`. Also: prefabs (`Assets/GameObject`), ScriptableObject data
  (`Assets/MonoBehaviour`), materials, `ProjectSettings/` (tags, layers, physics).
- `{fileID: N}` is another object in the same file: search for `--- !u!<classID> &N`.
- A `guid` points at an asset; `grep -rl <guid> --include=*.meta assetripper/unity` names it.
  Script GUIDs resolve to `Assets/Scripts/Assembly-CSharp/<Class>.cs.meta`; read the class
  itself in [`decompiled/`](../decompiled/README.md).
