import { CircleAlert, Plug } from "lucide-react";
import { useAppSelector } from "@/app/hooks";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { useGetStatusesQuery } from "@/features/device-status/deviceStatusApi";
import { describeApiError, formatSummary, sortByMac } from "@/features/device-status/format";
import { useNow } from "@/features/device-status/useDeviceStatusStream";
import { DeviceCard } from "./DeviceCard";

const GRID = "grid grid-cols-[repeat(auto-fill,minmax(min(100%,18rem),1fr))] gap-4";

function CardsSkeleton() {
  return (
    <div role="status" aria-label="장비 상태를 불러오는 중" className={GRID}>
      {[0, 1, 2].map((i) => (
        <Card key={i}>
          <CardHeader>
            <Skeleton className="h-4 w-24" />
            <Skeleton className="h-3 w-32" />
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <Skeleton className="h-9 w-36" />
            <Skeleton className="h-3 w-full" />
            <Skeleton className="h-5 w-20" />
          </CardContent>
        </Card>
      ))}
    </div>
  );
}

/** 등록된 장비 전체의 상태와 릴레이 제어. 상태는 앱이 받는 스트림에서 온다. */
export function DashboardPage({ getNow = Date.now }: { getNow?: () => number }) {
  const { devices, received, connection } = useAppSelector((state) => state.deviceStatus);
  const { error, isFetching, refetch } = useGetStatusesQuery();
  const now = useNow(getNow);
  const cards = sortByMac(devices);

  return (
    <>
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div className="flex flex-col gap-1">
          <h1 className="text-2xl font-semibold tracking-tight">대시보드</h1>
          {received && <p className="text-muted-foreground">{formatSummary(cards)}</p>}
        </div>
        {connection !== "connecting" && (
          <p className="text-sm text-muted-foreground" aria-live="polite">
            {connection === "open" ? "실시간 연결됨" : "연결 끊김 — 다시 연결 중"}
          </p>
        )}
      </header>

      {!received ? (
        error !== undefined && !isFetching ? (
          <Alert variant="destructive" className="py-6">
            <CircleAlert aria-hidden="true" />
            <AlertTitle>장비 상태를 불러오지 못했습니다</AlertTitle>
            <AlertDescription className="flex flex-col items-start gap-3">
              <span>{describeApiError(error)}</span>
              <Button variant="outline" onClick={() => refetch()}>
                다시 시도
              </Button>
            </AlertDescription>
          </Alert>
        ) : (
          <CardsSkeleton />
        )
      ) : cards.length === 0 ? (
        <div className="flex flex-col items-center gap-3 rounded-lg border border-dashed px-6 py-16 text-center">
          <span className="flex size-12 items-center justify-center rounded-xl bg-muted text-muted-foreground">
            <Plug aria-hidden="true" className="size-5" />
          </span>
          <p className="text-base font-semibold">등록된 장비가 없습니다</p>
          <p className="max-w-sm text-muted-foreground">장비 설정 도구로 장비를 등록하면 여기에 나타납니다.</p>
        </div>
      ) : (
        <div className={GRID}>
          {cards.map((device) => (
            <DeviceCard key={device.mac} device={device} now={now} />
          ))}
        </div>
      )}
    </>
  );
}
