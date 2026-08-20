# ChurchSubtitle OpenAI 실시간 한국어 자막 PoC

Windows 방송실 PC에서 24 kHz mono PCM 오디오를 OpenAI `gpt-live-transcribe`로 실시간 전송하고, 한 줄 자막 이벤트와 평가 자료를 남기는 .NET 8 PoC입니다. 번역과 `gpt-realtime-2.1` 음성 대화 기능은 사용하지 않습니다.

## 구성

- `ChurchSubtitle.Core`: 자막 이벤트, OpenAI WebSocket 공급자, 재접속, 지연 측정, CER 평가
- `ChurchSubtitle.Cli`: `transcribe`와 `evaluate` 명령
- `scripts/prepare-audio.ps1`: 권한이 확인된 YouTube 영상에서 테스트 음원 추출
- `scripts/run-transcription.ps1`: `low`, `medium` 또는 `high` 단일 실행
- `scripts/run-bakeoff.ps1`: 같은 음원으로 `low`/`medium` 순차 실행
- `scripts/evaluate.ps1`: 사람이 교정한 정답 자막과 결과 비교

## 1. 도구 설치

PowerShell에서 다음을 실행합니다. 도구는 프로젝트가 아니라 `%LOCALAPPDATA%\church-subtitle-tools`에 설치됩니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-tools.ps1
```

## 2. 테스트 음원 준비

원본 영상은 24:26 길이입니다. 먼저 사용 권한이 있는 소스인지 확인한 뒤 15분 평가 세트를 준비합니다. 평가 세트의 기본 구간은 `09:25–24:25`입니다. 링크가 가리킨 `17:25`부터는 약 7분만 남아 있어 15분 합격 테스트에는 사용할 수 없습니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\prepare-audio.ps1 `
  -ConfirmAuthorizedSource
```

생성 파일은 자동 삭제하거나 덮어쓰지 않습니다.

- `data/poc-source-15m/original.*`: 내려받은 원본 오디오
- `data/poc-source-15m/service-segment-48k-mono.flac`: 보관용 15분 FLAC
- `data/poc-source-15m/service-segment-24k-mono-s16le.pcm`: OpenAI 전송용 PCM
- `data/poc-source-15m/source-metadata.json`: 출처 URL, 구간, 권한 확인 시각

17:25부터 끝까지 약 7분만 별도로 만들려면 다음처럼 실행합니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\prepare-audio.ps1 `
  -Start 00:17:25 -Duration 00:07:00 -OutputDirectory data\poc-source-7m `
  -ConfirmAuthorizedSource
```

로컬 영상·자막 UI에는 원본의 `00:00–24:26` 전체 구간 PCM이 필요합니다. 위 단계에서 권한 확인 후 내려받은 기존 `data\poc-source-15m\original.*`을 다시 사용하면 원본을 재다운로드하지 않아도 됩니다. 출력 파일이 이미 있으면 아래 명령은 덮어쓰지 않습니다.

```powershell
$source = Get-ChildItem .\data\poc-source-15m -Filter 'original.*' -File |
  Select-Object -First 1
$ffmpeg = Get-ChildItem "$env:LOCALAPPDATA\church-subtitle-tools\media" `
  -Filter ffmpeg.exe -File -Recurse | Select-Object -First 1 -ExpandProperty FullName
New-Item -ItemType Directory -Force .\data\poc-source-full | Out-Null
& $ffmpeg -hide_banner -n -ss 00:00:00 -t 00:24:26 -i $source.FullName `
  -vn -ac 1 -ar 24000 -c:a pcm_s16le -f s16le `
  .\data\poc-source-full\service-full-24k-mono-s16le.pcm
```

웹 실행기가 사용하는 경로는 정확히 `data\poc-source-full\service-full-24k-mono-s16le.pcm`입니다. 이 파일은 헤더 없는 `24 kHz / 16-bit / mono PCM (s16le)`이며, 24:26 전체라면 70,368,000바이트입니다. 다른 소스를 사용하거나 길이가 다르면 영상 시간과 PCM 위치의 대응을 별도로 검증해야 합니다.

## 3. OpenAI 키와 전사

프로젝트 루트의 `.env` 파일을 열고 등호 뒤에 키를 입력합니다. 이 파일은 Git에서 제외되며 Windows 사용자·시스템 전역 환경변수를 변경하지 않습니다.

```dotenv
OPENAI_API_KEY=YOUR_OPENAI_KEY
```

저장한 뒤 실행합니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-transcription.ps1 -Delay low
```

이미 현재 PowerShell 프로세스에 `OPENAI_API_KEY`가 설정되어 있으면 그 값이 `.env`보다 우선합니다. 로더와 프로그램은 키 값을 콘솔이나 측정 로그에 출력하지 않습니다.

두 설정을 연속 비교하려면 예상 비용을 확인한 뒤 실행합니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-bakeoff.ps1 -ConfirmEstimatedCost
```

각 실행은 `runs/<시각>-<delay>/` 아래에 다음을 생성합니다.

- `events.jsonl`: `CaptionUpdate` partial/final 이벤트
- `final-arrival-journal.txt`: 강제 종료에도 남도록 final 도착 즉시 flush한 저널
- `final.txt`: 정상 종료 시 음성 시작 시각 순으로 정렬한 전사본
- `runtime-metrics.json`: 재접속 횟수와 모델이 실제로 제공한 지연 샘플

partial은 전체 자막 스트림 기준 최대 초당 5회 갱신하며 10초 commit ACK를 기다리지 않고 즉시 전달합니다. final만 ACK의 `item_id`와 연결한 뒤 commit 순서로 전달하고 도착 즉시 저널에 flush합니다. 이 순서는 단어 timestamp가 아니라 애플리케이션이 부여한 10초 segment ordinal입니다. 완료된 줄·지연 추적·commit 조정 상태는 즉시 제거하거나 최근 duplicate 방지 창으로 제한해, 장시간 예배에서도 세션 내부 상태가 발화 수에 비례해 계속 늘지 않습니다. 연결 장애 시 같은 OpenAI 공급자로 1초, 2초, 4초 간격으로 최대 세 번 재접속하며 공급자 자동 변경은 하지 않습니다. ACK 전인 현재 구간은 최대 480,000바이트를 메모리에 유지하므로 비탐색 live stream도 새 연결에서 구간 전체를 다시 보냅니다. ACK를 받은 구간은 되감지 않습니다. ACK가 네트워크에서 유실되면 재전송이 중복될 수 있으며, 재접속이 한 번이라도 발생한 실행은 성공으로 간주하지 않고 수동 자막 전환을 안내합니다.

현재 공개 API 동작에 맞춰 `wss://api.openai.com/v1/realtime?intent=transcription`으로 연결합니다. `gpt-live-transcribe`는 server VAD 값을 거부하므로 공식 manual-commit 형태인 `turn_detection: null`을 명시합니다. 입력을 100ms 속도로 유지하면서 오디오 바이트 기준 480,000바이트(10초)마다 `input_audio_buffer.commit`을 보내고, 마지막에 남은 오디오가 있을 때만 한 번 더 커밋합니다. 각 commit은 `input_audio_buffer.committed`의 `item_id` ACK를 최대 10초 기다린 뒤 다음 창으로 진행합니다. 스트림 종료 후에는 ACK로 연결된 모든 `conversation.item.input_audio_transcription.completed`를 공통 최대 10초 drain 동안 기다립니다. 이 모드에서는 VAD 음성 경계가 없으므로 최초 partial 및 발화 종료 후 final 지연 샘플을 임의 생성하지 않으며, 해당 지표는 비어 있을 수 있습니다.

세 번의 재접속도 실패하면 프로세스는 0이 아닌 종료 코드로 중단됩니다. v1에서는 이 신호를 보고 기존 수동 자막 입력으로 전환하며, 다른 STT 공급자로 자동 전환하지 않습니다.

## 4. 로컬 영상·자막 테스트 UI

위의 전체 PCM과 `.env` 키가 있는 상태에서 프로젝트 루트에서 실행합니다. 실행기는 .NET 8, 키, 고정 PCM 경로를 먼저 확인하고 로컬 서버를 시작합니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-web.ps1
```

콘솔에 표시되는 `http://127.0.0.1:5287`을 browser에서 엽니다. YouTube 플레이어를 원하는 위치로 옮긴 뒤 `시작`을 누르면 그 현재 재생 위치를 100ms PCM 프레임 경계로 내림 정렬하고, 대응하는 바이트부터 파일 끝까지 HTTP Range로 읽어 실시간 속도로 보냅니다. 시작 버튼을 누르는 순간부터 실제 OpenAI API 사용료가 발생합니다. browser가 YouTube iframe의 소리를 캡처하는 구조는 아니며, 준비된 전체 PCM을 영상 시각에 매핑해 별도로 전송합니다.

세션은 화면의 `중지`를 누르거나 영상이 끝날 때까지 계속됩니다. 테스트 UI는 `partial`/`final` 조각을 공백 한 칸으로 연결한 한 문단을 왼쪽 위부터 표시합니다. 자연스럽게 세 번째 줄이 생기면 맨 위 한 줄만 밀어내고 아래 `2줄`을 유지하는 rolling projection입니다. WebSocket API는 화면 줄 수와 무관하게 세션 동안 개별 CaptionUpdate 이벤트를 계속 내보내므로 운영 클라이언트는 필요한 누적·표시 정책을 별도로 적용할 수 있습니다.

### 송출 화면 (레거시 캡션보드 스타일)

`http://127.0.0.1:5287/output.html`은 레거시 자막기(자막기.exe 2021-07-01 빌드)의 송출 스펙을 재현한 표시 전용 창입니다([레거시 분석](docs/legacy-memo-analysis.md) §6). 검정 화면 하단 19.5% 밴드(1080p 기준 211px)를 진회색 RGB(71,71,71)로 칠하고, 흰색 나눔고딕 ExtraBold 52pt 상당(6.42vh) 글자를 같은 2줄 rolling 정책으로 표시합니다. 제어 화면 우측 상단의 `송출 화면 열기`로 같은 브라우저에서 창을 연 뒤 두 번째 모니터로 옮기고 화면을 클릭해 전체 화면으로 두면, 제어 화면이 표시하는 자막 문단이 BroadcastChannel로 중계됩니다. 서버 WebSocket 세션을 추가로 사용하지 않으므로 단일 세션 제한과 충돌하지 않습니다. 나눔고딕 ExtraBold가 설치되어 있지 않으면 맑은 고딕으로 대체됩니다.

운영 연동용 API와 지금 만든 확인용 화면의 책임은 [운영 API와 테스트 UI 구분](docs/api-and-test-ui.md)에 별도로 정리했습니다.

한 번에 하나의 전사 세션만 실행할 수 있습니다. 화면 세션을 끝낸 뒤 서버까지 종료하려면 서버 PowerShell 창에서 `Ctrl+C`를 누릅니다. `OPENAI_API_KEY`는 서버 프로세스에서만 읽으며 HTML, JavaScript, PCM Range 요청, WebSocket 메시지로 전달하거나 로그에 출력하지 않습니다. 서버는 loopback 주소에만 바인딩되므로 이 PoC를 외부 네트워크에 그대로 공개하지 마십시오.

### 실시간 자막 WebSocket API

운영 연동과 테스트 UI 모두 로컬 개발 환경에서 `ws://127.0.0.1:5287/ws/captions`를 사용합니다. HTTPS 앞에 배치한 클라이언트는 같은 경로의 `wss:`를 사용해야 합니다. 입력 오디오는 헤더 없는 `24 kHz / 16-bit / mono PCM (s16le)`입니다.

1. 연결 직후 UTF-8 텍스트 설정을 보냅니다: `{"type":"start","delay":"low"}` (`medium`, `high`도 가능)
2. PCM을 binary 프레임으로 보냅니다. 권장 크기는 100ms당 4,800바이트입니다.
3. 입력이 끝나면 UTF-8 텍스트를 보냅니다: `{"type":"end"}`
4. 서버는 `CaptionUpdate` JSON을 텍스트 프레임으로 반환합니다. `state`는 `partial`, `final`, `status` 중 하나입니다.

10초마다 보내는 내부 OpenAI commit은 자막 segment를 확정하기 위한 서버 내부 동작이며 browser/운영 클라이언트가 보낼 명령이 아니고 WebSocket 세션을 종료하지도 않습니다. 운영 WebSocket API에는 총 실행 시간 상한이 없습니다. 서버는 binary PCM이 계속 들어오는 동안 같은 세션을 유지하고, 정확한 `{"type":"end"}` 명령이나 클라이언트 연결 해제 때만 입력을 끝냅니다. 15초 idle timeout은 오디오 프레임이 전혀 오지 않는 고장 연결을 정리하기 위한 것으로, PCM silence 프레임이 계속 들어오는 정상 예배를 제한하지 않습니다.

API 키는 클라이언트 프로토콜에 포함하지 않습니다. 서버는 localhost에만 바인딩되고, 잘못된 프레임·두 번째 동시 세션·OpenAI 오류는 `status` 이벤트 뒤 연결을 닫습니다.

## 5. 정확도 평가

`final.txt`를 들으며 교정한 UTF-8 정답 파일을 만든 뒤 실행합니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\evaluate.ps1 `
  -RunDirectory .\runs\20260818-120000-low `
  -ReferencePath .\reference\service-15m.txt
```

`evaluation.json`에는 공백·문장부호를 제외한 한국어 CER, 핵심 용어 정확도, partial/final p50·p95, 자동 합격 기준이 기록됩니다. 음악·침묵 환각 여부는 원본 음원과 `events.jsonl`을 대조해 수동 확인해야 합니다.

## 합격 기준

- CER 10% 이하
- 핵심 용어 정확도 95% 이상
- 최초 partial p95 2초 이하
- final p95 3초 이하
- 입력 오디오 길이 15분(허용 오차 ±5초)
- 15분 실행 중 재접속 0회
- 음악·침묵 환각 문장 없음(수동 검수)

## 개발 검증

```powershell
$dotnet = "$env:LOCALAPPDATA\church-subtitle-tools\dotnet\dotnet.exe"
& $dotnet test .\ChurchSubtitle.sln
& $dotnet build .\ChurchSubtitle.sln --configuration Release
```

OpenAI 세션은 공식 문서의 transcription session, 24 kHz PCM, `languages: ["ko"]`, `delay: low|medium|high`을 사용합니다. 이 PoC의 기본값은 `low`이며, `medium`은 지연과 정확도의 균형, `high`는 더 많은 지연을 허용하고 정확도를 우선할 때 선택합니다. OpenAI 공식 API는 그 밖의 delay 단계도 제공하지만 현재 애플리케이션은 검증 범위를 이 세 값으로 제한합니다. 문서상 VAD 지원은 모델에 따라 다르며, 현재 `gpt-live-transcribe` PoC에서는 위 설명처럼 application-level 10초 커밋을 사용합니다: <https://developers.openai.com/api/docs/guides/realtime-transcription>
