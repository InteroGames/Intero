# CLAUDE.md

## 프로젝트

시대 불명의 점령 도시. 창 없는 취조실. 1인 NPC 심문극.
플레이어는 부역자들의 재산을 털던 도둑. 형사는 절도범으로 잡되, 뜻대로 안 되면 반역 조직 연루로 몰아간다.
**두 게이지를 동시에 낮게 유지한 채 취조실을 나가는 것이 승리.**

핵심 훅: 판이 끝나도 형사는 기억한다. 3판째쯤 "자네는 늘 그렇게 시작하더군"이 나온다.

## 상태 — 중단 (2026-08-10)

8/10 사전 과제 마감 시점에서 멈췄다. **재개 예정은 2026년 9~10월.**
아래 "D1~D6", "게이트", "남은 일수" 류의 일정 표현은 전부 그 마감 기준의 잔재다.
**남은 일수를 계산하거나 급하게 잘라내지 말 것.** 재개 시점의 우선순위는 `README.md`의
"다시 시작할 때"가 기준이다.

현재 메인 경로는 **2페이즈 구조(잠입 → 취조)**다. 상세 설계는 `Assets/Docs/2페이즈_설계.md`.
기존 단일 취조실은 손대지 않았고 그대로 컴파일된다 — 되돌아갈 자리로 남겨둔 것이다.

## 환경

- Unity **6000.5.6f1**, 빌트인 렌더 파이프라인. 프로젝트: `D:\workspace\Interrogation`
- 패키지: `com.unity.nuget.newtonsoft-json`, `com.unity.ugui`(TMP 포함), `com.coplaydev.unity-mcp` — 전부 manifest에 등록 완료
- **MCP for Unity**: HTTP 모드, `http://127.0.0.1:8080/mcp`. 에디터가 켜져 있고 `Window > MCP for Unity` 서버가 Running이어야 동작한다. 안 붙으면 그 창부터 확인.
- **한글 폰트**: TMP 기본 폰트는 한글이 전부 □로 나온다. 모든 TMP 텍스트는 반드시 `Assets/Fonts/Malgun SDF.asset` (다이내믹, 맑은 고딕)을 쓸 것. 새 텍스트를 만들 때마다 적용해야 한다.
- **씬 자동 생성**: 현재 기본 경로는 `Tools > Interrogation > Setup Phases (Heist + Inquiry)` (소스 `Assets/Editor/PhaseSceneSetup.cs`). 레거시 단일 취조실은 `Tools > Interrogation > Setup Scene` (`InterrogationSceneSetup.cs`). **기존 씬을 덮어쓰므로 씬을 손으로 수정한 뒤에는 다시 돌리지 말 것.** 키는 씬 밖에 있으므로 재생성해도 안 날아간다.
- **검증**: `Tools > Interrogation > Validate Phases` — 배선 누락·카드 정합성·잠입 결과 반영을 찍어준다. 머지 전에 돌린다.
- **플레이 로그**: 실행할 때마다 `PlayLogs/session_*.log`에 질문·입력·태틱·게이지·LLM 실패 원인이 남는다. **게임이 이상하면 추측하지 말고 이 파일부터 읽을 것.**

---

## 협업 (3인)

리포: `https://github.com/InteroGames/Intero.git` (구 `jasonwpgml/Intero` — 리다이렉트되지만 리모트를 새 주소로 바꿔둘 것)

### 처음 클론했을 때 (팀원 각자 1회)

1. **에디터 버전을 정확히 맞춘다.** 다른 버전으로 열면 프로젝트 파일이 갈려서 전원이 고생한다.
   `ProjectSettings/ProjectVersion.txt`를 임의로 고치지 말 것.
2. **`apikey.txt`를 프로젝트 루트에 직접 만든다.** 커밋되지 않으므로 각자 자기 키를 쓴다.
   ```
   openai=sk-...
   ```
3. `Tools > Interrogation > Setup Scene` 실행 → Play.

### 파일 소유권

겹치지 않게 나눠 두었다. 남의 영역을 고쳐야 하면 먼저 말할 것.

| 담당 | 파일 |
|---|---|
| 로직·판정 | `InterrogationState.cs`, `Judgment.cs` |
| LLM·대사 | `LLMClient.cs`, `DialoguePool.cs`, `dialogue_pool.json` |
| 씬·연출·UI | `InterrogationSceneSetup.cs`, `SpeechBubble.cs`, `SeatedLook.cs`, `InterrogationUI.cs` |

`InterrogationController.cs`는 로직과 LLM이 만나는 이음매다. **여기를 고칠 때만 서로 알린다.**

### 씬 충돌은 병합하지 않는다

`Interrogation.unity`, `Assets/Materials/*`, `Malgun SDF.asset`은 전부 **코드가 생성한 산출물**이다.
충돌이 나면 YAML을 들여다보지 말고 한쪽을 고른 뒤 `Setup Scene`을 다시 돌린다.
씬을 손으로 고쳐야 하는 시점(D5 연출)이 오면, 그때 이 규칙을 팀에서 깨고 명시적으로 알린다.

### 브랜치

`main` 직접 푸시 금지. 짧게 살고 빨리 합치는 브랜치를 쓴다 (`logic/turn-limit`, `ui/bubble-fade`).
해커톤이므로 리뷰는 가볍게, 대신 **머지 전에 반드시 Play로 한 판 돌려본다.**

---

## 절대 규칙 (어기면 프로젝트가 망가진다)

### 1. LLM은 판정하지 않는다

LLM이 하는 일은 **자연어 → 데이터** 변환 하나뿐이다.
`LLMClient.Extract()`가 반환하는 것은 추출된 사실 요소와 태틱 라벨뿐이다.

- 모순 검출 → `ContradictionDetector` (코드)
- 게이지 증감 → `GaugeCalculator` (코드)
- 승패 판정 → `InterrogationState.Evaluate()` (코드)

**"LLM한테 맡기면 더 자연스럽지 않나?"는 이미 검토하고 기각한 안이다.**
이유: 같은 상황에서 매번 다르게 판정하면 플레이어가 학습할 수 없고, 밸런싱이 불가능해진다.
이 게임은 플레이어가 규칙을 학습하는 게임이다.

### 2. 게이지는 GaugeCalculator 밖에서 수정하지 않는다

`state.suspicion += n` 같은 코드를 다른 곳에 쓰지 말 것.
증감 테이블은 한 곳에 모여 있어야 D6 밸런싱이 가능하다.

### 3. 화면에 오류 메시지를 절대 띄우지 않는다

파싱 실패, API 장애, 인젝션 시도, 욕설 — 전부 형사의 대사로 흡수된다.
`InterrogationController.Submit()`의 모든 분기는 반드시 `Finish()`를 거친다.
"입력을 이해할 수 없습니다" 같은 문구가 화면에 나가는 순간 몰입이 깨지고 완성도 부족으로 읽힌다.

### 4. 스키마는 동결됐다

`InterrogationState`의 **키 이름과 열거값**은 변경 금지.
숫자(임계값, 증감 폭)는 D6에서 조정할 대상이므로 변경 가능.
구조를 바꾸고 싶으면 바꾸지 말고 그 필요를 메모로 남길 것.

### 5. 히든 엔딩은 게임 내 입력만 인용한다

제4의 벽 엔딩에서 형사가 인용하는 문장은 반드시 플레이어가 이 게임에 입력한 것이어야 한다.
실제 개인정보를 아는 척하는 연출은 금지. 톤은 "감시했다"가 아니라 "이해했다".

### 6. API 키를 커밋하지 않는다

키는 **프로젝트 루트 `apikey.txt`** 에 있다 (`.gitignore` 등록됨). 씬·코드에 넣지 말 것.
`ApiKeyStore`가 파일 → 환경변수 순으로 찾는다. 씬을 다시 만들어도 키는 유지된다.
형식: `openai=sk-...` / `anthropic=sk-ant-...` (또는 키만 한 줄).

---

## 지금 하지 말 것

- **연출 고도화** (애니메이션, 파티클, 포스트프로세싱) — D5까지 미룬다.
  3D 취조실 자체는 8/3에 이미 만들었다. 프리미티브로만 지었고 `Setup Scene`이 통째로 재생성한다.
- **기능 추가** — SF 엔딩, 공포 엔딩, 여러 NPC, 멀티 판정은 전부 본선용. 지금은 안 만든다.
- **리팩터링을 위한 리팩터링** — 돌아가는 코드를 예뻐지려고 건드리지 않는다.
- **Play 모드에서 씬 조작** — MCP 씬 작업은 에디터 모드에서만 동작한다.

---

## 지금 고쳐야 할 것 (우선순위 순)

### P0. 모순 검출이 질문 맥락을 무시한다

현재 `ContradictionDetector`는 모든 진술의 같은 필드를 무차별 대조한다.
"그날 밤 어디 있었나"와 "어제는 어디 있었나"의 답이 서로 모순으로 잡힌다.

→ 질문에 `topic` 태그를 붙이고, 같은 topic 안에서만 대조하도록 수정.
`Statement`에 `topic` 필드 추가는 허용한다 (규칙 4의 예외, 명백한 설계 결함).

**이게 안 고쳐지면 게임이 재미없는 게 아니라 부당하게 느껴진다.** 최우선.

### ~~P1. 승리 경로가 없다~~ — 해결됨 (8/3)

`TURN_LIMIT = 6`에 도달하면 `Evaluate()`가 판정한다.
둘 다 50 미만 → `RELEASED`(유일한 승리, 게임 종료). 아니면 `HELD` → 다음 판 재조사.
`HELD`는 D1 이후 추가된 열거값이다 (규칙 4의 예외, `InterrogationState.cs`에 사유 기재).

**남은 것:** 턴 상한 6이 적절한지 미검증. 석방 전용 대사가 대사 풀에 없어서 지금은 LLM 생성에만 의존한다.

### ~~P2. DialoguePool이 Newtonsoft 의존~~ — 해결됨 (8/3)

`com.unity.nuget.newtonsoft-json` 3.2.1이 manifest에 등록되어 있다. 컴파일 실패 시에만 다시 볼 것.

### P3. confession 정확도 미검증

부인문("훔치지 않았다")을 자백으로 오판하는지 테스트 필요.
오판하면 의심도 +40이 잘못 들어가서 게임이 즉시 깨진다.

---

## 게이트

- **D3 저녁** — 텍스트만으로 3판 플레이. 재미없으면 컨셉 축소를 결정한다.
- **D4 종료** — "자네는 늘 그렇게 시작하더군"이 실제로 발화되는가. 이게 게임의 전부다.
- **D5** — 유일하게 잘라도 되는 날. 일정이 밀리면 3D를 반나절로 축소.

## 매번 확인할 것

인터넷을 끄고 실행했을 때:
- 게임이 멈추지 않는가
- 형사가 폴백 대사로 대응하는가
- 오류 메시지가 화면에 뜨지 않는가

**시연 중 네트워크가 죽어도 게임은 계속 돌아가야 한다.**

---

## 파일 구조

전체 목록은 `README.md`의 "구조"가 최신이다. 아래는 레거시 단일 취조실 기준으로 남겨둔다.

```
Assets/Scripts/Heist/          Phase 1 (잠입)
Assets/Scripts/Inquiry/        Phase 2 (취조) - 판정 전부 InquiryJudge
Assets/Scripts/
  InterrogationState.cs      상태 모델, 열거형, 임계값
  Judgment.cs                모순 검출 + 게이지 계산 (코드 판정 전부)
  DialoguePool.cs            대사 풀 로더
  LLMClient.cs               Anthropic API, 추출 전용 (claude-haiku-4-5, timeout 6초)
  InterrogationController.cs 턴 루프
  InterrogationUI.cs         최소 UI
Assets/Resources/
  dialogue_pool.json         폴백 36 + 형사 대사 80
  SpeechBubble.cs            심문관 머리 옆 월드 말풍선 (빌보드)
  SeatedLook.cs              앉은 1인칭 시점. 우클릭 드래그로 둘러보기
Assets/Editor/
  InterrogationSceneSetup.cs 취조실 씬 전체 자동 생성기 (Tools 메뉴)
Assets/Fonts/
  malgun.ttf + Malgun SDF    한글 TMP 폰트 (전 텍스트 필수)
Assets/Scenes/
  Interrogation.unity        Setup Scene 실행 시 생성됨
Assets/Docs/
  D1_스키마_확정.md           스키마 원본 (동결)
  README_SETUP.md            D2 배선 가이드 + 알려진 미완성
```

## 작업 방식

- 한 번에 하나씩. P0 끝내고 확인한 뒤 P1로.
- 어떤 단계든 **2번 실패하면 자동화를 버리고 손으로 한다.** 디버깅에 시간을 태우는 것이 이 프로젝트의 가장 흔한 사망 원인이다.
- 컴파일 에러는 Unity Console을 직접 읽고 고칠 것.
