# D2 배선 가이드

## 1. 패키지

Package Manager → Add package by name → `com.unity.nuget.newtonsoft-json`
TextMeshPro는 Essentials만 임포트.

## 2. 파일 배치

```
Assets/
  Scripts/
    InterrogationState.cs
    Judgment.cs
    DialoguePool.cs
    LLMClient.cs
    InterrogationController.cs
    InterrogationUI.cs
  Resources/
    dialogue_pool.json      ← 확장자를 .json 그대로 두고 TextAsset으로 참조
```

## 3. 씬 구성 (Canvas 하나면 끝)

| 오브젝트 | 컴포넌트 | 비고 |
|---|---|---|
| `_Game` | LLMClient, InterrogationController, InterrogationUI | 빈 게임오브젝트 |
| `LogText` | TMP_Text | ScrollRect 안에 두는 게 편함 |
| `InputField` | TMP_InputField | Line Type: Single Line |
| `SubmitButton` | Button | |
| `SuspicionBar` | Slider | Interactable 끄기, Min 0 / Max 1 |
| `SubversionBar` | Slider | 위와 동일 |
| `GaugeLabel` | TMP_Text | |
| `DebugText` | TMP_Text | 제출 빌드에서 showDebug 끄기 |

인스펙터에서 `_Game`의 각 필드에 위 오브젝트를 드래그.

## 4. API 키

`LLMClient`의 `apiKey`는 인스펙터에서만 넣는다.

**주의:** 이 키는 빌드에 그대로 박힌다. 시연 영상 촬영과 로컬 테스트까지는 괜찮지만,
빌드 파일을 배포한다면 반드시 중계 서버를 두거나 제출 직전 키를 교체·폐기할 것.
제출용 빌드를 공개 배포한다면 키를 넣은 채로 올리지 말 것.

## 5. 첫 실행 체크리스트

- [ ] 형사의 첫 질문이 뜬다
- [ ] 입력하면 `> 입력` 이 로그에 찍힌다
- [ ] 1.2초 넘으면 "(서류를 넘긴다)" 가 뜬다
- [ ] 응답이 한 글자씩 출력된다
- [ ] 게이지가 움직인다
- [ ] DebugText에 태틱과 증감이 찍힌다

## 6. 인터넷 끄고 다시 실행

- [ ] 게임이 멈추지 않는다
- [ ] 형사가 폴백 대사로 대응한다
- [ ] 오류 메시지가 화면에 뜨지 않는다

**6번이 통과 안 되면 시연에서 죽는다. D2 안에 반드시 확인.**

## 7. 알려진 미완성

- `Extraction.confession` 정확도 미검증 → D3에서 부인문 테스트 필요
- 모순 검출이 질문 맥락을 무시함. "그날 밤 장소"와 "어제 장소"를 같은 필드로 본다.
  D3에서 질문에 `topic` 태그를 붙여 같은 topic 안에서만 대조하도록 수정.
- 승리(석방) 경로 미구현. `CanReleaseNow()`는 있으나 호출되는 곳이 없다.
  턴 상한(예: 6턴)을 두고 종료 시 판정하는 방식으로 D3에서 연결.
