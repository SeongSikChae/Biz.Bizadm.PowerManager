import { Link } from "react-router";
import { Button } from "@/components/ui/button";
import { Card, CardAction, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from "@/components/ui/card";
import { formatCurrent, formatLastReport, formatPower, formatPowerFactor, formatVoltage } from "@/features/device-status/format";
import { RelayControl } from "@/features/device-status/RelayControl";
import type { DeviceStatus } from "@/features/device-status/types";
import { displayValue } from "@/features/devices/deviceList";
import { cn } from "@/lib/utils";

/** 온라인 여부를 색 점과 글자로 보인다. */
export function OnlineStatus({ online }: { online: boolean }) {
  return (
    <span className="inline-flex items-center gap-1.5 text-sm text-muted-foreground">
      <span
        aria-hidden="true"
        data-slot="online-dot"
        className={cn("size-2 rounded-full", online ? "bg-success" : "bg-muted-foreground/40")}
      />
      {online ? "온라인" : "오프라인"}
    </span>
  );
}

function Meter({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex min-w-0 flex-col gap-0.5">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="tabular-nums">{value}</dd>
    </div>
  );
}

/** 대시보드의 장비 카드. 오프라인 장비의 계량 값은 흐리게 보인다. */
export function DeviceCard({ device, now }: { device: DeviceStatus; now: number }) {
  return (
    <Card role="article" aria-label={device.mac}>
      <CardHeader>
        <CardTitle>{displayValue(device.modelId)}</CardTitle>
        <CardDescription className="font-mono">{device.mac}</CardDescription>
        <CardAction>
          <OnlineStatus online={device.online} />
        </CardAction>
      </CardHeader>
      <CardContent className="flex grow flex-col gap-4">
        <dl className={cn("flex flex-col gap-3", !device.online && "opacity-50")} data-testid="meters">
          <div className="flex flex-col">
            <dt className="text-xs text-muted-foreground">전력</dt>
            <dd className="text-3xl font-semibold tracking-tight tabular-nums">{formatPower(device.power)}</dd>
          </div>
          <div className="grid grid-cols-3 gap-2">
            <Meter label="전압" value={formatVoltage(device.voltage)} />
            <Meter label="전류" value={formatCurrent(device.current)} />
            <Meter label="역률" value={formatPowerFactor(device.powerFactor)} />
          </div>
        </dl>
        <RelayControl device={device} />
      </CardContent>
      <CardFooter className="justify-between gap-2">
        <span className="text-xs text-muted-foreground">{formatLastReport(device.lastReportAt, now)}</span>
        <Button asChild variant="outline" size="sm">
          <Link to={`/devices/${device.mac}`}>제어</Link>
        </Button>
      </CardFooter>
    </Card>
  );
}
