"""Bootstrap the EMBER Unity project.

1. Copies the URP Blank template's ProjectData~ (Assets/Packages/ProjectSettings) into EMBER/.
2. Patches ProjectSettings.asset (activeInputHandler -> 2 = Both, productName -> EMBER).
3. Extracts the class .unitypackage files into EMBER/ exactly like Unity's importer would
   (GUID folders -> <pathname> + <pathname>.meta), so asset GUIDs are preserved.

Usage: python -I bootstrap_project.py <unity_editor_dir> <game_root>
"""
import os
import re
import sys
import tarfile

SKIP_TEMPLATE = (
    "Assets/TutorialInfo",
    "Assets/Readme.asset",
    "Assets/Scenes",
    "Assets/Settings/SampleSceneProfile.asset",
)
PACKAGES = ["01.unitypackage", "grant_life.unitypackage", "create_world.unitypackage"]


def safe_join(root, rel):
    rel = rel.replace("\\", "/").strip()
    if rel.startswith("/") or ".." in rel.split("/") or ":" in rel:
        raise ValueError(f"unsafe path in archive: {rel!r}")
    out = os.path.normpath(os.path.join(root, rel))
    if not out.startswith(os.path.normpath(root) + os.sep):
        raise ValueError(f"path escapes project: {rel!r}")
    return out


def copy_template(editor_dir, project):
    tgz = os.path.join(editor_dir, "Data", "Resources", "PackageManager", "ProjectTemplates",
                       "com.unity.template.urp-blank-17.2.6.tgz")
    count = 0
    with tarfile.open(tgz, "r:gz") as tf:
        for m in tf.getmembers():
            if not m.isfile() or "ProjectData~/" not in m.name:
                continue
            rel = m.name.split("ProjectData~/", 1)[1]
            if rel.startswith("Library/") or any(rel == s or rel.startswith(s + "/") or rel == s + ".meta" for s in SKIP_TEMPLATE):
                continue
            dst = safe_join(project, rel)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            with open(dst, "wb") as f:
                f.write(tf.extractfile(m).read())
            count += 1
    print(f"template: {count} files")


def patch_settings(project):
    p = os.path.join(project, "ProjectSettings", "ProjectSettings.asset")
    with open(p, "r", encoding="utf-8") as f:
        t = f.read()
    t, n1 = re.subn(r"activeInputHandler: \d", "activeInputHandler: 2", t)
    t, n2 = re.subn(r"productName: .*", "productName: EMBER", t)
    with open(p, "w", encoding="utf-8", newline="\n") as f:
        f.write(t)
    print(f"ProjectSettings patched (inputHandler={n1}, productName={n2})")


def extract_package(pkg_path, project):
    entries = {}
    with tarfile.open(pkg_path, "r|gz") as tf:
        for m in tf:
            parts = m.name.strip("./").split("/")
            if len(parts) != 2 or not m.isfile():
                continue
            guid, kind = parts
            if kind in ("pathname", "asset", "asset.meta"):
                entries.setdefault(guid, {})[kind] = tf.extractfile(m).read()
    written = 0
    for guid, e in entries.items():
        if "pathname" not in e:
            continue
        rel = e["pathname"].decode("utf-8").splitlines()[0].strip()
        if not rel.startswith("Assets/"):
            raise ValueError(f"unexpected path {rel!r}")
        dst = safe_join(project, rel)
        if "asset" in e:
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            with open(dst, "wb") as f:
                f.write(e["asset"])
        else:
            os.makedirs(dst, exist_ok=True)
        if "asset.meta" in e:
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            with open(dst + ".meta", "wb") as f:
                f.write(e["asset.meta"])
        written += 1
    print(f"{os.path.basename(pkg_path)}: {written} assets")


def main():
    editor_dir, root = sys.argv[1], sys.argv[2]
    project = os.path.join(root, "EMBER")
    if os.path.exists(os.path.join(project, "ProjectSettings")):
        print("EMBER project already exists - skipping template copy")
    else:
        copy_template(editor_dir, project)
        patch_settings(project)
    for pkg in PACKAGES:
        extract_package(os.path.join(root, pkg), project)


if __name__ == "__main__":
    main()
