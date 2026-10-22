"""두 DLL을 빌드·검사하고 게임에 복사하지 않고 설치용 zip을 만듭니다."""
import argparse
import os
from pathlib import Path
import subprocess


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

    run("dotnet", "restore", "GearEnhance.Package.csproj", "--locked-mode", f"-p:SptRoot={game}")
    run("dotnet", "restore", "tests/GearEnhance.Tests.csproj", "--locked-mode")
    run("dotnet", "restore", "client-tests/GearEnhance.ClientTests.csproj", "--locked-mode", f"-p:SptRoot={game}")
    run("dotnet", "build", "GearEnhance.Package.csproj", "-c", "Release", "--no-restore",
        f"-p:SptRoot={game}", "-p:GearCreatePackage=false")
    run("dotnet", "test", "tests/GearEnhance.Tests.csproj", "-c", "Release", "--no-restore")
    run("dotnet", "test", "client-tests/GearEnhance.ClientTests.csproj", "-c", "Release", "--no-restore", f"-p:SptRoot={game}")
    if args.validate_only:
        print("컴파일·검사 완료. 인게임 검증 및 배포 빌드는 수행하지 않았습니다.")
        return
    run("dotnet", "msbuild", "GearEnhance.Package.csproj", "-t:CreateGearPackage",
        "-p:Configuration=Release", f"-p:SptRoot={game}")


if __name__ == "__main__":
    main()
