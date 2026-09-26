# Decompiled game source - generate this yourself

**This folder is intentionally empty in the repository.** It holds an ILSpy decompile of
*Isolated Inhale*'s own assembly, which is the game developer's copyrighted code and is
not ours to redistribute. Everything here except this file is gitignored.

NPC.Core does not need it to build. It is the one C# copy of the game that `docs/` cites
(`Gate.cs`, `EntryDetector.cs`, `LifecareController.cs`, `Docker.cs`, `Airlock.cs`,
`PlayerDetector.cs` most often). For the scene and serialized values, see
[docs/game-sources.md](../docs/game-sources.md).

## Generating it

```bash
dotnet tool install -g ilspycmd
ilspycmd -p -o decompiled "<game folder>/Isolated Inhale_Data/Managed/Assembly-CSharp.dll"
python tools/cleanexports.py --apply
```

- `-p` decompiles as a project, which keeps the namespace folders (`Space/Player.cs` and
  so on). The `.csproj` and `Properties/` it writes are harmless.
- The game ships as Mono, not IL2CPP, so the output is close to the original source.
  `Assembly-CSharp.pdb` sits next to the DLL; keep it there and ILSpy picks up the real
  local variable names.
- `docs/` line references come from an ILSpy dump of **v0.8.9**. Another game version or
  another decompiler shifts them.
- `cleanexports.py` deletes `bin/` and `obj/` left behind if an IDE built the project.

AssetRipper also writes a decompile of the same assembly. It is not used: this one is
regenerated with one command, uses a newer decompiler and reads the `.pdb`
([game-sources.md](../docs/game-sources.md#what-is-deliberately-not-kept)).

## Looking at one type without redumping

Flags vary a little between ilspycmd versions - check `ilspycmd -h`.

```bash
# one type, to stdout (fully qualified name: Airlock, but Space.Player)
ilspycmd -t Airlock "<game folder>/Isolated Inhale_Data/Managed/Assembly-CSharp.dll"

# list the types in an assembly (c = classes, i = interfaces, s = structs, d = delegates, e = enums)
ilspycmd -l c "<game folder>/Isolated Inhale_Data/Managed/Assembly-CSharp.dll"

# IL instead of C#, when the decompiled C# looks wrong (compiler-generated
# iterators and coroutines are the usual suspects). -il ignores -t and prints
# the whole assembly, so write it to a file outside this folder and search it.
ilspycmd -il "<game folder>/Isolated Inhale_Data/Managed/Assembly-CSharp.dll" > assembly.il
```

The same works on the other assemblies in `Managed/` when behaviour lives outside the
game's own code: `UnityEngine.*Module.dll` (engine - physics, animation),
`UnityEngine.UI.dll`, `FMODUnity.dll` (audio), `Newtonsoft.Json.dll` (the save format).
Don't dump those into this folder; `docs/` line references assume it holds
`Assembly-CSharp` only.
