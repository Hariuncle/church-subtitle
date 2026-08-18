# 실시간 자막 WebSocket API 및 로컬 테스트 UI 설계

## 목표

교회 방송 시스템 또는 테스트 브라우저가 24 kHz, 16-bit, mono PCM 오디오를 전송하면 OpenAI `gpt-live-transcribe` 결과를 `CaptionUpdate` JSON으로 돌려주는 로컬 API를 만든다. 같은 API를 사용하는 브라우저 화면에서 상단 원본 영상과 하단 실시간 자막을 확인한다.

## 범위

- ASP.NET Core .NET 8 로컬 서버
- 단일 양방향 WebSocket 오디오/자막 API
- 준비된 15분 PCM을 사용하는 브라우저 테스트 클라이언트
- YouTube 원본 영상의 `09:25` 시작 화면
- 테스트 UI 전용 30초, 2분, 15분 실행 길이 선택
- `low`, `medium` 전사 지연 선택
- 한 번에 하나의 전사 세션

현장 오디오 장치 캡처와 기존 방송 자막 시스템 sink는 이번 범위에 포함하지 않는다.

## 서버 구조

새 `ChurchSubtitle.Web` 프로젝트가 `127.0.0.1`에만 바인딩된다. `scripts/run-web.ps1`이 기존 로컬 `.env` 로더를 사용해 `OPENAI_API_KEY`를 현재 서버 프로세스에만 전달한다.

서버는 다음 경계를 가진다.

- `AudioCaptionWebSocketEndpoint`: 클라이언트 WebSocket 수락, 설정 및 오디오 프레임 검증, 스트림 종료 처리
- `WebSocketCaptionSink`: `CaptionUpdate`를 camelCase JSON 텍스트 프레임으로 전송
- `SingleSessionGate`: 동시 전사 세션을 한 개로 제한하고 두 번째 요청은 오류로 종료
- 기존 `OpenAiRealtimeTranscriptionProvider`: PCM 스트림을 OpenAI로 전달하고 partial/final을 sink로 발행

API 키는 서버에서만 사용하며 HTML, JavaScript, WebSocket 응답, 로그에 포함하지 않는다.

OpenAI 연결은 전사 전용 `wss://api.openai.com/v1/realtime?intent=transcription` endpoint를 사용한다. 2026-08-19 실제 연결 진단에서 model query는 `invalid_model`, `gpt-live-transcribe`에 명시한 server VAD 값은 `invalid_value`로 거부되었다. 따라서 공식 manual-commit 예시처럼 `session.audio.input.turn_detection: null`을 명시한다. 100ms PCM 전송 속도를 유지하며 바이트 기준 480,000바이트(10초)마다 `input_audio_buffer.commit`을 보내고, 마지막 비어 있지 않은 나머지를 커밋한다.

한 번에 한 commit만 ACK 대기 상태로 둔다. `input_audio_buffer.committed.item_id`를 받은 뒤에만 해당 10초 창을 메모리에서 지우고 다음 창으로 진행한다. ACK 전 창은 최대 480KB로 제한되며, 비탐색 live stream에서 transient 연결 실패가 나면 새 세션에 전체 창을 재전송한다. commit이 서버에 수락됐지만 ACK가 유실된 경우에는 재전송 중복 가능성이 있으므로 해당 실행을 불완전 처리한다. ACK 전에도 partial은 즉시 sink에 전달하고, final만 ACK로 연결된 item ID와 결합해 완료로 인정한다. duplicate final은 무시하고 out-of-order final은 segment ordinal 순서로 sink에 전달한다. 재접속마다 provider item ID namespace를 새로 부여한다.

각 commit ACK는 최대 10초로 제한한다. 스트리밍 중에는 completed를 계속 수집하되, 모든 completed의 최종 제한 시간은 입력 종료 뒤 공유하는 최대 10초 drain이다.

10초 commit은 하나의 연속 전사 세션 안에서 segment만 나누며 세션 종료 신호가 아니다. 공급자와 `/ws/captions`에는 임의의 총 실행 시간 제한이 없다. binary PCM이 계속 도착하는 동안 무기한 이어지고, 정확한 `{"type":"end"}` 또는 연결 해제에서만 끝난다. endpoint의 15초 no-frame idle timeout은 PCM silence도 전송되지 않는 고장 연결만 정리한다.

완료된 projector 줄과 latency 추적 상태는 final 처리 시 제거한다. 비정상·미완료 상태와 최근 duplicate 방지 tombstone도 고정된 상한을 두며, 정렬 불가능한 final이 상한을 넘으면 조용히 유실하지 않고 실행을 실패 처리한다. 따라서 정상적인 연속 세션의 메모리는 누적 segment 수에 비례해 증가하지 않는다.

VAD 음성 경계 이벤트가 없으면 단어 timestamp, 발화 시작/종료 시각, partial/final latency를 추정하지 않는다. commit ordinal에 대응하는 10초 창 시작 offset은 정렬 메타데이터일 뿐 단어 또는 발화 timestamp가 아니다. 해당 runtime metric은 빈 배열이고 turn count는 0일 수 있다.

## WebSocket 프로토콜

경로는 `/ws/captions`이다.

연결 후 첫 프레임은 UTF-8 JSON 설정이다.

```json
{"type":"start","delay":"low"}
```

그다음 클라이언트는 PCM 바이너리를 전송한다. 권장 프레임은 100ms에 해당하는 4,800바이트다. 서버는 16-bit 샘플 경계를 지키기 위해 홀수 바이트 프레임을 거부하고, 설정 전에 바이너리가 오거나 단일 프레임이 96,000바이트를 초과하면 오류 상태를 반환하고 연결을 종료한다. 내부 pipe는 클라이언트 프레임 경계와 무관하게 연속 PCM 스트림으로 취급한다.

오디오가 끝나면 다음 텍스트 프레임을 보낸다.

```json
{"type":"end"}
```

서버 응답은 기존 `CaptionUpdate` JSON 형식을 유지한다. `state`는 `partial`, `final`, `status`를 사용한다. 정상 종료와 오류도 `status` 이벤트로 알린 뒤 WebSocket을 닫는다.

클라이언트 연결 해제 시 오디오 스트림과 OpenAI 작업을 취소한다. OpenAI 실패 또는 재접속이 발생하면 자동 성공으로 간주하지 않고 오류 상태를 전송한다.

## 테스트 UI

단일 페이지 레이아웃이다.

- 상단: YouTube 원본 영상, 시작 위치 `09:25`
- 중간 제어줄: `low/medium`, `30초/2분/15분`, 시작, 중지, 연결 상태
- 하단: 현재 partial을 크게 표시하고 final 문장을 최근 순서로 누적

시작 버튼은 준비된 PCM을 로컬 서버에서 읽고 선택한 길이만큼 4,800바이트씩 100ms 간격으로 `/ws/captions`에 보낸다. 동시에 YouTube 플레이어를 `09:25`로 이동해 재생한다. 브라우저는 API 키를 읽거나 전송하지 않는다.

30/120/900초 길이는 이 파일 기반 UI의 시험 편의 기능이며 운영 API의 최대 세션 길이가 아니다.

UI 기본값은 `low`, 30초다. 30초 테스트 예상 비용은 15분 실행 비용의 약 1/30이다.

## 로컬 데이터 경로

`GET /test-assets/service.pcm`은 기존 `data/poc-source-15m/service-segment-24k-mono-s16le.pcm`을 로컬에서만 제공한다. 파일이 없으면 404와 한국어 오류 메시지를 반환한다. 이 경로는 테스트 전용이며 운영 오디오 API는 파일 경로에 의존하지 않는다.

## 오류 처리

- `.env` 키 없음: 서버 시작 전 안전 종료
- PCM 파일 없음: UI에 준비 스크립트 실행 안내
- 잘못된 첫 프레임 또는 PCM 크기: status 오류 후 연결 종료
- 동시 세션: 두 번째 연결을 busy 오류로 종료
- 브라우저 중지/연결 해제: OpenAI 및 pipe 취소
- OpenAI 오류/재접속/미완료 final: status 오류, 실행 실패 처리

키 값과 오디오 원문은 오류 응답에 포함하지 않는다.

## 검증

- WebSocket 설정 프레임 검증 단위 테스트
- CaptionUpdate JSON 직렬화 테스트
- 동시 세션 gate 테스트
- 바이너리 오디오가 provider 입력 스트림으로 전달되는 통합 테스트
- 잘못된 프레임과 클라이언트 중단 테스트
- 정적 UI 로드 및 PCM test asset 범위 응답 테스트
- 기존 Core 테스트 전체 회귀
- 실제 `.env` 키를 이용한 기본 30초 `low` 실행

실제 API smoke test에서는 키 값을 출력하지 않고 연결 상태, partial/final 수, 종료 상태만 기록한다.
