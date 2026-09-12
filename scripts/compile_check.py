#!/usr/bin/env python3
"""Compile the runtime mod against an installed Big Ambitions Managed folder."""

from __future__ import annotations

import argparse
import html
import shutil
import subprocess
import tempfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Assets" / "Mods" / "Unit-8200" / "Scripts" / "Unit8200Mod.cs"
DEFAULT_MANAGED = (
    Path.home()
    / "Library/Application Support/Steam/steamapps/common/Big Ambitions"
    / "Big Ambitions.app/Contents/Resources/Data/Managed"
)
ASSEMBLIES = [
    "BigAmbitions.dll",
    "BigAmbitions.AI.dll",
    "BigAmbitions.Items.dll",
    "BigAmbitions.ModAPI.dll",
    "BigAmbitions.ModsInternal.dll",
    "DayNightCycle.dll",
    "HGExtensions.dll",
    "HGPlugins.dll",
    "JimmysUnityUtilities.dll",
    "UnityEngine.dll",
    "UnityEngine.CoreModule.dll",
    "UnityUIExtensions.dll",
]


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--managed-dir", type=Path, default=DEFAULT_MANAGED)
    parser.add_argument("--output-dir", type=Path)
    args = parser.parse_args()

    missing = [name for name in ASSEMBLIES if not (args.managed_dir / name).is_file()]
    if missing:
        parser.error(f"Managed folder is missing required assemblies: {', '.join(missing)}")

    references = "\n".join(
        f'    <Reference Include="{html.escape(Path(name).stem)}"><HintPath>{html.escape(str(args.managed_dir / name))}</HintPath><Private>false</Private></Reference>'
        for name in ASSEMBLIES
    )
    project = f"""<Project Sdk=\"Microsoft.NET.Sdk\">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <NuGetAudit>false</NuGetAudit>
    <AssemblyName>Unit-8200</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=\"{html.escape(str(SOURCE))}\" Link=\"Unit8200Mod.cs\" />
{references}
  </ItemGroup>
</Project>
"""

    with tempfile.TemporaryDirectory(prefix="unit8200-compile-") as temporary:
        project_path = Path(temporary) / "Unit8200.CompileCheck.csproj"
        project_path.write_text(project, encoding="utf-8")
        subprocess.run(
            ["dotnet", "build", str(project_path), "--configuration", "Release", "--nologo", "--verbosity", "minimal"],
            check=True,
        )
        if args.output_dir:
            output = args.output_dir.resolve()
            if output.exists():
                shutil.rmtree(output)
            (output / "Locales").mkdir(parents=True)
            shutil.copy2(Path(temporary) / "bin/Release/netstandard2.1/Unit-8200.dll", output)
            for locale in ("en.json", "ja.json"):
                shutil.copy2(ROOT / "Assets/Mods/Unit-8200/Locales" / locale, output / "Locales")
            print(f"OK: built loadable mod at {output}")


if __name__ == "__main__":
    main()
