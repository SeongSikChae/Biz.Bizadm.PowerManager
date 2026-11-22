import { CircleAlert, Info, Plug } from "lucide-react";
import { Link, useParams } from "react-router";
import { useAppSelector } from "@/app/hooks";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { OnlineStatus } from "@/features/dashboard/DeviceCard";
import { DeviceSubtitle } from "@/features/device-status/DeviceLabel";
import { useGetSettingsQuery, useGetStatusesQuery } from "@/features/device-status/deviceStatusApi";
import { describeApiError, displayName, formatLastReport, formatPower, uncontrollableReason } from "@/features/device-status/format";
import type { DeviceStatus } from "@/features/device-status/types";
import { useNow } from "@/features/device-status/useDeviceStatusStream";
import { DeviceNameDialog } from "./DeviceNameDialog";
import { PowerSection } from "./PowerSection";
import { ReportConditionForm } from "./ReportConditionForm";
import { ScheduleForm } from "./ScheduleForm";
import { StandbyCutoffForm } from "./StandbyCutoffForm";

const SETTINGS_NOTE = "서버가 마지막으로 보낸 값이며 장비의 현재 값과 다를 수 있습니다";
/**
 * 묶음을 놓는 격자. 최대 폭 1120px에 두 열이고, 열 최소 폭 27rem은 세 열이 들어가지 않는 값이다.
 * 같은 줄의 묶음은 격자의 늘림으로 높이가 같다.
 */
const COLUMNS = "grid w-full max-w-[70rem] grid-cols-[repeat(auto-fit,minmax(min(100%,27rem),1fr))] gap-4";

function DashboardLink() {
  return (
    <Link
      to="/"
      className="self-start rounded-md text-sm text-muted-foreground outline-none hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50"
    >
      ← 대시보드
    </Link>
  );
}

function LoadError({
  title,
  message,
  retrying = false,
  onRetry,
}: {
  title: string;
  message: string;
  retrying?: boolean;
  onRetry: () => void;
}) {
  return (
    <Alert variant="destructive" className="py-6">
      <CircleAlert aria-hidden="true" />
      <AlertTitle>{title}</AlertTitle>
      <AlertDescription className="flex flex-col items-start gap-3">
        <span>{message}</span>
        <Button variant="outline" disabled={retrying} onClick={onRetry}>
          다시 시도
        </Button>
      </AlertDescription>
    </Alert>
  );
}

function LoadSkeleton() {
  return (
    <div role="status" aria-label="장비 상태를 불러오는 중" className={COLUMNS}>
      <Skeleton className="h-48 w-full" />
      <Skeleton className="h-48 w-full" />
    </div>
  );
}

/**
 * 장비 한 대의 머리와 네 묶음. 묶음은 설정 응답과 관계없이 바로 그리고, 설정 묶음은 응답이 오면 그 값으로 채운다.
 * 오류 판정은 `error`로 한다: 다시 읽는 동안에도 성공할 때까지 남는다.
 */
function DeviceControl({ device, now }: { device: DeviceStatus; now: number }) {
  const { data, error, isFetching, refetch } = useGetSettingsQuery(device.mac, { refetchOnMountOrArgChange: true });
  const ready = data !== undefined && !isFetching;
  const reason = uncontrollableReason(device);
  const settings = { device, ready, loading: isFetching, note: SETTINGS_NOTE };
  return (
    <>
      <header className="flex w-full max-w-[70rem] flex-col gap-3">
        <DashboardLink />
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div className="flex min-w-0 flex-col gap-1">
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
              <h1 className="min-w-0 text-2xl font-semibold tracking-tight wrap-anywhere">{displayName(device)}</h1>
              <DeviceNameDialog device={device} />
            </div>
            <p className="wrap-anywhere text-muted-foreground">
              <DeviceSubtitle device={device} />
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-x-4 gap-y-1">
            <OnlineStatus online={device.online} />
            <span className="font-semibold tabular-nums">{formatPower(device.power)}</span>
            <span className="text-sm text-muted-foreground">{formatLastReport(device.lastReportAt, now)}</span>
          </div>
        </div>
      </header>
      {reason !== null && (
        <Alert>
          <Info aria-hidden="true" />
          <AlertTitle>{reason}</AlertTitle>
        </Alert>
      )}
      {!ready && error !== undefined && (
        <div className="w-full max-w-[70rem]">
          <LoadError
            title="설정을 불러오지 못했습니다"
            message={describeApiError(error)}
            retrying={isFetching}
            onRetry={() => void refetch()}
          />
        </div>
      )}
      <div className={COLUMNS}>
        <PowerSection device={device} />
        <ReportConditionForm {...settings} initial={data?.reportCondition ?? null} />
        <StandbyCutoffForm {...settings} initial={data?.standbyCutoff ?? null} />
        <ScheduleForm {...settings} initial={data?.schedule ?? null} />
      </div>
    </>
  );
}

/** 장비 한 대의 제어 화면. 상태는 앱이 받는 스트림에서 온다. */
export function DeviceControlPage({ getNow = Date.now }: { getNow?: () => number }) {
  const { mac = "" } = useParams();
  const device = useAppSelector((state) => state.deviceStatus.devices[mac] as DeviceStatus | undefined);
  const received = useAppSelector((state) => state.deviceStatus.received);
  const { error, isFetching, refetch } = useGetStatusesQuery();
  const now = useNow(getNow);

  if (device !== undefined) return <DeviceControl key={mac} device={device} now={now} />;
  if (received) {
    return (
      <div className="flex flex-col items-center gap-3 rounded-lg border border-dashed px-6 py-16 text-center">
        <span className="flex size-12 items-center justify-center rounded-xl bg-muted text-muted-foreground">
          <Plug aria-hidden="true" className="size-5" />
        </span>
        <p className="text-base font-semibold">장비를 찾을 수 없습니다</p>
        <Button asChild variant="outline">
          <Link to="/">대시보드로 가기</Link>
        </Button>
      </div>
    );
  }
  return (
    <>
      <DashboardLink />
      {error !== undefined && !isFetching ? (
        <LoadError title="장비 상태를 불러오지 못했습니다" message={describeApiError(error)} onRetry={() => void refetch()} />
      ) : (
        <LoadSkeleton />
      )}
    </>
  );
}
