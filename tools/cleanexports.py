#!/usr/bin/env python3
"""Tidy the local game exports: move them into the standard layout and delete redundant files.

Layout (docs/game-sources.md):
    decompiled/            ilspycmd -p of Assembly-CSharp
    assetripper/unity/     AssetRipper "Unity project" export (ExportedProject/, AuxiliaryFiles/)
    assetripper/primary/   AssetRipper "primary content" export (glb, png)

Usage
    python tools/cleanexports.py            # dry run: print what would move and be deleted
    python tools/cleanexports.py --apply    # do it

No dependencies. Only touches the three folders above (and the legacy names it migrates).
"""
import argparse
import glob
import os
import shutil
import stat
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
UNITY = os.path.join('assetripper', 'unity')
PRIMARY = os.path.join('assetripper', 'primary')

# Legacy locations -> standard ones. A flat export straight into assetripper/ counts as legacy too.
MOVES = [
    ('assetripper_unity', UNITY),
    ('assetripper_primary', PRIMARY),
    (os.path.join('assetripper', 'ExportedProject'), os.path.join(UNITY, 'ExportedProject')),
    (os.path.join('assetripper', 'AuxiliaryFiles'), os.path.join(UNITY, 'AuxiliaryFiles')),
]

# Whole folders that are redundant (copies of game files, other decompiles, IDE and editor caches).
JUNK_DIRS = [
    os.path.join('decompiled', 'bin'),
    os.path.join('decompiled', 'obj'),
    os.path.join('decompiled', '.vs'),
    os.path.join('decompiled', 'Assembly-CSharp', 'bin'),
    os.path.join('decompiled', 'Assembly-CSharp', 'obj'),
    os.path.join(UNITY, 'AuxiliaryFiles', 'GameAssemblies'),
    os.path.join(UNITY, 'ExportedProject', 'Library'),
    os.path.join(UNITY, 'ExportedProject', 'Temp'),
    os.path.join(UNITY, 'ExportedProject', 'Logs'),
    os.path.join(UNITY, 'ExportedProject', 'UserSettings'),
    os.path.join(PRIMARY, 'Assemblies'),
    os.path.join(PRIMARY, 'Scripts'),
    os.path.join(PRIMARY, 'AuxiliaryFiles', 'GameAssemblies'),
    os.path.join(PRIMARY, 'Assets', 'Shader'),
]

# Loose files: the second decompile in the Unity export (.cs.meta is kept, it carries script GUIDs),
# and the old gitignore stubs left inside moved folders.
JUNK_GLOBS = [
    os.path.join(UNITY, 'ExportedProject', 'Assets', 'Scripts', '**', '*.cs'),
]
STUB_MARKER = 'survives the gitignore'


def size_of(path):
    if os.path.isfile(path):
        return os.path.getsize(path)
    total = 0
    for root, _, files in os.walk(path):
        for f in files:
            try:
                total += os.path.getsize(os.path.join(root, f))
            except OSError:
                pass
    return total


def mb(n):
    return '%.1f MB' % (n / 1048576)


def force_remove(func, path, _):
    # Windows: read-only files make rmtree fail.
    os.chmod(path, stat.S_IWRITE)
    func(path)


def remove(path):
    if os.path.isdir(path):
        shutil.rmtree(path, onerror=force_remove)
    else:
        os.chmod(path, stat.S_IWRITE)
        os.remove(path)


def prune_empty(path):
    """Remove folders the deletes left empty, stopping below the top-level export folder."""
    while os.path.isdir(path) and not os.listdir(path) and os.path.dirname(os.path.relpath(path, REPO)):
        os.rmdir(path)
        path = os.path.dirname(path)


def plan_moves():
    moves = []
    for src, dst in MOVES:
        s, d = os.path.join(REPO, src), os.path.join(REPO, dst)
        if not os.path.isdir(s):
            continue
        if os.path.exists(d):
            print('skip move %s -> %s: target exists, merge by hand' % (src, dst))
            continue
        moves.append((s, d))
    return moves


def plan_deletes(moved):
    """Paths to delete. Deletes run before moves, so paths inside a pending move use the old location."""
    def where(rel):
        full = os.path.join(REPO, rel)
        for s, d in moved:
            if full == d or full.startswith(d + os.sep):
                return s + full[len(d):]
        return full

    found = []
    for rel in JUNK_DIRS:
        p = where(rel)
        if os.path.isdir(p):
            found.append(p)
    for pattern in JUNK_GLOBS:
        found += [p for p in glob.glob(where(pattern), recursive=True) if os.path.isfile(p)]
    for rel in (UNITY, PRIMARY):
        stub = os.path.join(where(rel), 'README.md')
        if os.path.isfile(stub) and STUB_MARKER in open(stub, encoding='utf-8', errors='replace').read():
            found.append(stub)
    return found


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('--apply', action='store_true', help='move and delete; without it, only print')
    args = ap.parse_args()

    moves = plan_moves()
    deletes = plan_deletes(moves)
    if not moves and not deletes:
        print('nothing to do')
        return 0

    for s, d in moves:
        print('move   %s -> %s' % (os.path.relpath(s, REPO), os.path.relpath(d, REPO)))
    total = 0
    loose = []
    for p in deletes:
        n = size_of(p)
        total += n
        if os.path.isdir(p):
            print('delete %-60s %10s' % (os.path.relpath(p, REPO) + os.sep, mb(n)))
        else:
            loose.append(n)
    if loose:
        print('delete %d loose files (.cs in the Unity export, old README stubs) %10s' % (len(loose), mb(sum(loose))))
    print('total  %s' % mb(total))

    if not args.apply:
        print('dry run - pass --apply to do it')
        return 0

    for p in deletes:
        remove(p)
        prune_empty(os.path.dirname(p))
    for s, d in moves:
        os.makedirs(os.path.dirname(d), exist_ok=True)
        shutil.move(s, d)
    print('done')
    return 0


if __name__ == '__main__':
    sys.exit(main())
