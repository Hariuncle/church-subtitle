# 프로젝트 로컬 OpenAI API 키 설계

## 목적

Windows 사용자 또는 시스템 전역 환경변수를 변경하지 않고 이 PoC에서만 OpenAI API 키를 사용한다.

## 설계

- 프로젝트 루트의 `.env` 파일에 `OPENAI_API_KEY`를 저장한다.
- `.env`는 기존 `.gitignore` 규칙으로 Git 추적에서 제외한다.
- `scripts/run-transcription.ps1`은 현재 프로세스에 `OPENAI_API_KEY`가 없을 때만 프로젝트 루트의 `.env`를 읽는다.
- 이미 현재 PowerShell 프로세스에 환경변수가 있으면 그 값을 우선한다.
- 파서는 빈 줄과 `#` 주석을 무시하고 `OPENAI_API_KEY=<value>` 한 항목만 읽는다.
- 키 값은 콘솔이나 로그에 출력하지 않는다.
- 파일이 없거나 값이 비어 있으면 기존과 같이 안전하게 실행을 중단한다.

## 파일

- `.env`: 사용자가 실제 키를 입력하는 로컬 비밀 파일
- `.env.example`: 커밋 가능한 형식 예시
- `scripts/run-transcription.ps1`: 로컬 `.env` 로더
- `README.md`: 설정 및 실행 안내

## 검증

- `.env`가 Git에서 무시되는지 확인한다.
- 프로세스 환경변수가 `.env`보다 우선하는지 검사한다.
- `.env`의 키가 현재 실행 프로세스에만 적용되는지 검사한다.
- 키 값이 출력에 노출되지 않는지 검사한다.
