"""두 DLL을 빌드·검사하고 게임에 복사하지 않고 설치용 zip을 만듭니다."""
import argparse
import os
from pathlib import Path
import subprocess
import tempfile
import zipfile


ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--spt-root", type=Path, required=True, help="SPT 4.1.6 게임 설치 경로")
    parser.add_argument("--validate-only", action="store_true", help="참조 DLL로 컴파일·검사만 합니다. 배포 zip을 만들지 않습니다.")
    args = parser.parse_args()
    game = args.spt_root.resolve()
    if not args.validate_only and not (game / "EscapeFromTarkov.exe").is_file():
        parser.error("배포 빌드는 실제 SPT 게임 폴더가 필요합니다. 참조 전용 환경에서는 --validate-only를 사용하세요.")
    environ = os.environ.copy()
    assembly = game / "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll"
    if assembly.is_file():
        environ["GEAR_GAME_ASSEMBLY"] = str(assembly)

    def run(*arguments):
        subprocess.run(arguments, cwd=ROOT, env=environ, check=True)

    run("dotnet", "restore", "src/Server/GearEnhance.Server.csproj", "--locked-mode")
    run("dotnet", "restore", "src/Client/GearEnhance.Client.csproj", "--locked-mode", f"-p:SptRoot={game}")
    run("dotnet", "restore", "tests/GearEnhance.Tests.csproj", "--locked-mode")
    run("dotnet", "restore", "client-tests/GearEnhance.ClientTests.csproj", "--locked-mode", f"-p:SptRoot={game}")
    run("dotnet", "build", "src/Server/GearEnhance.Server.csproj", "-c", "Release", "--no-restore")
    run("dotnet", "build", "src/Client/GearEnhance.Client.csproj", "-c", "Release", "--no-restore", f"-p:SptRoot={game}")
    run("dotnet", "test", "tests/GearEnhance.Tests.csproj", "-c", "Release", "--no-restore")
    run("dotnet", "test", "client-tests/GearEnhance.ClientTests.csproj", "-c", "Release", "--no-restore", f"-p:SptRoot={game}")
    if args.validate_only:
        print("컴파일·검사 완료. 인게임 검증 및 배포 빌드는 수행하지 않았습니다.")
        return
    build = ROOT / "Build"
    build.mkdir(exist_ok=True)
    output = build / "GearEnhance-0.2.0.zip"
    # Build to a temporary archive so a failed build never leaves a partial zip.
    with tempfile.TemporaryDirectory(prefix="gear-package-", dir=build) as work:
        archive = Path(work) / output.name
        with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as bundle:
            bundle.write(ROOT / "src/Client/bin/Release/netstandard2.1/GearEnhance.Client.dll",
                         "BepInEx/plugins/GearEnhance/GearEnhance.Client.dll")
            bundle.write(ROOT / "src/Server/bin/Release/net10.0/GearEnhance.Server.dll",
                         "SPT_Runtime/user/mods/GearEnhance/GearEnhance.Server.dll")
            for name in ("LICENSE", "README.md"):
                bundle.write(ROOT / name, f"SPT_Runtime/user/mods/GearEnhance/{name}")
        archive.replace(output)
    print(f"패키지: {output} (게임 폴더에는 복사하지 않았습니다.)")


if __name__ == "__main__":
    main()
