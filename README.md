### IMPORTANT NOTICE / DISCLAIMER

- **Original Author:** R_F
- **Original Repository:** danyhappy564-cmyk/Gear_Enhance
- **Original Link:** https://github.com/danyhappy564-cmyk/Gear_Enhance
- **License:** MIT

1. **Reflection & Take-Downs:** I deeply reflect on the ECOT incident. As an AI-assisted "vibe coder," I will immediately delete files if the original authors ask.
2. **No Re-Distribution:** These ported builds are unverified, temporary fixes. Please do NOT re-upload or share them anywhere else.
3. **Do Not Pester Original Authors:** Never report bugs or pester original modders regarding issues from my unofficial ports.
4. **Full Credit & Respect:** I will always credit original creators on GitHub and prioritize their decisions above all else.
5. **Support Original Creators:** Instead of using my ports, please visit the original authors' Forge pages to leave kind words or tips.

---

## 변경 이력

- 2026-10-22 11:53 — 1차 프로토타입: 창고 장비 우클릭 강화 창, F12 설정, 가격 비례 비용, 서버 확률 판정, 실패 처리, 아이템별 단계 기록을 추가했습니다. 능력치 효과는 다음 단계로 보류했습니다.

# 장비 강화 — Gear Enhance

SPT **4.1.6**용 클라이언트·서버 모드입니다. 창고의 무기·방어구를 우클릭하면 **장비 강화** 메뉴를 추가하고 별도 창에서 비용과 확률을 확인한 뒤 시도하도록 구현했습니다.

**현재는 개발용 0.1.0 프로토타입입니다. 강화 단계만 저장하며 반동·인체공학·방어 성능은 바꾸지 않습니다. 루블 차감과 실패 시 내구도 0·삭제 기능은 실제로 처리하도록 구현되어 있습니다. 게임 화면과 실제 플레이에서의 작동은 아직 검증하지 않았습니다.**

## 사용 흐름

1. 장착한 장비를 해제하고 창고에 넣습니다. 장비의 내부 보관칸은 비워 주세요.
2. 내 장비를 우클릭하고 **장비 강화**를 누릅니다.
3. 현재·다음 단계, 성공률, 비용, 보유 루블과 실패 결과를 확인합니다. 견적은 60초 동안 유효합니다.
4. **비용을 지불하고 강화**를 누릅니다. 파괴 옵션을 선택했다면 부착물까지 사라진다는 확인란도 체크해야 합니다.
5. 서버가 판정한 결과와 지불 금액을 표시합니다. 100% 성공도 비용은 소모합니다.

강화 창은 별도로 그린 초기 UI이며 게임의 수리 창 프리팹을 복제한 화면은 아닙니다. 창이 열린 동안 뒤쪽 인벤토리의 마우스 클릭을 차단하도록 구현했습니다. 레이드·상인 판매품·다른 사람의 장비에서는 강화할 수 없도록 제한합니다.

## F12 설정과 기본값

BepInEx ConfigurationManager가 설치되어 있으면 F12의 **Gear Enhance**에서 조절합니다. 설정 파일은 `BepInEx/config/rf.gearenhance.cfg`입니다. 설정을 바꾼 뒤 견적을 다시 불러오면 적용됩니다.

| 설정 | 기본값 | 의미 |
|---|---|---|
| 성공률 | 75% | 0~100% |
| 항상 성공 | OFF | 켜면 성공률 100% |
| 최대 단계 | +10 | +1~+100 |
| 기준 가격 | 북밸류 | 플리 기준가도 선택 가능 |
| 비용 비율 | 10% | 장비 **본체** 가격 기준, 부착물 가격 제외 |
| 단계별 비용 배수 | 1.25배 | 현재 단계마다 비용 증가 |
| 최소 비용 | 1,000 루블 | 비용은 올림, 1회 최대 1억 루블 |
| 실패 처리 | 비용만 소모 | 내구도 0 / 파괴도 선택 가능 |

비용은 `최대(최소 비용, 본체 기준가 × 비용 비율 × 단계별 배수^현재 단계)`를 올림한 값입니다. 예를 들어 기준가 10만 루블이면 +0→+1은 1만, +1→+2는 12,500 루블입니다. 플리 기준가는 SPT가 가진 템플릿별 기준가이며 실시간 최저 매물가가 아닙니다. 플리 가격이 없으면 북밸류를 사용하며 창에 그 사실을 표시합니다.

실패 옵션:

- `KeepItem`: 루블만 소모하며 아이템과 기존 강화 단계는 유지합니다.
- `ZeroDurability`: 본체와 내구도가 있는 부착물·방탄판의 **현재** 내구도를 0으로 만듭니다. 최대 내구도는 유지합니다.
- `Destroy`: 본체와 부착물, 장착된 탄창·탄약을 삭제합니다. 보험 등록도 함께 제거합니다. 되돌리기 기능은 없습니다.

강화 단계와 최근 100회 결과는 SPT 프로필의 추가 항목 `rfGearEnhance`에 아이템 ID별로 보관하며 일반 프로필 저장 흐름을 사용합니다. 판매·분해 후 새 아이템 ID로 바뀌는 경우 기록을 이전하지 않습니다. 레이드 이후 ID 유지 여부와 다른 모드의 프로필 수정에 대한 실전 호환성은 미검증입니다.

## 빌드

필요한 도구는 **.NET SDK 10, Python 3.12+, 실제 SPT 4.1.6 설치본**입니다. Python 외부 패키지는 빌드 스크립트에 필요하지 않습니다.

```powershell
python tools/build.py --spt-root "E:\SPT 4.1"
```

완료되면 `Build/GearEnhance-0.1.0.zip`을 만듭니다. 이 명령은 게임 폴더에 설치하거나 게임·서버를 실행하지 않습니다. IDE에서는 `LocalSettings.props.example`을 `LocalSettings.props`로 복사하고 `SptRoot`를 본인 경로로 바꿉니다. 개인 경로 파일은 Git에서 제외합니다.

직접 검사할 때:

```text
dotnet build src/Server/GearEnhance.Server.csproj -c Release
dotnet build src/Client/GearEnhance.Client.csproj -c Release -p:SptRoot="E:\SPT 4.1"
dotnet test tests/GearEnhance.Tests.csproj -c Release
```

실제 게임 어셈블리 API 검사까지 하려면 `GEAR_GAME_ASSEMBLY`를 `EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll` 전체 경로로 지정합니다. 지정하지 않으면 해당 검사 3개는 생략합니다. 빌드 도구는 실제 게임 DLL이 있으면 자동으로 지정합니다.

## 검증 범위

자동 검사는 확률 경계값, 비용 계산과 상한, 루블 부족, 창고 밖 아이템 차단, 장비 상태 변경, 중복 요청·동시 실행, 현재 내구도 0, 부착물·보험 삭제, 프로필 JSON 저장 후 기록 복원과 클라이언트·서버 JSON 호환성을 다룹니다. 루블 차감과 삭제 검사에서는 SPT 4.1.6의 실제 `InventoryHelper`를 사용합니다.

클라우드의 클라이언트 컴파일은 BepInEx 5.4.23.2와 NuGet의 Unity 참조 모듈 2021.3.33으로 검사했습니다. 스텁 DLL은 사용하지 않았습니다. 게임의 실제 Unity 모듈로 빌드한 결과나 인게임 검증을 대신하지 않으며 이 참조 환경에서는 배포 zip을 만들지 않습니다. `--validate-only` 옵션으로 컴파일·검사만 수행할 수 있습니다.

인게임 확인이 필요한 항목은 우클릭 메뉴·한글 표시, 입력 차단·창 닫기, F12 변경 반영, SPT HTTP 연결과 세션 콜백, 결과 후 인벤토리 갱신, 재접속 시 기록, Transmog·수리 관련 모드와의 호환성입니다. 일반 장비나 본 프로필을 쓰기 전에 별도 테스트 프로필과 사본으로 확인하세요.

## 다음 단계 제안

- 무기·방어구의 실제 강화 효과를 각각 선택하고 단계별 증가량·상한을 결정하기.
- 실패가 반복될수록 성공률을 올리는 보정과 일정 단계까지의 안전 강화.
- 파괴 방지 아이템 또는 추가 비용을 내는 보호 옵션.
- 아이템 이름의 `+단계` 표시와 강화 기록 조회 화면.

이 제안들은 현재 구현에 포함하지 않았습니다.

## 참고

- [Klamist/RepairSkillExtension](https://github.com/Klamist/RepairSkillExtension) — 수리 버프와 SPT 서버 아이템 변경 경로 조사에 참고했습니다. 참고 모드는 설치 의존성이 아니며 그 모드의 수리 동작은 교체하지 않습니다.
- [Transmog 포팅 기록](https://github.com/danyhappy564-cmyk/Cluade_For_spt/blob/main/projects/Transmog-zzap--Bootleg-/NOTES.md) — SPT 4.1 우클릭 동적 메뉴 API를 확인했습니다. 별도 CustomInteractions 프리패처는 필요하지 않습니다.
- [설계와 데이터 흐름](docs/DESIGN.md)
