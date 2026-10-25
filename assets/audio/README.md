# 강화 음원

## 1.4.0 합성 음원 (synth/, 아직 게임에 연결 안 됨)

??? 도전·신화 단계용 소리 4개는 사용자 요청으로 **이 저장소의 스크립트로 합성**했습니다(외부 음원·샘플 없음). `python3 tools/make_sounds.py`를 다시 실행하면 같은 파일이 나옵니다. 48kHz 스테레오 PCM16이고, 크기는 아래 사용자 음원과 비슷하게 맞췄습니다. 1.4.0 구현 때 `src/Client/Audio/`로 옮겨 DLL에 넣습니다. 쓰임새는 [docs/DESIGN-1.4.0.md](../../docs/DESIGN-1.4.0.md) 7절.

| 파일 | 재생 시점 | 길이 |
|---|---|---|
| synth/myth_confirm.wav | ??? 확인 창이 열릴 때 | 1.900초 |
| synth/myth_charge.wav | ??? 진행 충전(게이지) | 4.411791667초 (charge.wav와 같은 211,766프레임) |
| synth/myth_success.wav | 신화 달성 | 3.800초 |
| synth/myth_up.wav | 신화 단계 상승 | 1.500초 |

## 사용자 제공 강화 음원

2026-10-22 사용자가 이 프로젝트에 연결하도록 제공한 MP3 여섯 개입니다. 합성 음원이나 다른 게임에서 추출한 파일로 교체하지 않았습니다. 코드의 MIT 라이선스를 이 음원에 새로 부여하지 않습니다. 원본 권리자·별도 라이선스 정보는 제공되지 않았습니다.

| 원본 첨부 이름 | 보존한 원본 | DLL 내 리소스 | 실제 재생 길이 |
|---|---|---|---|
| 강화중소리.mp3 | source/charge.mp3 | GearEnhance.Audio.charge.wav | 4.411791667초 |
| 강화 성공할때.mp3 | source/success.mp3 | GearEnhance.Audio.success.wav | 1.000초 |
| 강화실패.mp3 | source/failure.mp3 | GearEnhance.Audio.failure.wav | 2.256초 |
| 파괴될때.mp3 | source/destroy.mp3 | GearEnhance.Audio.destroy.wav | 1.074520833초 |
| 스타 2 달성 시.mp3 | source/star2.mp3 | GearEnhance.Audio.star2.wav | 4.166984127초 |
| heart-beat-speeds-up.mp3 | source/heartbeat.mp3 | GearEnhance.Audio.evolution_charge.wav(혼합) | 4.411791667초 |

원본 MP3의 컨테이너 길이는 인코더 패딩을 포함할 수 있으므로 게이지는 디코딩한 PCM 프레임 수/샘플레이트를 사용합니다. 일반 강화/결과 음원5개는 원본 음색·채널·샘플레이트를 보존하며 편집하지 않았습니다. 심장박동만 사용자 요청으로 편집했습니다. 원본31.716916100초의 마지막4.411791667초(27.305124433초부터)를 잘라48kHz 스테레오로 변환하고, charge×0.8 + heartbeat×0.45로 혼합했습니다. 심장박동의 시작40ms/끝70ms를 페이드 처리해 잘린 경계를 완화했습니다. 일반 강화의 charge.wav는 바꾸지 않았습니다. 파괴 MP3에 포함된 PNG 표지와 메타데이터만 재생용 WAV에서 제외합니다.

변환은 각 원본을 대상으로 다음처럼 실행했습니다. 출력 파일은 src/Client/Audio/에 이미 들어 있으므로 일반 빌드에서 FFmpeg는 필요 없습니다.

```text
ffmpeg -v error -nostdin -i assets/audio/source/charge.mp3 -map 0:a:0 -vn -map_metadata -1 -c:a pcm_s16le -fflags +bitexact -flags:a +bitexact src/Client/Audio/charge.wav
```

success/failure/destroy/star2도 같은 옵션으로 변환했습니다. 심장박동의 정확한 FFmpeg 필터와 구간도 provenance.json에 기록합니다. 원본·출력 SHA256, 채널 수, 샘플 수는 [provenance.json](provenance.json)에 기록합니다. 테스트는 컴파일된 DLL의 실제 리소스 해시와 프레임 수를 검사합니다. Unity에서의 실제 청취·스피커 지연은 별도 확인 대상입니다. 게임 엔진 재생 시계 동기화는 하드웨어 출력 지연을 제거하는 기능은 아닙니다.
