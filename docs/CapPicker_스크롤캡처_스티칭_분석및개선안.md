# CapPicker 스크롤 캡처 스티칭 분석 및 소스 개선안

- 대상 소스: `P260913_CapPicker2(1).zip`
- 핵심 대상 파일: `ScrollCapture.cs`
- 분석 자료: `scroll-last.log`, `scroll-dbg-1.png` ~ `scroll-dbg-14.png`, 최종 스크롤 캡처 결과 이미지
- 목적: 앱별 전용 처리 없이, CapPicker의 가벼운 구조를 유지하면서 세로 스크롤 캡처 스티칭 안정성을 높임

---

## 1. 결론 요약

현재 실패의 주된 원인은 **스크롤 입력 자체보다 스티칭 엔진**에 있다.

실제 로그를 보면 대부분의 Wheel 스크롤은 약 400px 단위로 안정적으로 이동한다. 그러나 현행 구현은 다음 문제로 인해 일부 프레임에서 잘못된 overlap을 채택하고, 고정 헤더/중복 영역까지 다시 결과에 그리면서 최종 이미지가 늘어나거나 반복된다.

핵심 개선 방향은 다음과 같다.

1. **누적 결과(accumulator)가 아니라 직전 프레임(previous)과 현재 프레임(frame) 사이에서 이동량을 계산한다.**
2. **고정영역(prefix)과 overlap은 새 결과에 다시 그리지 않고, 실제 새로 나타난 하단 영역만 append한다.**
3. `SeamContinues()`의 이진 판정을 약화/대체하고, **매칭 점수 + 최근 정상 이동량과의 연속성**으로 후보를 평가한다.
4. 매칭 실패 시 다음 스크롤을 보내지 말고 **현재 위치에서 재캡처 후 재매칭**한다.
5. 최근 정상 scroll delta의 median을 사용해 갑작스러운 오매칭을 차단한다.
6. 이후 2차 개선으로 exact ARGB 비교를 grayscale/edge signature 기반으로 완화하고, 실제 스크롤 대상 HWND 탐지를 보강한다.
7. 편집 툴바의 **좌우/상하 반전 아이콘을 통상적인 대칭형 flip 아이콘으로 교체**해 직관성을 높인다.
8. 우측 **설정 버튼은 아이콘 전용을 유지**하되, 현재 톱니 glyph 자체를 통상적인 설정(cog) 아이콘 형태로 다시 그린다. 텍스트 버튼으로 바꾸지 않는다.

현 상태에서 우선 1~5번만 반영해도 Chrome/Edge 등 일반 웹페이지 스티칭 품질은 크게 개선될 가능성이 높다. UI 쪽은 7~8번을 함께 반영하면 기능 변화 없이 완성도와 직관성을 높일 수 있다.

---

## 2. 실제 로그에서 확인된 동작

테스트 영역:

```text
region=2880x1704 lowSpec=False
```

주요 로그:

```text
s2  prefix=242 overlap=1062 best=0.916 bestH=1063
s3  prefix=242 overlap=1062 best=0.917 bestH=1063
s4  prefix=242 overlap=1066 best=0.917 bestH=1069
s5  prefix=247 overlap=1060 best=0.917 bestH=1072
s6  prefix=243 overlap=-1   best=0.917 bestH=404
s7  prefix=243 overlap=667  best=0.917 bestH=739
s8  prefix=338 overlap=966  best=0.917 bestH=977
s9  prefix=338 overlap=966  best=0.916 bestH=1238
s10 prefix=338 overlap=971  best=0.917 bestH=1021
s11 prefix=352 overlap=207  best=0.998 bestH=952
s12 prefix=352 overlap=1197 best=0.917 bestH=1200
s13 equal=True
s14 PageDown equal=True
result frames=11 height=6586 stop=끝까지 도달
```

현행 추가 높이 계산식은 다음과 같다.

```csharp
added = frame.Height - prefix - overlap;
```

따라서 정상적인 초반 구간은 거의 정확히 400px이다.

```text
s2: 1704 - 242 - 1062 = 400
s3: 1704 - 242 - 1062 = 400
s4: 1704 - 242 - 1066 = 396
s5: 1704 - 247 - 1060 = 397
s8: 1704 - 338 - 966  = 400
s9: 1704 - 338 - 966  = 400
s10:1704 - 338 - 971  = 395
```

즉 **실제 스크롤은 대부분 약 400px로 정상**이다.

---

## 3. 가장 명확한 오매칭: s11

s11은 현 문제를 가장 잘 보여준다.

```text
s11 prefix=352 overlap=207 best=0.998 bestH=952
```

`bestH=952`, `best=0.998`은 952px 후보가 사실상 매우 강한 정답 후보였음을 뜻한다.

이 후보를 사용하면:

```text
1704 - 352 - 952 = 400px
```

으로 앞선 구간과 정확히 연속된다.

그러나 실제 채택된 overlap은 207px이므로:

```text
1704 - 352 - 207 = 1145px
```

을 한 번에 추가했다.

정상 예상치 400px보다 **745px를 과다 추가**한 것이다.

현재 최종 높이 6586px에서 이 한 건만 정상 400px로 교정하면:

```text
6586 - 745 = 5841px
```

수준이 된다.

따라서 결과 이미지의 비정상적인 늘어짐/반복은 단순 체감 문제가 아니라 **로그상 수치로도 확인되는 스티칭 오판정**이다.

---

## 4. 원인 1: `SeamContinues()`가 정답 후보를 버림

현행 `FindOverlap()`은 큰 overlap부터 검색하고 `RowsMatch()`가 통과해도 다시 `SeamContinues()`를 적용한다.

현행 소스 기준:

```csharp
if (!SeamContinues(pa, stride, acc.Height, pb, stride,
    frame.Height, w, h, frameTop))
{
    if (ratio > bestRatio) { bestRatio = ratio; bestH = h; }
    continue;
}
return h;
```

`SeamContinues()`의 목적은 표/목록처럼 반복되는 패턴에서 alias offset을 거르는 것이지만, 이번 s11에서는 **0.998 수준의 강한 후보를 탈락시키고 207px짜리 잘못된 후보를 채택**했다.

### 문제점

- true/false 단일 조건이 너무 강하다.
- 실제 scroll delta의 연속성을 고려하지 않는다.
- 매우 높은 이미지 일치도도 `SeamContinues()` 하나로 무시될 수 있다.
- 반복 패턴 방지용 로직이 오히려 정상 후보를 제거한다.

### 개선 원칙

`SeamContinues()`를 절대 기각 조건으로 쓰지 말고 후보 점수의 일부로 낮춘다.

권장 우선순위:

```text
1. 이미지 일치도(match score)
2. 최근 정상 scroll delta와의 차이
3. overlap 크기
4. 반복 패턴 의심 패널티
```

예시:

```text
후보 A: overlap=952, added=400, score=0.998, 최근 median≈400
→ 매우 강하게 채택

후보 B: overlap=207, added=1145, score가 통과하더라도 최근 median≈400과 큰 차이
→ 기각 또는 강한 패널티
```

---

## 5. 원인 2: 누적 이미지(accumulator)를 매칭 대상으로 사용

현재 코드:

```csharp
overlap = FindOverlap(accumulator, frame, prefix, out best, out bestH);
```

스크롤 이동량은 본질적으로 **직전 프레임과 현재 프레임 사이의 관계**인데, 점점 길어지는 누적 결과 이미지와 현재 프레임을 비교하고 있다.

### 문제점

- 결과 이미지가 길어질수록 후보 공간이 커진다.
- 이전에 잘못 붙인 영역이 다음 매칭에 영향을 준다.
- 반복되는 표/목록/고정 UI가 accumulator 내부 여러 위치에 존재할 수 있어 alias 가능성이 커진다.
- `LockBits` 후 accumulator 전체를 `int[]`로 복사하므로 결과가 길수록 메모리와 CPU 부담이 증가한다.

### 개선

매칭은 항상 다음 두 장으로만 수행한다.

```text
previous frame ↔ current frame
```

즉:

```csharp
MatchResult match = FindFrameShift(previous, frame, prefix, history);
```

으로 변경한다.

accumulator는 **결과 저장용**으로만 사용하고, 매칭에는 사용하지 않는다.

### 장점

- 매칭 메모리가 프레임 2장 수준으로 고정
- 앞선 오스티칭이 다음 매칭에 전파되지 않음
- scroll delta라는 물리적으로 명확한 값을 얻을 수 있음
- CapPicker의 가벼움 방향과 잘 맞음

---

## 6. 원인 3: 고정영역과 중복영역을 제외한다고 계산하지만 실제 draw는 전체 프레임

현재 추가 높이는 다음처럼 계산한다.

```csharp
int added = frame.Height - prefix - overlap;
```

그러나 실제 합성은:

```csharp
g.DrawImageUnscaled(accumulator, 0, 0);
g.DrawImageUnscaled(frame, 0, accumulator.Height - overlap - prefix);
```

이다.

즉 위치만 위로 당겨서 **frame 전체를 다시 그린다.**

고정 header와 이미 겹친 content를 실제 source crop에서 제거하지 않는다.

### 개선

첫 프레임은 전체를 유지하고, 두 번째 프레임부터는 아래 영역만 append한다.

```text
sourceY = prefix + overlap
sourceHeight = frame.Height - sourceY
```

개념 코드:

```csharp
int sourceY = prefix + overlap;
int added = frame.Height - sourceY;

Bitmap stitched = new Bitmap(accumulator.Width,
    accumulator.Height + added,
    PixelFormat.Format32bppArgb);

using (Graphics g = Graphics.FromImage(stitched))
{
    g.DrawImageUnscaled(accumulator, 0, 0);

    Rectangle src = new Rectangle(0, sourceY, frame.Width, added);
    Rectangle dst = new Rectangle(0, accumulator.Height, frame.Width, added);
    g.DrawImage(frame, dst, src, GraphicsUnit.Pixel);
}
```

이렇게 해야:

- Chrome 탭/주소창 등 고정영역은 최초 1회만 존재
- overlap 구간도 중복되지 않음
- 실제 신규 콘텐츠만 아래에 붙음

---

## 7. 원인 4: 매칭 실패 후 바로 다음 스크롤을 수행

현행:

```csharp
if (overlap < MinOverlap)
{
    failStreak++;
    if (failStreak < 3)
        continue;
}
```

`continue`하면 for-loop 처음으로 돌아가 **다시 `SendScroll()`부터 실행**한다.

즉 현재 화면에서 매칭을 다시 시도하는 게 아니다.

이번 로그도 이를 보여준다.

```text
s6 overlap=-1
s7 overlap=667
```

s6에서 실패한 뒤 다시 스크롤했고, s7은 약:

```text
1704 - 243 - 667 = 794px
```

을 추가했다.

이는 약 400px 스크롤 두 번이 합쳐진 값으로 해석 가능하다.

이번에는 우연히 회복했지만, overlap이 더 줄어드는 앱에서는 연쇄 실패 가능성이 높다.

### 개선

매칭 실패 시:

```text
스크롤 추가 X
↓
100~200ms 대기
↓
같은 현재 위치를 재캡처
↓
재매칭
↓
최대 2회 재시도
↓
그래도 실패할 때만 종료 또는 보조 전략
```

권장 구조:

```csharp
Bitmap frame = CaptureRectangle(...);
MatchResult match = TryMatch(previous, frame);

if (!match.Success)
{
    frame.Dispose();
    Thread.Sleep(150);
    frame = CaptureRectangle(...); // 스크롤 없이 재캡처
    match = TryMatch(previous, frame);
}
```

lazy loading, smooth-scroll animation, compositor 지연에 특히 효과적이다.

---

## 8. 개선안 핵심 구조

### 현행

```text
누적 이미지
   ↓
Scroll
   ↓
고정시간 대기
   ↓
Capture
   ↓
accumulator ↔ frame overlap 탐색
   ↓
frame 전체를 위치만 조정해 draw
```

### 개선 권장

```text
previous frame
   ↓
Scroll
   ↓
화면 안정화 또는 제한시간 대기
   ↓
current frame
   ↓
StaticPrefix(previous, current)
   ↓
previous ↔ current의 Y 이동량 / overlap 탐색
   ↓
최근 scroll delta와 비교해 후보 검증
   ↓
실패 시 현재 위치 재캡처
   ↓
current의 신규 하단부만 accumulator에 append
   ↓
previous = current
```

---

## 9. `MatchResult` 도입 권장

단순 `int overlap`보다 매칭 결과를 구조화하는 것이 좋다.

예시:

```csharp
private sealed class MatchResult
{
    public bool Success;
    public int Prefix;
    public int Overlap;
    public int Added;
    public double Score;
    public bool IsOutlier;
}
```

또는 struct로 해도 된다.

### 기대 효과

- 로그에 판단 근거를 남기기 쉬움
- 후보 점수와 outlier 여부를 분리 가능
- 이후 grayscale signature 등의 알고리즘으로 교체하기 쉬움

---

## 10. 최근 이동량 median을 이용한 이상치 차단

이번 로그에서 정상 이동량은 약 395~400px로 매우 안정적이다.

따라서 최근 3~5개의 `added` 값을 유지하고 median을 구한다.

예:

```text
최근 added = 400, 400, 396, 397
median ≈ 398.5
```

새 후보가 1145px라면 명백한 이상치다.

### 권장 규칙

일반 중간 구간에서는:

```text
candidateAdded가 median의 약 0.5~1.8배 범위
```

정도를 우선 후보로 본다.

다만 페이지 끝에서는 작은 이동이 정상일 수 있으므로:

- `candidateAdded < median * 0.5`는 페이지 끝 후보로 허용
- `candidateAdded > median * 1.8`은 매우 높은 이미지 score가 없는 한 기각
- 직전 매칭 실패 후에는 약 2배 이동도 허용 가능

즉 절대 임계값보다 **상황별 가중치**로 사용한다.

---

## 11. `FindOverlap()` 개선 방향

### 1차 개선: 현재 픽셀 비교 구조를 최대한 유지

우선 큰 구조만 고친다.

변경 전:

```csharp
FindOverlap(accumulator, frame, prefix, ...)
```

변경 후:

```csharp
FindOverlap(previous, frame, prefix, expectedAdded, ...)
```

후보 h마다:

```csharp
candidateAdded = frame.Height - prefix - h;
```

를 계산해 최근 median과 가까운 후보에 가점을 준다.

`RowsMatch()`가 성공한 첫 후보를 즉시 return하지 말고, 상위 후보를 일정 수 수집해 최종 점수로 선택하는 방식이 안전하다.

예시 scoring 개념:

```text
score = imageMatchScore
      - deltaDeviationPenalty
      - repetitionPenalty
```

### 2차 개선: exact ARGB 의존 완화

현재 `RowsMatch()`는:

```csharp
if (pa[ia + x] != pb[ib + x])
```

형태의 exact pixel equality 기반이다.

브라우저/WPF/Electron에서는 다음 영향이 있을 수 있다.

- sub-pixel font rendering
- fractional DPI
- compositor timing
- hover/caret/animation
- lazy loading

따라서 장기적으로는 다음 방식이 더 안정적이다.

```text
원본 프레임
→ 1/4~1/8 정도 sampling
→ grayscale 또는 밝기값
→ 수평 edge/gradient signature
→ Y offset 후보 검색
→ 상위 후보만 원본 픽셀로 검증
```

외부 라이브러리 없이도 구현 가능하다.

단, 2.1 첫 안정화에서는 **구조 개선 후 실제 성공률을 먼저 확인**하고 필요 시 도입하는 것을 권장한다.

---

## 12. `StaticPrefix()` 처리 원칙

현재 `StaticPrefix(previous, frame)` 자체는 이번 로그에서 242~352px의 브라우저 고정영역을 상당히 잘 탐지하고 있다.

따라서 당장 제거할 필요는 없다.

다만 역할을 명확히 한다.

```text
StaticPrefix = 매칭에서 제외할 상단 고정영역
```

그리고 결과에 추가할 때도 동일하게 제외해야 한다.

즉:

```csharp
sourceY = prefix + overlap;
```

을 실제 crop 시작점으로 사용한다.

### 추후 한계

다음 UI는 top-prefix 방식만으로 완전히 처리되지 않을 수 있다.

- 좌측 고정 내비게이션
- 우측 고정 패널
- 하단 floating toolbar
- 페이지 내부 중간 sticky 요소

그러나 현재 단계에서 범용 block mask까지 구현하면 복잡도가 급격히 증가하므로, **상단 고정영역 + frame-to-frame shift**만으로 먼저 안정화하는 것이 적절하다.

---

## 13. Scroll 입력은 2차 개선 대상으로 유지

이번 테스트에서는 실제 Wheel 이동이 약 400px로 안정적이므로 **현재 실패의 1차 원인은 scroll input이 아니다.**

따라서 `SendScroll()` 재설계는 우선순위를 낮춘다.

다만 범용성 확대 시 다음 순서를 검토한다.

```text
1. 캡처 영역 중앙의 WindowFromPoint()로 실제 자식 HWND 탐색
2. 해당 HWND에 WM_MOUSEWHEEL
3. 실패 시 top-level HWND
4. 최후 fallback으로 SendInput 실제 Wheel
5. 선택적으로 UI Automation ScrollPattern
```

앱 이름별 분기보다 **지원되는 스크롤 경로를 탐지해서 선택**하는 방식이 CapPicker 방향에 맞다.

---

## 14. 고정 Sleep 개선은 안정화 후 적용

현재:

```csharp
Thread.Sleep(settleMs);
```

일반 모드 500ms, 저사양 800ms 방식이다.

기능 안정화 후에는 adaptive settle을 고려할 수 있다.

예:

```text
60~80ms 간격 소형 sample 비교
↓
2회 연속 변화량이 매우 작으면 안정화 완료
↓
최대 800~1000ms timeout
```

장점:

- 정적 페이지는 더 빨라짐
- animation/lazy loading은 더 기다릴 수 있음
- 고정 500ms보다 앱 편차에 강함

단, 첫 개선 단계에서는 스티칭 구조 수정과 혼합하지 않는 편이 디버깅에 유리하다.

---

## 15. 권장 소스 수정 순서

### Phase 1 — 필수 구조 수정

- [ ] `FindOverlap(accumulator, ...)` → `FindOverlap(previous, ...)`
- [ ] 전체 frame draw → `prefix + overlap` 이후 신규 영역만 crop/append
- [ ] `SeamContinues()`를 hard reject에서 제거하거나 soft penalty로 변경
- [ ] 최근 정상 `added` 3~5개 저장 및 median 계산
- [ ] 후보 overlap 선택 시 median과의 연속성 반영
- [ ] 매칭 실패 시 추가 Scroll 전에 현재 위치 재캡처 1~2회
- [ ] 로그에 `added`, `median`, `score`, `retry` 기록

### Phase 2 — 품질 강화

- [ ] exact ARGB 대신 grayscale/edge signature 후보 탐색
- [ ] 상위 2~3개 후보 원본 검증
- [ ] adaptive settle 도입
- [ ] 실제 child HWND / SendInput fallback 검토

### Phase 3 — Release 정리

- [ ] `scroll-dbg-*.png` 자동 저장 제거 또는 디버그 옵션으로 제한
- [ ] `scroll-last.log`는 LocalAppData 또는 진단 옵션일 때만 기록
- [ ] 최대 결과 크기/OOM 방어 재검증
- [ ] 실패 시 지금까지 정상 스티칭된 결과 보존

---

## 16. 구현 예시: 핵심 루프 의사코드

```csharp
Bitmap accumulator = first.Clone();
Bitmap previous = first;
Queue<int> recentAdded = new Queue<int>();

while (...)
{
    SendScroll(...);
    WaitForSettle(...);

    Bitmap frame = CaptureRectangle(region);

    int prefix = StaticPrefix(previous, frame);
    int expected = Median(recentAdded);

    MatchResult match = FindFrameOverlap(
        previous,
        frame,
        prefix,
        expected);

    if (!match.Success)
    {
        // 스크롤하지 않고 같은 위치에서 재캡처
        frame.Dispose();
        Thread.Sleep(150);
        frame = CaptureRectangle(region);

        prefix = StaticPrefix(previous, frame);
        match = FindFrameOverlap(previous, frame, prefix, expected);
    }

    if (!match.Success)
        break;

    if (match.Added <= 0)
        break;

    accumulator = AppendNewStrip(
        accumulator,
        frame,
        match.Prefix + match.Overlap);

    PushRecentAdded(recentAdded, match.Added, maxCount: 5);

    previous.Dispose();
    previous = frame;
}
```

---

## 17. 구현 예시: 신규 영역만 append

```csharp
private static Bitmap AppendNewStrip(Bitmap accumulator, Bitmap frame, int sourceY)
{
    sourceY = Math.Max(0, Math.Min(sourceY, frame.Height));
    int added = frame.Height - sourceY;
    if (added <= 0)
        return accumulator;

    Bitmap stitched = new Bitmap(
        accumulator.Width,
        accumulator.Height + added,
        PixelFormat.Format32bppArgb);

    using (Graphics g = Graphics.FromImage(stitched))
    {
        g.DrawImageUnscaled(accumulator, 0, 0);

        Rectangle src = new Rectangle(0, sourceY, frame.Width, added);
        Rectangle dst = new Rectangle(0, accumulator.Height, frame.Width, added);
        g.DrawImage(frame, dst, src, GraphicsUnit.Pixel);
    }

    accumulator.Dispose();
    return stitched;
}
```

중요한 점은 **frame 전체를 음수/상향 offset으로 그리는 방식이 아니라 실제 source crop을 사용한다는 것**이다.

---

## 18. 로그 형식 개선 권장

현재 로그에 다음 정보를 추가하면 진단이 훨씬 쉬워진다.

```text
s11 mode=Wheel
prefix=352
selectedOverlap=952
added=400
score=0.998
medianAdded=400
retry=0
outlier=False
```

실패 시:

```text
s6 firstMatch=FAIL
retry=1
retryMatch=SUCCESS
selectedOverlap=...
added=...
```

후보가 여러 개라면 진단 빌드에서만 상위 3개를 기록한다.

```text
candidate1 h=952 added=400 score=0.998 deltaPenalty=0.000
candidate2 h=207 added=1145 score=0.93 deltaPenalty=0.65
```

---

## 19. 검증 시나리오

### A. Chrome / GitHub README

이번 테스트와 동일 계열.

확인사항:

- Chrome 탭/주소창이 결과 중간에 반복되지 않음
- README 제목/섹션이 누락 없이 1회씩 이어짐
- 약 400px 이동이 연속적으로 검출됨
- 마지막 짧은 이동만 작은 `added`로 종료

### B. Edge 긴 웹페이지

- sticky header 있는 페이지
- 없는 페이지
- 이미지 lazy-loading 페이지

### C. Windows Explorer

- 파일 목록
- 행 간격이 반복되는 구조에서 alias 방지 확인

### D. PDF 뷰어

- 페이지 사이 공백 반복
- 텍스트 반복 패턴

### E. 종료/실패

- 페이지 끝 자동 종료
- `Esc` 중단 시 정상 누적 결과 반환
- 매칭 실패 시 손상된 strip을 붙이지 않음
- MaxTotalHeight 도달 시 정상 종료

---

## 20. 성공 판정 기준

1차 안정화 목표는 "모든 앱 100%"가 아니라 다음으로 잡는다.

```text
Chrome / Edge 일반 웹페이지
+ Explorer 목록
+ 일반 Win32/WPF 스크롤 화면
```

에서:

- 중복 header 없음
- 눈에 띄는 seam 없음
- 내용 누락 없음
- 갑작스러운 수백~천px 과다 append 없음
- 페이지 끝 정상 감지

이 수준이면 CapPicker 2.1의 범용 스크롤 캡처로 충분히 실사용 가치가 있다.

---

## 21. 최종 권고

현재 코드를 버리고 앱별 전용 엔진을 만드는 방향은 권장하지 않는다.

이번 자료에서 확인되는 핵심은 **스크롤 자체는 대체로 안정적이며, 스티칭 후보 선택과 합성 방식이 실패의 중심**이라는 점이다.

따라서 다음 5개를 우선 수정한다.

```text
① previous ↔ current frame 매칭
② 고정영역 + overlap을 crop하고 신규 strip만 append
③ 최근 scroll delta median 기반 후보 검증
④ SeamContinues hard reject 제거/완화
⑤ 실패 시 추가 스크롤 전 동일 위치 재캡처
```

이후 실제 테스트 결과를 보고 grayscale/edge signature, child HWND, adaptive settle을 순차적으로 추가하는 것이 가장 가볍고 안전한 개선 경로다.

---

## 22. UI 보완 1: 상하/좌우 반전 아이콘 직관성 개선

### 현행 상태

`MainForm.cs`에서는 다음처럼 반전 기능을 별도 아이콘으로 사용한다.

```csharp
flipHButton = MakeIconButton(AppIcon.FlipHorizontal, L10n.T("좌우 반전", "Flip horizontal"));
flipVButton = MakeIconButton(AppIcon.FlipVertical, L10n.T("상하 반전", "Flip vertical"));
```

실제 도형은 `Ui.cs`의 `AppIcon.FlipHorizontal`, `AppIcon.FlipVertical`에서 직접 그린다.

현행 아이콘은 다음 개념이다.

```text
좌우 반전: 세로 점선 축 + 한쪽 삼각형 1개
상하 반전: 가로 점선 축 + 아래쪽 삼각형 1개
```

특히 `FlipVertical`은 **하나의 하향 삼각형과 점선**으로 보이기 때문에, 일반 사용자에게는 `아래로 이동`, `펼치기`, `다운로드`, `정렬` 계열 아이콘으로 오인될 수 있다. 실제 기능인 "상하 반전"을 즉시 연상시키기 어렵다.

### 개선 원칙

반전 아이콘은 **축을 기준으로 서로 거울상인 두 도형이 동시에 보이는 대칭형**이 가장 직관적이다.

권장 형태:

```text
좌우 반전
◀ │ ▶
또는
▷ │ ◁

상하 반전
  △
───
  ▽
```

실제 렌더링에서는 Unicode 문자를 쓰지 않고 기존 GDI+ 도형으로 그리되, 다음 원칙을 적용한다.

- 좌우 반전: 중앙 세로축 + 좌/우 대칭 삼각형 2개
- 상하 반전: 중앙 가로축 + 상/하 대칭 삼각형 2개
- 한쪽 도형만 채우지 말고 **양쪽의 시각적 무게를 동일하게** 유지
- 점선은 필수 아님. 작은 40×40 버튼에서는 **얇은 실선 축이 더 또렷할 수 있음**
- 회전 아이콘과 동일한 stroke 굵기/색상 사용
- hover/disabled 상태는 기존 `FlatButton` 렌더링을 그대로 사용

### 권장 소스 개선안

`Ui.cs`의 두 case만 교체하면 되며 외부 아이콘 리소스는 필요 없다.

개념 예시:

```csharp
case AppIcon.FlipHorizontal:
{
    int axisX = cx;
    g.DrawLine(p, axisX, t + 2, axisX, bb - 2);

    Point[] left = new Point[] {
        new Point(l + 3, cy),
        new Point(cx - 3, t + 4),
        new Point(cx - 3, bb - 4)
    };
    Point[] right = new Point[] {
        new Point(rr - 3, cy),
        new Point(cx + 3, t + 4),
        new Point(cx + 3, bb - 4)
    };

    g.DrawPolygon(p, left);
    g.DrawPolygon(p, right);
}
break;

case AppIcon.FlipVertical:
{
    int axisY = cy;
    g.DrawLine(p, l + 2, axisY, rr - 2, axisY);

    Point[] top = new Point[] {
        new Point(cx, t + 3),
        new Point(l + 4, cy - 3),
        new Point(rr - 4, cy - 3)
    };
    Point[] bottom = new Point[] {
        new Point(cx, bb - 3),
        new Point(l + 4, cy + 3),
        new Point(rr - 4, cy + 3)
    };

    g.DrawPolygon(p, top);
    g.DrawPolygon(p, bottom);
}
break;
```

위 코드는 개념 예시이며 실제 적용 시 현재 `DrawIcon()`의 `l/t/rr/bb/cx/cy` 범위와 DPI 스케일에 맞춰 1~2px 조정한다.

### 판정

- 기능 변경: 없음
- 리소스 증가: 사실상 없음
- 회귀 위험: 매우 낮음
- 개선 효과: 높음

따라서 **스크롤 캡처 개선과 별개로 함께 반영 권장**한다.

---

## 23. UI 보완 2: 설정 아이콘 모양 개선

### 사용자 지적의 핵심

설정 버튼의 문제는 **아이콘 전용 버튼이라는 점이 아니다.** 오히려 설정은 현재처럼 톱니 아이콘만 두는 편이 적절하다.

문제는 현재 `Ui.cs`의 `AppIcon.Settings`가 통상적인 Windows 설정(cog) 아이콘과 시각적으로 다르게 보여 **아이콘 자체가 어색하고 주변 아이콘과 조화가 떨어지는 점**이다. 따라서 `설정 / Settings` 텍스트를 추가하는 방식은 적용하지 않는다.

현행 코드:

```csharp
case AppIcon.Settings:
{
    // Classic cog: toothed outer ring with center hole.
    int outer = Math.Max(6, r.Width / 2);
    int inner = Math.Max(3, r.Width / 5);
    int tooth = Math.Max(2, r.Width / 8);
    g.DrawEllipse(p, cx - outer / 2, cy - outer / 2, outer, outer);
    for (int i = 0; i < 8; i++)
    {
        double a = Math.PI * i / 4.0;
        int x1 = cx + (int)Math.Round(Math.Cos(a) * (outer / 2.0 - 1));
        int y1 = cy + (int)Math.Round(Math.Sin(a) * (outer / 2.0 - 1));
        int x2 = cx + (int)Math.Round(Math.Cos(a) * (outer / 2.0 + tooth));
        int y2 = cy + (int)Math.Round(Math.Sin(a) * (outer / 2.0 + tooth));
        g.DrawLine(p, x1, y1, x2, y2);
    }
    g.DrawEllipse(p, cx - inner / 2, cy - inner / 2, inner, inner);
}
break;
```

현재 방식은 원 둘레에 방사형 선을 8개 추가하는 형태라, 작은 크기에서는 **톱니바퀴보다 햇살/스프로킷처럼 보일 수 있다.** 특히 다른 CapPicker 아이콘이 비교적 단순하고 정돈된 선형 도형인데 비해 설정 아이콘만 방사형 선이 많아 시각적 밀도가 달라진다.

### 권장 방향

설정 버튼은 다음 원칙으로 유지·개선한다.

- **아이콘 전용 유지**
- `설정` 텍스트 추가하지 않음
- 버튼 크기와 위치는 현행 툴바 체계 유지
- tooltip `설정 / Settings` 유지
- 아이콘만 **통상적인 8톱니 cog outline**으로 교체
- 외곽 톱니는 선을 뻗는 방식이 아니라 **실제 톱니 윤곽**으로 표현
- 중앙 원형 hole은 유지하되 크기를 조금 키워 인식성 확보
- 펜 두께는 주변 아이콘과 동일 계열로 유지

### 권장 소스 개선안

가장 단순한 방법은 `GraphicsPath`로 외곽 톱니 윤곽을 만든 뒤 중앙 원을 그리는 것이다. 별도 이미지 리소스나 아이콘 폰트는 필요 없다.

개념 예시:

```csharp
case AppIcon.Settings:
{
    const int teeth = 8;
    float outerR = Math.Max(7f, r.Width * 0.30f);
    float rootR  = outerR * 0.78f;
    float holeR  = outerR * 0.34f;

    using (GraphicsPath gear = new GraphicsPath())
    {
        PointF[] pts = new PointF[teeth * 4];
        double step = Math.PI * 2.0 / pts.Length;
        double start = -Math.PI / 2.0;

        for (int i = 0; i < pts.Length; i++)
        {
            // 두 점은 바깥쪽, 두 점은 안쪽으로 두어
            // 방사형 선이 아닌 짧고 넓은 톱니 형태를 만든다.
            int phase = i % 4;
            float rad = (phase == 1 || phase == 2) ? outerR : rootR;
            double a = start + i * step;
            pts[i] = new PointF(
                cx + (float)Math.Cos(a) * rad,
                cy + (float)Math.Sin(a) * rad);
        }

        gear.AddPolygon(pts);
        g.DrawPath(p, gear);
    }

    g.DrawEllipse(p,
        cx - holeR, cy - holeR,
        holeR * 2f, holeR * 2f);
}
break;
```

실제 반영 시에는 40×40 버튼에서 가장 자연스럽게 보이도록 `outerR/rootR/holeR` 비율만 미세 조정하면 된다. 핵심은 **현재처럼 원에서 가느다란 선 8개가 튀어나오는 형태를 피하고, 톱니 자체가 하나의 cog 윤곽으로 읽히게 만드는 것**이다.

### 적용 판단

- 기능 변경: 없음
- 버튼 구조 변경: 없음
- 텍스트 추가: 없음
- 리소스 증가: 없음
- 회귀 위험: 매우 낮음
- 개선 효과: 중~높음

따라서 설정 버튼은 **레이아웃이나 텍스트 체계를 바꾸지 말고 `AppIcon.Settings`의 drawing만 교체**하는 것이 가장 적절하다.

---

## 24. UI 보완 적용 우선순위

스크롤 캡처 스티칭은 기능 안정성 이슈이므로 최우선이다. UI 보완은 같은 버전에 함께 넣어도 위험이 낮다.

권장 순서:

```text
[필수]
1. 스티칭 previous ↔ current 구조 전환
2. 신규 strip만 append
3. SeamContinues hard reject 완화
4. median 기반 이상치 차단
5. 실패 시 동일 위치 재캡처

[UI 동시 반영 권장]
6. FlipHorizontal / FlipVertical을 대칭형 표준 아이콘으로 교체
7. 설정 버튼은 icon-only 유지
8. `AppIcon.Settings`를 통상적인 8톱니 cog outline으로 교체
```

UI 6~8번은 캡처/편집 로직과 독립적이므로 스크롤 엔진 수정과 충돌 가능성이 낮다.

---

## 25. 최종 권고 업데이트

CapPicker 2.1 계열의 이번 개선은 **기능을 늘리는 작업보다 이미 추가한 스크롤 캡처를 안정화하고, 툴바의 작은 이질감을 정리하는 작업**으로 보는 것이 적절하다.

최종 목표는 다음과 같다.

```text
스크롤 캡처:
- 앱별 전용 코드 최소화
- previous/current 프레임 기반 범용 stitching
- 중복/누락/고정 헤더 반복 제거
- 실패 시 손상 결과를 붙이지 않음

UI:
- 반전 기능은 한눈에 의미가 전달되는 대칭형 아이콘
- 설정은 icon-only를 유지하되 통상적인 cog 형태로 다시 그려 직관성과 통일감 확보
- 기존 CapPicker의 작은 크기·단일 EXE·직접 GDI 렌더링 구조 유지
```

별도 이미지 리소스나 아이콘 라이브러리를 추가할 필요가 없으므로 **가벼움에는 사실상 영향이 없다.**


---

## 26. 실제 소스 반영 결과 (2026-09-13)

본 문서의 개선안을 기준으로 `P260913_CapPicker2(1).zip` 소스에 다음 사항을 실제 반영하였다.

### ScrollCapture.cs

- `accumulator ↔ frame` 매칭을 제거하고 **`previous ↔ current frame` 인접 프레임 매칭**으로 전환
- `StaticPrefix + overlap` 구간을 다시 그리지 않고 **현재 프레임의 신규 하단 strip만 append**하도록 변경
- 기존 `SeamContinues()` hard reject 제거
- 최근 정상 스크롤 이동량 5건의 **median**을 연속성 힌트로 사용
- 연속성 탐색 실패 시 전체 overlap 범위를 다시 탐색하는 fallback 유지
- 정확한 ARGB 동일 비교 대신 **RGB/Luma 허용오차 기반 시각적 비교** 적용
- 매칭 실패 시 추가 스크롤하지 않고 **동일 위치에서 최대 2회 재캡처** 후 재시도
- 캡처 영역 중앙의 실제 child HWND를 우선 스크롤 대상으로 선택하고, 실패 시 선택된 top-level HWND 사용
- 최대 50장 / 16,000px 제한 및 Esc 중단은 그대로 유지

### Ui.cs

- `AppIcon.Settings`: 원 + 방사형 선 구조를 제거하고 **8톱니 cog outline + 중앙 hole** 형태로 변경
- `AppIcon.FlipHorizontal`: 세로 대칭축 양쪽의 두 삼각형으로 변경
- `AppIcon.FlipVertical`: 가로 대칭축 위·아래의 두 삼각형으로 변경
- 외부 PNG/SVG/icon font를 추가하지 않고 기존 GDI+ 직접 렌더링 구조 유지

## 27. 제공된 실패 프레임 재검증

기존 실패 실행의 `scroll-dbg-1.png ~ scroll-dbg-14.png`에 새 알고리즘을 동일하게 적용해 재구성 검증하였다.

기존 로그의 대표 오류:

```text
s11: overlap=207 / bestH=952 / best=0.998
기존 최종 높이: 6,586px
```

새 인접 프레임 매칭에서 검출된 실제 신규 높이(스크롤 delta)는 대략 다음과 같았다.

```text
400, 400, 400, 400, 400, 399, 400, 400, 400, 400, 161px
```

재구성 결과 높이는 **5,864px**였으며, 기존 결과에서 발생했던 Chrome 상단 고정영역의 반복 삽입과 s11의 과도한 1,145px 추가가 제거되는 것을 확인하였다.

즉 제공된 실제 실패 샘플에 대해서는 본 개선 방향이 단순 이론이 아니라 **스티칭 결과 자체를 정상화하는 방향으로 검증**되었다.

> 단, 현재 실행 환경에서는 Windows용 `csc.exe` 실빌드를 수행할 수 없으므로 최종 바이너리 검증은 Windows에서 `BUILD.cmd`로 확인해야 한다. 정적 구문/괄호 구조 및 대상 코드 반영 여부는 별도 검사 완료.
