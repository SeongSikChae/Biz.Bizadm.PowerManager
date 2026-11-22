# 장비명·입력 단위 표기·제어 화면 로딩 개선 계획

## 요약

- **서버**: `Devices` 테이블에 `Name` 열을 더하고(마이그레이션), 관리 포트에 `PUT /api/admin/devices/{mac}/name`을 둔다. 장비 목록·상태 조회·상태 스트림의 원소에 `name`을 더한다. 장비명이 바뀌면 기존 변경 알림(`DeviceStateStore.NotifyChanged`)으로 스트림에 보낸다.
- **로딩**: 설정 조회를 `DeviceControl`로 올리고, 세 설정 묶음을 응답과 관계없이 그린다. 묶음은 설정이 준비됐을 때 한 번 입력을 채운다. 설정 묶음 자리의 스켈레톤(`SettingsSections`)을 없앤다.
- **단위·범위**: 입력 이름·설명을 한 파일에 모으고 모델로 고른다. `Field`가 설명을 입력 아래에 늘 보이고 오류는 그 아래에 보인다.
- **장비명 화면**: 머리의 "이름 변경" 창, 카드·확인 창·목록의 이름 표시, 대시보드 순서.

## 바꿀 파일·구조

서버:

- src/Biz.Bizadm.PowerManager.Server.Spa/Data/DeviceRegistration.cs: `Name`(기본 빈 문자열)
- src/Biz.Bizadm.PowerManager.Server.Spa/Data/PowerManagerDbContext.cs: `Name` 최대 길이 30, 필수
- src/Biz.Bizadm.PowerManager.Server.Spa/Data/Migrations/<시각>_DeviceName.cs, <시각>_DeviceName.Designer.cs(새, `dotnet ef migrations add DeviceName --output-dir Data/Migrations`로 생성)
- src/Biz.Bizadm.PowerManager.Server.Spa/Data/Migrations/PowerManagerDbContextModelSnapshot.cs: 위 명령이 고침
- src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceName.cs(새): 장비명의 공백 제거와 검사
- src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceNameRequest.cs(새): 요청 본문
- src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceRegistrationService.cs: 장비명 저장 `RenameAsync`
- src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceCommandService.cs: `DeviceCommandResult`에 `InvalidName` 결과 추가
- src/Biz.Bizadm.PowerManager.Server.Spa/Devices/AdminDevicesController.cs: 장비명 저장 API, 목록 원소에 `name`
- src/Biz.Bizadm.PowerManager.Server.Spa/Devices/AdminDeviceResponse.cs, DeviceStatusResponse.cs: `Name`
- src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceStatusService.cs: 조회에 `Name`
- tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DeviceNameApiTests.cs(새): C10~C13
- tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/AdminDeviceEndpointTests.cs, DeviceStatusApiTests.cs: 원소 필드 목록에 `name`

클라이언트(`src/Biz.Bizadm.PowerManager.Client/src/` 아래):

- components/ui/dialog.tsx(새, shadcn CLI로 추가)
- features/device-status/types.ts: `DeviceStatus.name`
- features/device-status/deviceStatusApi.ts: `saveName`
- features/device-status/deviceStatusSlice.ts: `deviceRenamed`
- features/device-status/format.ts, format.test.ts: `displayName`, 순서 `sortByName`(`sortByMac` 대체), `deviceLabel` 삭제
- features/device-status/DeviceLabel.tsx(새): 확인 창의 장비 표시와 제목 아래 줄
- features/device-status/ConfirmDialog.tsx, RelayControl.test.tsx: 장비 표시, 테스트 원소에 `name`
- features/dashboard/DashboardPage.tsx, DeviceCard.tsx, DashboardPage.test.tsx
- features/device-control/fields.ts(새), fields.test.ts(새): 입력 이름·설명, `isB540`
- features/device-control/deviceName.ts(새), deviceName.test.ts(새): 화면의 장비명 공백 제거와 검사
- features/device-control/DeviceNameDialog.tsx(새): 이름 변경 창
- features/device-control/DeviceControlPage.tsx, PowerSection.tsx, ReportConditionForm.tsx, StandbyCutoffForm.tsx, ScheduleForm.tsx, DeviceControlPage.test.tsx
- features/devices/devicesApi.ts, deviceList.ts, deviceList.test.ts, DevicesPage.tsx, DevicesPage.test.tsx
- App.test.tsx: 테스트 원소에 `name`(필요할 때)

문서:

- docs/reference/mqtt-device-control.md, docs/reference/b540-mqtt-topic.md: 최소·최대 간격과 지연의 관찰
- docs/features/device-control.md, docs/features/device-dashboard.md, docs/features/device-list.md, docs/features/device-register.md, docs/features/admin-ui.md: 요구·이력·`파일:` 줄

## 결정

- 장비명은 `Devices` 테이블의 열로 둔다: 장비마다 하나이고 목록·상태 조회가 이미 이 테이블을 읽는다 / 검토한 대안: 별도 테이블(조인만 늘고 얻는 것이 없음) (D002, D003)
- 저장은 `DeviceRegistrationService.RenameAsync`가 한다: 등록 정보를 쓰는 곳을 한 군데로 두고, 추적한 엔터티의 바뀐 열만 UPDATE되므로 장치의 재등록과 겹쳐도 서로의 열을 덮지 않는다 / 검토한 대안: 컨트롤러에서 직접 저장(알림과 저장 순서가 흩어짐)
- 결과는 기존 `DeviceCommandResult`를 쓴다: mac·등록 판정의 문구와 코드가 제어 API와 같다 / 검토한 대안: 새 결과 타입(같은 값의 중복)
- 앞뒤 공백 제거는 서버 `string.Trim()`(제거 문자가 `char.IsWhiteSpace`와 같다. .NET 문서 `String.Trim`), 화면은 같은 문자를 적은 정규식을 쓴다: JS `trim`은 U+FEFF를 없애고 U+0085를 남겨 다르다 / 검토한 대안: 화면은 검사하지 않고 서버 400만 보임(C15의 이유 표시를 채우지 못함) (D008)
- 화면은 저장 성공 뒤 slice의 장비명을 바로 바꾼다(`deviceRenamed`): 스트림이 끊겨 있어도 머리가 바뀐다(C15). 스트림의 새 원소는 그대로 덮는다 / 검토한 대안: 스트림만 기다림(끊기면 옛 이름)
- 장비 목록은 열 때마다 다시 읽는다(`refetchOnMountOrArgChange: true`): 다른 탭에서 바꾼 이름도 보인다. 기존 목록을 보이며 갱신하는 동작은 device-list R10 그대로다 / 검토한 대안: 저장 뒤 목록 캐시를 무효화(같은 탭에서만 유효하고 두 API가 얽힘)
- 설정 묶음은 다시 마운트하지 않고, 설정이 준비된 첫 렌더에서 입력을 한 번 채운다(`RelayControl`이 쓰는 렌더 중 상태 맞춤과 같은 방식): 불러오는 중에 보낸 예약 삭제의 진행·결과 상태가 남아야 한다(C4) / 검토한 대안: `key`로 다시 마운트(삭제 결과가 사라짐) (D001)
- 예약 묶음은 설정이 준비되기 전에 삭제가 성공했으면 채우기를 건너뛴다(C4). 삭제 응답보다 설정 응답이 먼저 오면 채운 뒤, 삭제 성공 때 비운다(기존 동작) / 검토한 대안: 삭제 뒤 설정을 다시 읽음(요청이 늘고 순서가 더 복잡해짐)
- 입력의 비활성은 입력마다 `disabled`로 한다: 묶음 틀과 배치가 바뀌지 않는다 / 검토한 대안: `<fieldset disabled>`(버튼·배치까지 영향)
- 이름 변경 창은 shadcn `dialog`를 쓴다: 확인 창(`alert-dialog`)은 되돌릴 수 없는 동작의 확인용이다. admin-ui R4의 부품 목록에 더한다 / 검토한 대안: `alert-dialog`에 입력을 넣음(역할이 맞지 않음), 머리에서 바로 편집(실수로 바뀌기 쉽고 오류 자리가 없음) (D002)
- 이름 순서는 `Intl.Collator("ko", { numeric: true })`로 비교하고, 같으면 mac으로 가른다: spec의 한국어 순서·숫자 비교 / 검토한 대안: 문자열 비교(`거실 10`이 `거실 2`보다 앞) (D004)
- 카드의 접근성 이름(`aria-label`)은 mac 그대로 둔다: 이름이 겹칠 수 있어(D003) 카드를 유일하게 가리킨다 / 검토한 대안: 장비명(중복 시 구분 불가)

## 인터페이스·데이터

서버:

```csharp
// DeviceName.cs
public static class DeviceName
{
    public const int MaxLength = 30;
    // null이면 false. 앞뒤 공백을 없앤 값의 길이가 30을 넘거나 제어 문자(char.IsControl)·U+2028·U+2029가 있으면 false.
    public static bool TryNormalize(string? value, [NotNullWhen(true)] out string? name);
}

// DeviceNameRequest.cs
public sealed record DeviceNameRequest(string? Name);

// DeviceRegistrationService: mac 형식 → 등록 → 장비명 순서로 판정한다. 값이 같으면 저장·알림 없이 성공.
public Task<DeviceCommandResult> RenameAsync(string mac, string? name, CancellationToken cancellationToken);
```

- mac 형식 판정은 기존 제어 API와 같다(`DeviceIdentity.TryNormalizeMac` 결과가 받은 값과 같아야 함).
- `PUT /api/admin/devices/{mac}/name` → 204, 400·404는 ProblemDetails. `AdminDevicesController`의 `[AdminPortOnly]`가 장치 포트를 404로 끝낸다.
- 저장 뒤 `states.NotifyChanged(mac)`. 저장 전에 알리지 않아, 스트림이 알림을 받고 읽는 값은 새 이름이다.
- `AdminDeviceResponse(Mac, Name, UserId, …)`, `DeviceStatusResponse(Mac, Name, ModelId, …)`.

클라이언트:

```ts
// fields.ts
export function isB540(modelId: string): boolean;
export interface FieldText { label: string; hint: string }
export type FieldKey = "powerCycle" | "minInterval" | "maxInterval" | "changeThreshold" | "thresholdWatts" | "delay"
  | "hour" | "minute" | "second" | "seconds";
export function fieldText(key: FieldKey, modelId: string): FieldText; // spec C5·C6 표

// deviceName.ts
export const MAX_NAME_LENGTH = 30;
export function normalizeName(text: string): string;       // .NET char.IsWhiteSpace 문자를 앞뒤에서 없앰
export function validateName(name: string): string | null; // 없앤 값의 오류 문구, 없으면 null

// 설정 묶음 셋이 받는 값. ready는 이번에 읽은 설정이 있다는 뜻이고, 그때 initial이 정해진다.
interface SettingsProps<T> { device: DeviceStatus; initial: T | null; ready: boolean; loading: boolean; note: string }
```

- `ready = data !== undefined && !isFetching`, `loading = isFetching`. 실패하면 둘 다 false다.
- 오류 안내는 `!ready && error !== undefined`일 때 보인다. 다시 읽는 동안에도 남는 까닭은 RTK Query가 조회 성공 때만 `error`를 지우기 때문이다(`@reduxjs/toolkit` 2.14.0 `src/query/core/buildSlice.ts`의 `delete substate.error`). `DevicesPage`도 같은 동작에 기댄다.
- `Field`에 `hint`(필수)와 `disabled`를 더한다. `aria-describedby`는 설명 id, 오류가 있으면 오류 id를 이어 적는다.
- `Section`에 `loading`을 더한다. 버튼 줄에 "설정을 불러오는 중"(`role="status"`)을 보이고 묶음에 `aria-busy`를 둔다. 글자 줄 높이가 버튼 높이(32px)보다 작고, 성공 안내("장비에 보냈습니다")가 있는 동안에는 화면에서만 숨겨(`sr-only`, 읽기 프로그램에는 남음) 버튼 줄이 두 줄이 되지 않으므로 묶음 높이가 바뀌지 않는다(D010).
- `DeviceLabel`: `장비명 — 모델 (mac)` 또는 `모델 (mac)`, mac은 `font-mono`. `DeviceSubtitle`: 장비명이 있으면 `모델 · mac`, 없으면 mac. 제목은 `displayName(device)`(장비명, 없으면 모델).
- 긴 장비명은 제목·확인 창·이름 변경 창에 `wrap-anywhere`(Tailwind 4.1)로 줄을 바꾼다.
- 이름 변경 창은 열 때 안쪽 폼을 새로 마운트해 지금 장비명으로 시작한다. 폼 제출(Enter 포함)로 저장하고, 보내는 중의 제출은 무시한다. 보내는 중에 "취소"·Esc로 닫으면 창만 닫히고 요청은 그대로 끝난다(성공하면 머리가 바뀜).
- `DevicesPage` 표 최소 폭은 720px에서 840px로 키운다.

## 오류·위험

- `dotnet ef`: 프로젝트 폴더의 `dotnet-tools.json`은 `dotnet-ef` 11.0.0, 전역 도구는 10.0.10이다(`dotnet tool list -g`). 패키지는 10.0.15다. 어느 쪽으로도 생성하지 못하면 멈추고 사용자에게 묻는다. 생성물은 `AddColumn<string>("Name", maxLength: 30, nullable: false, defaultValue: "")`만 있어야 하며, 다르면 알린다.
- shadcn CLI는 원격 레지스트리에서 `dialog`를 받는다(미확인: 지금 받을 수 있는지). 받지 못하면 멈추고 사용자에게 묻는다. 생성물이 새 의존성을 넣으려 하면 받지 않는다(`package.json` diff로 알아챔). React 18의 ref 경고가 나면 `forwardRef`로 보정한다(`frontend/react-project-creation.md` 6번).
- 장비명 저장과 장치의 재등록이 겹침: 두 요청이 서로 다른 열만 UPDATE한다. 테스트로 재등록 뒤 장비명 유지와, 장비명 저장 뒤 다른 값 유지를 확인한다.
- 스트림에서 저장 전 값으로 만든 원소가 저장 성공 처리 뒤에 도착하면 잠깐 옛 이름이 보일 수 있다. 저장 뒤의 알림으로 새 원소가 이어 오므로 남지 않는다.
- 기존 테스트는 입력 이름(`전원 재투입`, `최소 간격` 등)과 "설정 묶음이 나타나면 준비 완료"에 기대므로 함께 고친다. 준비 완료는 "설정을 불러오는 중"이 사라지는 것으로 기다린다.
- 본문이 아예 없거나 JSON이 깨진 요청은 `[ApiController]`가 동작 전에 400으로 끝내 mac·등록 판정보다 앞선다. 기존 제어 API와 같고, C11의 판정 순서는 해석할 수 있는 본문에 대한 것이다.
- 서버를 새로 배포하면 DB에 열이 생긴다. 이전 실행 파일로 되돌리면 새 장비의 첫 등록이 실패할 수 있다(`Name`이 NOT NULL이고 이전 코드는 값을 넣지 않음. 마이그레이션의 `defaultValue`가 DB 기본값으로 남는지는 생성된 DDL로 확인). 배포는 이 작업에 넣지 않는다.

## 검증 전략

- C1·C3·C4·C5·C7·C8: `DeviceControlPage.test.tsx`. 설정 응답을 늦추거나 실패시키고, 응답 전·후·재시도의 입력·버튼·안내와 삭제 순서를 확인한다.
- C2: 값과 활성은 `DeviceControlPage.test.tsx`. 위치와 크기는 헤드리스 Edge(임시 프로필)로 빌드한 화면에서 설정 응답을 2초 늦춰 1440px·800px·390px의 네 묶음 좌표를 응답 전·후로 비교한다. 390px에서는 응답 전에 예약 삭제가 성공한 때의 좌표도 함께 비교한다.
- C6: `fields.test.ts`(문구 전체)와 `DeviceControlPage.test.tsx`(설명 연결).
- C9: 고친 문장을 읽고, 근거 문서에서 "보고 조건", "지연", 12자리 16진수를 검색한다.
- C10~C13: `DeviceNameApiTests`. C10은 `InitialDevices`까지만 마이그레이션한 DB에 장비를 넣고 앱을 시작해 확인한다. C12는 재등록 전후, 장비명 저장 전후의 행과 응답을 비교한다. C13은 스트림에서 변경 원소를 기다리고, 같은 값 저장 뒤에는 다른 장비의 변경만 오는지로 확인한다.
- C14~C17: `DeviceControlPage.test.tsx`, `DashboardPage.test.tsx`, `RelayControl.test.tsx`, `format.test.ts`, `deviceName.test.ts`.
- C18: `DevicesPage.test.tsx`, `deviceList.test.ts`.
- C19: 헤드리스 Edge 390px에서 공백 없는 30자 이름으로 `scrollWidth`와 요소 폭을 확인한다.
- C20: `dotnet test --solution Biz.Bizadm.PowerManager.slnx`, `npm test`, `npm run lint`, `npm run build`.
- C21: 기준 spec 다섯 개를 읽고 `파일:` 줄의 경로가 모두 있는지 확인한다.
- 화면 확인은 실장비와 운영 서버를 쓰지 않고, 빌드한 화면에 가짜 API 응답(요청 가로채기)을 넣어서 한다.

## 원칙 점검

- 적용 규칙: workflow/spec-driven.md와 하위 문서, git/github-issue.md, git/github-access.md, git/commit-message.md, workflow/repository-content.md, workflow/conversation-language.md, shell/powershell-windows.md(`dotnet`·`npm` 명령은 PowerShell로 실행), frontend/react-project-creation.md(기존 프로젝트에 기능 추가이므로 구조·버전 유지, 6번 shadcn CLI와 React 18 ref 보정), 저장소 기준 spec(admin-ui R1·R2·R4·R5·R6)
- 충돌: 없음. admin-ui R4(부품 목록)·R5(스켈레톤)는 이번에 기준 spec을 바꿔 맞춘다(spec C21은 R5를 밝혔고, R4는 `dialog` 부품을 더한다).
