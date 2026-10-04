"""Review/apply Unity 6.7 generated Core command buffers to a Unity project."""

import argparse
import json
from pathlib import Path
import re
import shutil
import sys

FILES = (
    "ComputeCommandBuffer.cs", "RasterCommandBuffer.cs", "UnsafeCommandBuffer.cs",
    "IBaseCommandBuffer.cs", "IComputeCommandBuffer.cs", "IRasterCommandBuffer.cs",
    "IUnsafeCommandBuffer.cs",
)
PACKAGE = "com.unity.render-pipelines.core"
VERSION = "17.7.0"


def corrected(text):
    text = re.sub(r"(?<![\w.])BuildSettings(?= buildSettings)",
                  "RayTracingAccelerationStructure.BuildSettings", text)
    text = text.replace("NativeArray`1[T]", "NativeArray<T>")
    text = text.replace("NativeArray`1", "NativeArray<T>")
    text = text.replace("RequestAsyncReadbackIntoNativeArray(output,",
                        "RequestAsyncReadbackIntoNativeArray(ref output,")
    text = text.replace(
        "int argsOffset, uint drawCount, GraphicsBuffer countBuffer, "
        "int countBufferOffset, MaterialPropertyBlock properties",
        "int argsOffset = 0, uint drawCount = 1, GraphicsBuffer countBuffer = null, "
        "int countBufferOffset = 0, MaterialPropertyBlock properties = null",
    )
    return text


def matching_package(path):
    manifest = path / "package.json"
    if not manifest.is_file():
        return False
    data = json.loads(manifest.read_text(encoding="utf-8-sig"))
    return data.get("name") == PACKAGE and data.get("version") == VERSION


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("project", type=Path, help="Unity project with generated root .cs files")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--apply", action="store_true", help="Embed Core if needed and write corrections")
    mode.add_argument("--check", action="store_true", help="Verify the embedded files; never write")
    args = parser.parse_args()
    project = args.project.resolve()
    if not (project / "Assets").is_dir() or not (project / "Packages/manifest.json").is_file():
        parser.error(f"Not a Unity project: {project}")

    # Validate all inputs before embedding or changing any file.
    outputs = {}
    for name in FILES:
        source = project / name
        if not source.is_file():
            parser.error(f"Missing generated file: {source}; generate Core CommandBuffers in Unity first")
        text = source.read_text(encoding="utf-8-sig")
        if "namespace UnityEngine.Rendering" not in text or "automatically generated" not in text:
            parser.error(f"Not a recognized generated Core command buffer: {source}")
        outputs[name] = corrected(text)

    embedded = project / "Packages" / PACKAGE
    if embedded.exists():
        if not matching_package(embedded):
            parser.error(f"Existing embedded package must be {PACKAGE} {VERSION}: {embedded}")
        original = embedded
    else:
        if args.check:
            parser.error(f"No embedded Core package: {embedded}")
        candidates = [p for p in (project / "Library/PackageCache").glob(f"{PACKAGE}@*")
                      if matching_package(p)]
        if len(candidates) != 1:
            parser.error(f"Expected one cached Core {VERSION} package; found {len(candidates)}")
        original = candidates[0]

    relative = Path("Runtime/CommandBuffers")
    for name in FILES:
        if not (original / relative / name).is_file():
            parser.error(f"Existing Core package is missing {relative / name}")

    mismatches = []
    for name, output in outputs.items():
        before = (original / relative / name).read_text(encoding="utf-8-sig")
        if before != output:
            mismatches.append(name)
        print(f"{name}: {'matches' if before == output else 'will change'}")

    if args.check:
        if mismatches:
            print(f"CHECK FAILED: {len(mismatches)} files differ", file=sys.stderr)
            return 1
        print("CHECK PASSED: all seven embedded files match the reviewed output")
        return 0

    if not args.apply:
        print(f"Preview only. Target: {embedded}")
        print("Use --apply to embed if needed and write the reviewed generated files.")
        return 0

    if original != embedded:
        shutil.copytree(original, embedded)
    for name, output in outputs.items():
        (embedded / relative / name).write_text(output, encoding="utf-8")
    print("Applied all seven files; retained existing .meta files and original root output.")
    print("Resolve packages/refresh in Unity, then wait for compilation.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
