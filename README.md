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

- 2026-10-22 13:32 — 승인된 목업 기준 강화 창을 재구성하고, 무기·방어구 특화 5종과 단계별 능력치 효과, F12 효과 조절, 방향 변경·기존 단계 이전을 추가했습니다.
- 2026-10-22 11:53 — 1차 프로토타입: 창고 장비 우클릭 강화 창, F12 설정, 가격 비례 비용, 서버 확률 판정, 실패 처리, 아이템별 단계 기록을 추가했습니다. 능력치 효과는 다음 단계로 보류했습니다.

# 장비 강화 — Gear Enhance 0.2.0

SPT **4.1.6**용 클라이언트·서버 모드입니다. 창고의 무기·방어구를 우클릭하여 **장비 강화** 창을 열고, 강화 방향·성공 후 능력치·확률·비용·실패 결과를 확인한 뒤 시도합니다.

이번 버전은 **능력치 효과와 목업 기준 UI를 구현한 개발판**입니다. 클라우드에서 컴파일·자동 검사·실제 게임 DLL의 API 대조를 완료했지만 **실제 게임 화면과 플레이는 아직 검증하지 않았습니다.**

## 화면 구성

![승인된 강화 창 디자인 목업](docs/images/enhancement-mockup.png)

위 이미지는 실제 게임 캡처가 아닌 승인된 설계 목업입니다. 구현은 같은 구성의 전용 IMGUI 창이며 게임의 수리 창 프리팹을 복제하지 않습니다. 실제 장비의 게임 아이콘을 표시하고 화면 크기에 맞춰 창을 축소합니다. 게임에서의 글꼴·아이콘·배치·입력 차단은 첫 실행 확인이 필요합니다.

## 강화 효과

아이템 하나에 **강화 방향 하나**를 적용합니다. 무기는 반동 제어·조작성·정밀성, 방어구는 내구성·기동성 중 고릅니다.

| 장비 | 방향 | 단계당 기본 효과 | 기본 +10 효과·상한 |
|---|---|---|---|
| 무기 | 반동 제어 | 수직·수평 반동 1.5% 감소 | 15% 감소 |
| 무기 | 조작성 | 인체공학 +1 | +10, 무기의 최종 인체공학은 100 상한 |
| 무기 | 정밀성 | 탄 퍼짐 1% 감소 | 10% 감소 |
| 방어구 | 내구성 | 피격 시 내구도 소모 3% 감소 | 30% 감소 |
| 방어구 | 기동성 | 해당 장비의 이동·회전·인체공학 페널티 2% 완화 | 20% 완화 |

- 목업의 반동 제어 **+4: 6% → +5: 7.5%**를 그대로 적용합니다.
- 효과는 선택한 장비 ID에 연결하며 같은 종류의 다른 장비를 바꾸지 않습니다. 매 계산에 한 번만 곱하고 원본 템플릿이나 기존 수리 버프는 수정하지 않습니다.
- 반동 제어는 부착물 반동 배율을 반영한 무기 반동 성분에 적용합니다. 게임의 별도 카메라 반동·플레이어 스킬·전역 반동 보너스를 모두 같은 비율로 줄이는 기능은 아닙니다.
- 정밀성은 총알의 기본 탄 퍼짐과 산탄 패턴 퍼짐을 줄입니다. 조준 흔들림이나 탄도 낙차·탄속은 바꾸지 않습니다.
- 방어구 내구성은 총격·폭발로 차감되는 내구도를 줄입니다. 방어 등급이나 캐릭터가 받는 직접 피해는 바꾸지 않습니다. 본체의 효과는 장착된 방탄판·바이저에도 적용하며 중첩하지 않습니다.
- 기동성은 장비의 음수 페널티만 완화합니다. 다른 모드가 넣은 양수 보너스는 줄이지 않습니다.

## 사용 흐름

1. 장비를 장착 해제하고 창고에 넣습니다. 내부 보관칸에 일반 아이템이 있으면 먼저 비웁니다.
2. 우클릭 → **장비 강화** → 강화 방향을 고릅니다.
3. 현재·성공 후 단계와 효과, 성공률, 비용, 보유 루블, 실패 결과를 확인합니다.
4. **강화 시도**를 누릅니다. 견적은 60초 동안 유효하며 만료되면 새로 불러옵니다.
5. 성공하면 단계와 선택 방향을 저장하고 클라이언트에 효과를 적용합니다. 실패하면 설정한 손실만 처리하며 단계·방향은 유지합니다. 파괴 옵션은 본체와 부착물 전체 삭제입니다.

100% 성공도 비용을 소모합니다. 장착 중인 장비·레이드·상인 판매품·타인의 아이템에는 강화를 허용하지 않습니다. 창 뒤의 인벤토리 마우스 클릭을 차단하도록 구현했습니다.

**강화 방향 변경:** 다른 방향 카드를 선택하면 단계는 그대로 두고 **별도 비용으로 100% 변경**합니다. 비용은 현재 단계의 일반 강화 견적과 같은 공식입니다. 이전 효과를 대체하며 여러 방향을 동시에 얻지 않습니다. 최대 단계에서도 방향 변경은 가능합니다. 창의 버튼과 안내도 “방향 변경”으로 바뀝니다.

**0.1.0 기록 이전:** 예전 프로토타입에서 지불한 단계가 있고 방향 기록이 없으면, 처음 한 번 **단계를 유지한 채 무료·100%로 효과를 선택**합니다. 최대 +10인 기존 아이템도 다시 비용을 내거나 단계를 올릴 필요가 없습니다.

## F12 설정

BepInEx ConfigurationManager가 설치되어 있으면 F12의 **Gear Enhance**에서 조절합니다. 파일은 `BepInEx/config/rf.gearenhance.cfg`입니다.

| 설정 | 기본값 |
|---|---|
| 성공률 / 항상 성공 | 75% / OFF |
| 최대 강화 단계 | +10 |
| 기준 가격 | 북밸류, 플리 기준가도 선택 가능 |
| 비용 비율 / 단계별 배수 / 최소 비용 | 10% / 1.25배 / 1,000 루블 |
| 실패 처리 | 비용만 소모 |
| 효과별 단계 증가량·상한 | 위 능력치 표와 같음 |

확률·비용 설정을 바꾸면 기존 견적을 무효화하고 새로 확인합니다. **효과의 증가량·상한은 이미 강화된 아이템에도 반영**하며, 들고 있는 무기와 장착 방어구의 계산값도 갱신하도록 연결했습니다. 이 실시간 갱신의 인게임 확인은 남아 있습니다. 최대 단계만 올려도 효과별 상한은 별도로 유지됩니다.

비용은 `최대(최소 비용, 본체 기준가 × 비용 비율 × 배수^현재 단계)`를 올림한 값입니다. 1회 상한은 1억 루블이며 부착물 가격은 제외합니다. 기준가 10만 루블이면 +0→+1은 1만, +1→+2는 12,500 루블입니다. 플리 기준가는 SPT의 템플릿별 기준가이며 실시간 최저 매물가가 아닙니다. 가격이 없으면 북밸류로 대체하고 창에 표시합니다.

실패 옵션:

- `KeepItem`: 비용만 소모하고 장비·단계·방향 유지.
- `ZeroDurability`: 본체와 내구도가 있는 부품의 현재 내구도 0. 최대 내구도·단계·방향 유지.
- `Destroy`: 본체·부착물·탄창·탄약 삭제 및 보험 등록 제거. 별도 확인란이 필요하며 되돌리기는 없습니다.

## 저장과 동기화

단계·방향·최근 100회 결과는 SPT 프로필 최상위 `rfGearEnhance`에 아이템 ID별로 보관합니다. 별도 파일을 만들지 않으며 일반 SPT 프로필 저장 주기를 사용합니다. 프로필 로드 완료 시 서버에서 현재 소유한 장비의 기록을 받아 효과를 복원하고, 강화 후에는 서버 결과와 다시 동기화합니다. 읽기 전용 동기화를 완료하지 못하면 새 견적을 막아 서로 다른 모드 버전에서 잘못 비용을 내지 않도록 합니다.

판매·분해·재조합으로 아이템 ID가 바뀌면 자동 이전하지 않습니다. 프로필 전환 시 이전 캐시를 비우며 Fika·다중 사용자 환경은 지원 범위에 포함하지 않았습니다.

## 빌드와 검사

필요한 도구는 **.NET SDK 10, Python 3.12+, 실제 SPT 4.1.6 설치본**입니다. Python 외부 패키지는 빌드 스크립트에 필요하지 않습니다.

```powershell
python tools/build.py --spt-root "E:\SPT 4.1"
```

패키지는 `Build/GearEnhance-0.2.0.zip`입니다. **게임 폴더에 복사하거나 서버·게임을 실행하지 않습니다.** IDE에서는 `LocalSettings.props.example`을 `LocalSettings.props`로 복사하고 `SptRoot`를 본인 경로로 바꿉니다. 개인 경로 파일은 Git에서 제외합니다. 클라이언트와 서버는 같은 버전으로 사용해야 합니다.

클라우드의 컴파일 전용 참조 환경에서는:

```bash
source /workspace/.onboarding/dotnet-env.sh
GEAR_GAME_ASSEMBLY=/workspace/Cluade_For_spt/references/Assembly-CSharp.dll \
  python3 tools/build.py --spt-root /workspace/.gear-reference --validate-only
```

단독 검사:

```text
dotnet test tests/GearEnhance.Tests.csproj -c Release
dotnet test client-tests/GearEnhance.ClientTests.csproj -c Release -p:SptRoot="E:\SPT 4.1"
```

`GEAR_GAME_ASSEMBLY`를 실제 `Assembly-CSharp.dll` 전체 경로로 지정하면 게임 API·계산 경로 대조 5개도 실행합니다. 경로가 없으면 이 5개만 생략하며, 클라이언트 검사에는 BepInEx의 실제 Harmony DLL이 필요합니다. 모든 복원은 고정한 lockfile을 사용합니다.

## 확인한 것과 남은 것

- 서버·클라이언트 빌드 경고·오류 0, **검사 55개 통과·생략 0**.
- 서버·공통 검사 46개: 비용·실패·중복 처리, 실제 SPT 차감·삭제 도우미, 방향 변경·기존 단계 이전, 기록 복원, 효과 계산·상한·프로필 분리, 실제 게임 API와 호출 경로.
- 클라이언트 검사 9개: 컴파일한 실제 효과 함수를 직접 실행하여 다섯 효과, 아이템 분리, F12 재조정, 부모 순환 종료, 타입이 있는 세션 콜백 연결을 검사했습니다. 실제 페널티 변환 코드로 실행 가능한 명령을 만들어 결과와 템플릿 보존도 확인했습니다.
- 클라우드 컴파일은 BepInEx 5.4.23.2와 UnityEngine.Modules 2021.3.33 참조를 사용했습니다. 게임 참조 스텁은 만들지 않았습니다. 검사 전용 아이템 객체는 게임 플러그인·패키지에 들어가지 않습니다.
- **미검증:** 실제 Unity 2022 게임 모듈로 만든 배포 빌드, 화면·한글·장비 이미지·입력 차단, 게임에서의 HTTP·패치 설치·재접속·반동·명중·방어구 피격·F12 갱신, 다른 모드와의 조합. 메타데이터와 모의 실행 검사는 실제 플레이 검증을 대신하지 않습니다.

실제 적용 확인은 별도 테스트 프로필과 장비 사본으로 진행해야 합니다. 원본 수리 버프를 건드리지 않는 구조지만 다른 모드가 같은 계산 지점을 변경하면 별도 대조가 필요합니다.

## 참고

- [Klamist/RepairSkillExtension](https://github.com/Klamist/RepairSkillExtension): 수리·서버 아이템 변경 경로 조사에 참고했습니다. 설치 의존성이 아니며 그 모드의 수리 이벤트를 교체하지 않습니다.
- [Transmog 포팅 기록](https://github.com/danyhappy564-cmyk/Cluade_For_spt/blob/main/projects/Transmog-zzap--Bootleg-/NOTES.md): SPT 4.1 동적 메뉴 API 참고.
- [설계와 적용 지점](docs/DESIGN.md)
