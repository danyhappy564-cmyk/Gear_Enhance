# Gear_Enhance v1.4.0

**태그:** `1.4.0` 예정(이번 작업에서는 생성하지 않았습니다). 코드 기준: `codex/release-1.4.0` / main `4f0ad53`. 최종 태그 대상 커밋은 게시 전에 확정해야 합니다.

**이전 릴리즈:** v1.3.0 (사용자 지정 비교 기준, 로컬 태그 `1.3.0` 이후 코드 기준까지 커밋 37개) → **이번: v1.4.0** (탭 화면·신화 강화·보험 개선·프레스티지 보험)

**초안 확인 범위:** 수치는 현재 소스와 변경 이력을 대조했습니다. 사용자께서 프레스티지 탭·보험 탭이 "잘 된다"고 알려주셨습니다(2026-10-26). 아래 기능 설명 전체가 게임 검증을 마쳤다는 뜻은 아닙니다. 이번 문서 작업에서는 빌드·자동 검사·배포 ZIP 생성·게임 실행을 하지 않았고, GitHub의 최신 게시 릴리즈도 별도로 조회하지 않았습니다.

**<변경점>**

**1. 강화·진화·보험을 탭 화면으로 정리했습니다**

- 기존 여러 창에 나뉘었던 기능을 일반 강화 · ★★성 · ★★★성 · 초월 · ???/신화 · 보험 · 프레스티지 · 도움말 탭으로 모았습니다. 오른쪽에는 원본/강화 후 장비 상태를 비교하고, 아래에는 시도 버튼을 고정했습니다.
- Noto Sans KR·Rajdhani 글꼴, 항목 카드·체크박스·아이콘·결과 팝업을 적용했습니다. **설치할 때 `fonts` 폴더도 함께 넣어 주세요.**
- 무기의 오른쪽 **탄 비교** 탭은 약실 탄을 먼저, 없으면 탄창 맨 위 탄을 찾아 데미지·관통력과 명중률·반동·내구도 소모·과열·불발·고장 값을 **원래 / 지금 / 다음 신화 단계**로 보여 줍니다. 탄이 없으면 안내만 보이고 해당 탄에 없는 값은 숨깁니다.
- 재료를 고를 때 견적 숫자가 번쩍이던 현상, 모듈형 방어구의 방어등급·보호 부위 표시, 긴 결과 문구와 보험·프레스티지 설명 잘림을 수정했습니다. 프레스티지·보험 탭 외 화면 전체와 영어 잘림은 이번 작업에서 게임 확인하지 않았습니다.

**2. 재료로 성공률을 올리고, 충전을 건너뛸 수 있습니다**

- 예전 비트코인 체크 대신 아래 재료 중 **한 번에 최대 2종**을 골라 개수를 조절합니다. 일반 강화·2성/3성 진화·초월·신화 강화에 적용하며 최종 성공률은 최대 100%입니다.

| 재료 | 1개당 상승 | 재료별 최대 상승 / 개수 | 1개당 추가 루블 |
|---|---|---|---|
| 독택 | +0.1%p | +5%p / 50개 | 5,000 ₽ |
| 그래픽 카드 | +1%p | +10%p / 10개 | 100,000 ₽ |
| 비트코인 | +5%p | +20%p / 4개 | 300,000 ₽ |

- 실제 시도 때 성공·실패와 관계없이 선택한 재료와 추가 루블을 소모합니다. 강화에 필수인 재료를 빼고 남은 창고 재료를 사용하며 인레이드 여부는 무관합니다.
- **즉시 강화**는 일반·초월·신화 강화에서 **50,000 ₽**를 더 내고 충전만 건너뜁니다. 성공률·실패 규칙은 같습니다. 2성/3성 진화·???에는 사용할 수 없고, ???에는 성공률 재료도 사용할 수 없습니다.

**3. 방어구 +6 위험과 파괴 방지권을 추가했습니다**

- 방어구도 무기처럼 **선택 항목 +6 시도부터** 고위험 규칙을 적용합니다. 기본 설정에서 실패 중 25%는 파괴, 나머지 75%는 선택 항목 -1입니다. 예를 들어 기본 성공률 50%인 +6 시도는 전체 확률로 성공 50%·파괴 12.5%·하락 37.5%입니다.
- 일반 강화 보호비는 `기본 강화비 × 시도 단계² × 계수`이며, 계수를 파괴 **50% → 10%**, 하락 **35% → 7%**로 내렸습니다. 보호는 해당 손실만 막으며 성공률은 올리지 않습니다.
- 진화·???에는 장비별 **평생 구매 한도**가 있는 전용 파괴 방지권을 사용합니다.

| 도전 | 1장 가격 | 장비별 평생 한도 |
|---|---|---|
| 1성 → 2성 | 1,000,000 ₽ | 3장 |
| 2성 → 3성 | 2,700,000 ₽ | 2장 |
| 3성 → ??? | 5,000,000 ₽ | 1장 |

- 사용을 선택하면 성공·실패와 관계없이 시도마다 1장을 소모하고, 실패해도 장비와 기존 단계를 지킵니다. 해당 진화·???에 성공하면 남은 전용 방지권은 환불 없이 사라지고 다음 도전으로 넘어가지 않습니다.
- 초월은 4·5단계 도전 실패 시 1단계 하락하며 **4단계 도전만** 하락 방지를 쓸 수 있습니다. 신화는 3·4·5단계 도전 실패 시 1단계 하락하며 **3단계 도전만** 방지할 수 있습니다. 하락 방지 추가비는 기본 강화비와 같습니다.

**4. 3성 다음에 ???와 신화 강화가 열립니다**

- 3성 진화를 마친 장비는 **800만 ₽ + LEDX 1개**로 ???에 도전합니다. 기본 성공률은 **50%**이며, 성공하면 **신화 0단계**, 실패하면 파괴입니다. ??? 전용 방지권을 쓰면 실패해도 3성를 유지합니다. 시도마다 확인 창이 뜹니다.
- 신화는 최대 **5단계**입니다. 1~5단계 기본 성공률은 **40/30/20/15/10%**, 실패마다 다음 확률 **+3%p**, 천장은 **+30%p**입니다. 처음 비용은 **500만 ₽ + LEDX 1개**, 루블은 단계마다 **×1.1**입니다. 신화 단계 강화 자체는 실패해도 파괴되지 않습니다.
- **신화 무기:** 그 총으로 쏜 탄의 데미지·관통력 **단계당 +3%, 5단계 +15%**. 탄의 불리한 명중률·반동·내구도 소모·과열·불발·고장 값은 **단계당 16%, 5단계 80%** 줄입니다. 유리한 값과 다른 총·창고의 같은 탄은 그대로이며 스타 배율을 받지 않습니다.
- **신화 방어구:** 방탄복은 받는 피해 **단계당 -10%, 최대 50%**이며 머리 명중은 절반만 줄입니다. 헬멧·가면·눈 보호구 같은 머리 방어구는 **단계당 -5%, 최대 25%**입니다. 여러 개를 입어도 감소율이 가장 큰 하나만 적용합니다. 타격 피해가 대상이며 낙하·출혈·탈수 등은 그대로입니다.
- 방어구는 ??? 성공 시 기존에 보호하지 않던 부위 중 무작위로 **최대 2곳**을 추가합니다. 해당 장비의 가장 좋은 소프트 아머(방탄판 제외)가 막습니다. 바꿀 부위 카드를 골라 다시 뽑을 수 있으며 첫 비용은 **200만 ₽ + 그래픽 카드 1개**, 이후 장비별 횟수마다 루블 **×1.5**입니다.
- 신화 효과는 **내 캐릭터 전용**이며 봇이 주워 쓰거나 입어도 적용되지 않습니다. 신화 전투 효과·추가 부위·다시 뽑기 전체 동작은 이번 작업에서 게임 확인하지 않았습니다.

**5. 강화 보험: 프리미엄 99%, 반환 시간과 차액 재가입**

| 등급 | 강화된 채 돌아올 확률 | 보험료(강화 가치 대비) | 성공 판정 후 반환 시간(실제 시간) |
|---|---|---|---|
| 일반 | 20% | 5% | 24시간 |
| 고급 | 55% | 15% | 12시간 |
| 프리미엄 | 99% | 30% | 즉시 |

- 프리미엄을 **90% → 99%**로 올렸습니다. 보험 탭에 돌아오는 중인 장비의 남은 시간을 표시하고 프리미엄은 `즉시`로 보입니다. 이미 가입한 보험의 확률은 가입 당시 값으로 고정됩니다.
- 레이드 분실 뒤 반환 판정에 성공하면 강화·스타·초월·신화·방탄판 등록을 유지한 장비와 슬롯 부품이 돌아옵니다(탄약·주머니 물건 제외). 상인 보험과 중복 반환하지 않습니다. 실패하거나 강화 보험이 없으면 강화가 사라지고, 상인 보험은 +0 장비만 돌려줍니다.
- 강화 성공·더 비싼 방탄판 차액 납부로 보험이 풀리면 결과 창과 보험 탭에서 **다시 가입**을 안내합니다. 재가입·상위 등급 변경은 **새 보험료 − 이미 낸 보험료**만 냅니다(최저 0 ₽). 레이드 반환 때 납부 기록은 끝납니다.
- 보험이 풀린 장비가 있으면 탭이 **금색**으로 빛나고 `다시 가입 N개`가 보입니다. 등록 목록에서 현재 창에 올린 장비가 아니어도 해당 줄에서 갱신할 수 있습니다.
- 확인 상태: 사용자가 보험 탭이 잘 된다고 알려주셨습니다(2026-10-26). 24·12시간 경과와 실제 반환 확률 전체를 확인했다는 뜻은 아닙니다.

**6. 프레스티지 보험: 다음 단계에서도 강화 장비를 받습니다**

- 프레스티지 탭에서 **강화 가치의 10%**를 내고 등록합니다. **프레스티지를 달성해 다음 단계로 넘어갈 때만** 강화·1성·초월·신화·부착물·방탄판을 보존한 장비가 시스템 우편으로 돌아옵니다. **레이드에서 죽거나 잃은 장비는 보상하지 않습니다.**
- 추가 강화·방탄판 교체에도 등록은 유지됩니다. 가치가 오르면 차액만 내고, 내려가도 환불하지 않습니다. 차액을 내지 않으면 **마지막으로 납부했을 때 보장한 강화 기록**으로 돌아옵니다.
- 반환하면 등록이 끝나므로 다음 프레스티지에서도 받으려면 다시 등록합니다. 최대 프레스티지(현재 **6**)에서는 등록할 수 없습니다. SPT 기본 이전으로 선택한 장비는 한 번만 돌아오고 강화가 이어집니다. 개발자 옵션을 연 동안 한 등록은 이전하지 않습니다.
- 받을 우편이 있으면 탭이 **분홍색**, 낼 차액이 있으면 **금색**으로 숨 쉬듯 빛납니다. 등록된 장비 목록에서 다른 장비도 차액을 내고 갱신할 수 있습니다.
- 확인 상태: 사용자가 프레스티지 탭이 잘 된다고 알려주셨습니다(2026-10-26). 실제 프레스티지 이전과 반환 우편 수령까지 확인한 것으로 확대하지 않았습니다.

**7. 표식·연출·기록 보호와 기타 수정**

- 창고·장비 칸에서 진화 장비는 2성·3성 별 표식, 신화 장비는 **신화 표식**과 분홍↔금빛 테두리로 표시합니다. 이름 아래에는 `[신화 V · 초월 X]`처럼 단계를 로마 숫자로 표시합니다(초월은 두 트랙 합). 강화 창·결과 팝업은 숫자를 사용합니다.
- ??? 확인·충전·신화 달성·신화 상승 효과음 4종과 신화 결과 연출을 추가했습니다. 일반·초월·신화 충전은 앞 무음 0.765초를 잘라 약 **3.647초**, 진화·???는 약 **4.412초**입니다. 효과음은 DLL에 포함합니다.
- F12에 **연출 줄이기·창고 신화 칸 반짝임·파괴 확인 창**을 추가했습니다. 일반 파괴 확인은 창을 닫을 때까지 생략할 수 있지만 ???는 매번 확인합니다. 자세한 로그는 기본 OFF입니다.
- 프로필의 강화 기록을 직접 고치면 다음 서버 시작 때 마지막 정상 기록으로 복원합니다. 모드 자체를 수정하는 것까지 막지는 못합니다.
- 개발자 옵션을 연 동안 한 강화는 다시 잠그거나 재시작하면 되돌아갑니다(사용한 루블·재료와 파괴된 장비는 복구되지 않습니다). 별도 저장 비밀번호도 있습니다. 비밀번호는 공개하지 않습니다.
- 신화 칸 테두리 초기화 오류, 저장 내구도가 아직 없는 새 장비의 강화 거절, 글꼴 읽기와 긴 부위명 잘림도 수정했습니다. 이번 작업에서 빌드·게임 재검증은 하지 않았습니다.

**알려진 점**

- 봇에는 신화 효과가 없습니다. Fika·다중 사용자 환경은 지원 범위가 아닙니다.
- 상인에게 판 장비와 판매·분해·재조합 등으로 바뀐 아이템 ID에는 기존 기록이 이어지지 않습니다.
- 기존 README에는 LootInstantSearch·ManimalTerminal을 함께 사용할 때 서로를 계속 호출해 레이드 사격 중 종료된 사례가 기록되어 있습니다. 해당 조합에서 같은 문제가 나면 둘 중 하나를 빼 주세요. 이번 작업에서 재현하지는 않았습니다.
- 프레스티지 보험은 레이드 분실 보상이 아니며, 강화 보험도 100% 반환을 보장하지 않습니다.
- 이번에 알려주신 게임 확인 범위는 프레스티지·보험 탭입니다. 나머지 기능 전체, 영어 화면, 타 모드 조합·장시간 성능은 미검증입니다. 이전 버전의 빌드·검사 통과 수치를 1.4.0 결과로 옮기지 않았습니다.
- 이상이 있으면 프로필 백업을 보관하고 클라이언트·서버를 같은 버전으로 맞춰 주세요. 1.3.0에는 신화·프레스티지 보험이 없으며, 1.2.0 이하에서는 초월·늘어난 최대 내구도 처리가 쓰이지 않습니다. 구버전에서 새 기록 보존은 보장하지 않습니다.

**설치 / 업데이트**

1. SPT **4.1.6**을 사용합니다. 서버·게임을 종료하고 프로필을 백업합니다.
2. 배포 파일 `GearEnhance-1.4.0.zip`을 SPT 폴더에 풀어 **두 폴더를 함께** 덮어씁니다: `BepInEx/plugins/GearEnhance/` + `SPT_Runtime/user/mods/GearEnhance/`.
3. **클라이언트와 서버를 모두 1.4.0으로** 교체하고, `BepInEx/plugins/GearEnhance/fonts/`의 글꼴과 라이선스 파일도 함께 설치합니다. 없으면 예전 글꼴로 보입니다.
4. 서버·게임을 다시 시작합니다. **통신 버전 12**라 한쪽만 바꾸면 강화 창이 "둘 다 업데이트" 안내로 진행을 막습니다. 이 안내로 저장 기록이 지워지지는 않습니다. **1.3.0의 강화 단계·스타 진화·초월 기록은 보존합니다.**

[![Buy Me a Coffee](https://img.shields.io/badge/Buy%20Me%20a%20Coffee-후원하기-FFDD00?logo=buymeacoffee&logoColor=black)](https://buymeacoffee.com/real_feels)

후원: https://buymeacoffee.com/real_feels

------------------------------------------------------------------------------------------------------------------------------------------------------

**<Changes (English)>**

**Draft status:** Proposed tag: `1.4.0` (not created in this task). Code baseline: `4f0ad53` on main / `codex/release-1.4.0`; confirm the final tag commit before publishing. Compared with the user-specified v1.3.0 baseline: **37 local commits**. The latest published GitHub release was not independently queried. The user reported that the Prestige and Insurance tabs work well on **2026-10-26**. This does not establish that every feature below has been tested in game. No build, automated tests, release ZIP creation or game launch was performed in this documentation task.

**1. Enhancement, evolution and insurance in one tabbed window**

- The left tabs cover Enhancement, 2nd Star, 3rd Star, Transcendence, ???/Mythic, Insurance, Prestige and Help. The right panel compares base/enhanced gear stats, with the attempt button fixed at the bottom.
- Added Noto Sans KR and Rajdhani fonts, cards, checkboxes, icons and result popups. **Install the `fonts` folder too.**
- The weapon **Ammo** tab uses the chambered round first, otherwise the magazine's top round. It compares **original / current / next mythic level** damage, penetration, accuracy, recoil, durability burn, heat, misfire and malfunction values. Missing properties are hidden; an empty gun shows a message.
- Fixed flickering quotes when selecting materials, modular armor class/zone displays and clipped long text, including insurance/prestige descriptions. Other screens and English text clipping were not checked in game during this task.

**2. Material chance boosts and instant attempts**

- Select **up to two kinds per attempt** for enhancement, either star evolution, transcendence or mythic leveling. Final chance is capped at 100%.

| Material | Boost per item | Maximum boost / count | Extra roubles per item |
|---|---|---|---|
| Dogtag | +0.1 pp | +5 pp / 50 | 5,000 ₽ |
| Graphics card | +1 pp | +10 pp / 10 | 100,000 ₽ |
| Bitcoin | +5 pp | +20 pp / 4 | 300,000 ₽ |

- Chosen materials and extra roubles are consumed once on the actual attempt, whether it succeeds or fails. Required recipe materials are reserved first; usable stash materials do not need found-in-raid status.
- **Instant** costs **50,000 ₽** to skip charging for enhancement, transcendence or mythic leveling. Chances and failure rules stay the same. Star evolution and ??? cannot use Instant; ??? also cannot use chance materials.

**3. Armor +6 risk and destruction tickets**

- Armor now follows the weapon high-risk rules from an attempt at **+6 in the selected track**: by default, 25% of failures destroy the gear and 75% drop that track by one. With a 50% success chance, that means 50% success, 12.5% destruction and 37.5% downgrade overall.
- Regular protection costs `base attempt fee × target level² × coefficient`. Destruction protection drops from **50% to 10%**, downgrade protection from **35% to 7%**. Protection prevents its named loss and does not raise the chance.

| Attempt | Price per ticket | Lifetime purchases per gear |
|---|---|---|
| 1st → 2nd star | 1,000,000 ₽ | 3 |
| 2nd → 3rd star | 2,700,000 ₽ | 2 |
| 3rd star → ??? | 5,000,000 ₽ | 1 |

- Selecting a ticket consumes one on **every attempt, success or failure**. A failure keeps the gear and its previous levels. Success removes remaining tickets for that evolution/??? without a refund; they do not carry into the next evolution. These are lifetime limits, not refillable stock limits.
- Transcendence attempts at levels 4 and 5 drop one level on failure; protection is available **only when attempting level 4**. Mythic attempts at levels 3–5 drop one level on failure; protection is available **only when attempting level 3**. Downgrade protection adds the base attempt fee.

**4. ??? and mythic progression after the 3rd star**

- Gear that completed the 3rd-star evolution can try ??? for **8,000,000 ₽ + one LEDX**, with a **50%** base chance. Success unlocks mythic level 0; failure destroys the gear unless a ??? ticket is used. A protected failure keeps the 3rd star. Every ??? attempt requires confirmation.
- Mythic leveling has **five levels**, with base chances **40/30/20/15/10%**, **+3 pp** pity per failure, capped at **+30 pp**. The first attempt costs **5,000,000 ₽ + one LEDX**; the rouble fee grows **×1.1** per level. Mythic leveling failures do not destroy the gear.
- **Weapons:** bullets fired from that gun gain **+3% damage and penetration per level, +15% at level 5**. Bad ammo accuracy, recoil, durability burn, heat, misfire and malfunction values are reduced by **16% per level, 80% at level 5**. Beneficial values and the same ammo in another gun or the stash stay unchanged. Star multipliers do not apply.
- **Armor:** body armor reduces damage taken by **10% per level, up to 50%**, halved for head hits. Head gear such as helmets, masks and eyewear gives **5% per level, up to 25%**. Only the strongest worn reduction applies. It covers impact damage; falls, bleeding and dehydration remain unchanged.
- Passing ??? on armor adds **up to two random previously unprotected zones**, handled by its best non-plate soft armor. Select a zone card to reroll it for **2,000,000 ₽ + one graphics card** initially; the rouble fee grows **×1.5** per reroll on that gear.
- Mythic effects apply **only to the local player**, not bots using the gear. Combat effects, extra zones and rerolls were not tested in game during this task.

**5. Enhancement insurance: Premium 99%, return times and renewal credit**

| Tier | Chance to return enhanced | Fee (% of enhancement value) | Real time after a successful return roll |
|---|---|---|---|
| Basic | 20% | 5% | 24 hours |
| Advanced | 55% | 15% | 12 hours |
| Premium | 99% | 30% | Immediate |

- Premium rises from **90% to 99%**. The tab shows remaining return time; Premium reads `Immediate`. Existing policies keep the chance recorded at purchase.
- A successful raid-loss return preserves levels, stars, transcendence, mythic data, plate registration and slotted parts, excluding ammo and pocket contents. Duplicate trader returns are removed. Failed rolls or uninsured losses remove enhancement; trader insurance alone returns +0 gear.
- Successful upgrades or paid plate differences end coverage and show a renewal notice. Renewal or an upgrade to a higher insurance tier costs **new fee − already paid fee**, with a minimum of zero. Payment credit ends when gear returns from a raid loss.
- The tab glows **gold** for lapsed gear and shows a renewal count. Renew other gear directly from its row in the registered gear list.
- Status: the user reported that the Insurance tab works well on 2026-10-26. This does not verify all return probabilities or 12/24-hour waits.

**6. Prestige insurance: keep enhanced gear across the next prestige**

- Register gear in the Prestige tab for **10% of enhancement value**. It is mailed back with levels, stars, transcendence, mythic data, attachments and plates **only when advancing to the next prestige stage**. **It does not cover raid losses.**
- Registration stays after further upgrades or plate swaps. Pay only the increase in the fee; lower values do not produce refunds. Without paying the increase, returned enhancement records are limited to the state covered at the **last payment**.
- Registration ends after the return, so register again for another prestige. Registration is unavailable at the maximum prestige stage, currently **6**. Gear selected for SPT's native transfer is returned once with its enhancements. Registrations made while developer options are unlocked do not transfer.
- The tab pulses **pink** for pending return mail and **gold** for unpaid fee increases. Update other gear from the registered gear list.
- Status: the user reported that the Prestige tab works well on 2026-10-26. A complete prestige transition and collecting its return mail were not separately confirmed.

**7. Markers, effects, record protection and other fixes**

- Evolved gear uses two- or three-star markers; mythic gear uses a **Mythic marker** and a slowly shifting pink/gold border. A line such as `[Mythic V · Trans. X]` shows Roman numeral levels in stash/gear slots; transcendence is the total of both tracks. Windows and result popups use regular numbers.
- Added four synthesized sounds for ??? confirmation/charging and mythic unlock/level-up, plus mythic result effects. Trimming 0.765 seconds of leading silence makes regular/transcendence/mythic charging about **3.647 seconds**; evolution/??? remains about **4.412 seconds**. Sounds are embedded in the DLL.
- F12 adds reduced effects, mythic stash-cell glow and destruction confirmation settings. Normal destruction confirmations can be skipped until the window closes; ??? always asks. Detailed logging is off by default.
- Hand-edited enhancement records are restored to the last valid state at the next server start. This does not prevent modification of the mod itself.
- Enhancements made with developer options unlocked roll back on locking or restarting. Spent roubles/materials and destroyed gear are not restored. A separate developer save password also exists; passwords are not published.
- Fixed mythic border initialization errors, rejection of new gear without stored durability, font loading and clipped long zone names. These changes were not rebuilt or retested in game during this task.

**Known issues**

- Bots do not receive mythic effects. Fika and multiplayer are outside the supported scope.
- Records do not follow gear sold to traders or IDs changed by selling, dismantling or rebuilding.
- The existing README records a raid shooting crash caused by LootInstantSearch and ManimalTerminal repeatedly calling each other. Remove one if the same issue occurs with that combination. It was not reproduced in this task.
- Prestige insurance does not cover raid loss, and enhancement insurance is not a guaranteed return.
- The reported in-game confirmation covers the Prestige and Insurance tabs. Full feature coverage, English layouts, other mod combinations and long sessions remain unverified. Older build/test results are not presented as 1.4.0 results.
- Keep a profile backup and match client/server versions when rolling back. Version 1.3.0 has no mythic/prestige insurance support; 1.2.0 and earlier ignore transcendence and added maximum durability. Preservation of new data by older versions is not guaranteed.

**Install / update**

1. Use SPT **4.1.6**. Stop the server and game, then back up your profile.
2. Extract the release archive `GearEnhance-1.4.0.zip` into the SPT folder, overwriting **both** `BepInEx/plugins/GearEnhance/` and `SPT_Runtime/user/mods/GearEnhance/`.
3. Update **both client and server to 1.4.0**. Include the fonts and license files in `BepInEx/plugins/GearEnhance/fonts/`; missing fonts fall back to the older font.
4. Restart the server and game. **Protocol 12** blocks mixed versions with an "update both" message, without deleting stored records. **Enhancement levels, star evolutions and transcendence records from 1.3.0 are preserved.**

^^7

[![Buy Me a Coffee](https://img.shields.io/badge/Buy%20Me%20a%20Coffee-donate-FFDD00?logo=buymeacoffee&logoColor=black)](https://buymeacoffee.com/real_feels)

donate: https://buymeacoffee.com/real_feels
