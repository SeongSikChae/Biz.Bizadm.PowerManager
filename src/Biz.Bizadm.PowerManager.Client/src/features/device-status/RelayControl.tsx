import { useEffect, useId, useState } from "react";
import { CircleAlert, LoaderCircle } from "lucide-react";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Switch } from "@/components/ui/switch";
import { ConfirmDialog } from "./ConfirmDialog";
import { useSetRelayMutation } from "./deviceStatusApi";
import { describeApiError, relayCacheKey } from "./format";
import type { DeviceStatus } from "./types";

/** 보낸 뒤 릴레이 상태가 바뀌기를 기다리는 시간(ms). */
const RELAY_TIMEOUT = 10_000;
const TIMEOUT_MESSAGE = "10초 안에 릴레이 상태가 바뀌지 않았습니다.";

interface SentRelay {
  on: boolean;
  sentAt: number;
  /** 실패 원인. null이면 진행 중이다. */
  error: string | null;
}

/** 10초 초과. 그 요청이 아직 진행 중일 때만 실패로 바꾼다(이미 끝났거나 새로 보냈으면 그대로). */
function timeOut(current: SentRelay | null, sent: SentRelay): SentRelay | null {
  return current !== null && current.sentAt === sent.sentAt && current.error === null ? { ...current, error: TIMEOUT_MESSAGE } : current;
}

/** API 실패. 보낸 값과 같은 상태라 이미 끝났어도 알린다(새로 보냈거나 이미 실패했으면 그대로). */
function fail(current: SentRelay | null, sent: SentRelay, error: string): SentRelay | null {
  if (current === null) return { ...sent, error };
  return current.sentAt === sent.sentAt && current.error === null ? { ...current, error } : current;
}

/**
 * 릴레이 제어. 상태를 알면 스위치, 모르면 켜기·끄기 버튼이고, 끄기만 확인 창을 거친다.
 * 보낸 값으로 바뀐 것을 스트림으로 받을 때까지 비활성이며, API가 실패하거나 10초 안에 바뀌지 않으면 오류를 알린다.
 */
export function RelayControl({ device, disabled: blocked = false }: { device: DeviceStatus; disabled?: boolean }) {
  const [setRelay] = useSetRelayMutation({ fixedCacheKey: relayCacheKey(device.mac) });
  const [sent, setSent] = useState<SentRelay | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const labelId = useId();

  // 보낸 값이 되면 끝난다. 렌더링 중 상태를 맞춰 한 번 더 그린다.
  if (sent !== null && sent.error === null && device.relay === (sent.on ? "on" : "off")) setSent(null);
  const pending = sent !== null && sent.error === null;
  const error = sent?.error ?? null;

  useEffect(() => {
    if (sent === null || sent.error !== null) return;
    const timer = setTimeout(() => setSent((current) => timeOut(current, sent)), RELAY_TIMEOUT);
    return () => clearTimeout(timer);
  }, [sent]);

  const send = async (on: boolean) => {
    const attempt: SentRelay = { on, sentAt: performance.now(), error: null };
    setSent(attempt);
    const result = await setRelay({ mac: device.mac, on });
    if (result.error !== undefined) {
      const message = describeApiError(result.error);
      setSent((current) => fail(current, attempt, message));
    }
  };

  const request = (on: boolean) => {
    if (on) void send(true);
    else setConfirmOpen(true);
  };

  const disabled = !device.controllable || pending || blocked;
  return (
    <div className="flex flex-col gap-2">
      <div className="flex min-h-8 items-center gap-2.5">
        {device.relay === "unknown" ? (
          <>
            <span className="text-muted-foreground">상태 모름</span>
            <Button variant="outline" size="sm" disabled={disabled} onClick={() => request(true)}>
              켜기
            </Button>
            <Button variant="outline" size="sm" disabled={disabled} onClick={() => request(false)}>
              끄기
            </Button>
          </>
        ) : (
          <>
            <Switch aria-label="릴레이" aria-describedby={labelId} checked={device.relay === "on"} disabled={disabled} onCheckedChange={request} />
            <span id={labelId}>{device.relay === "on" ? "켜짐" : "꺼짐"}</span>
          </>
        )}
        {pending && (
          <span role="status" className="inline-flex items-center gap-1.5 text-muted-foreground">
            <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
            {sent.on ? "켜는 중" : "끄는 중"}
          </span>
        )}
      </div>
      {error !== null && (
        <Alert variant="destructive">
          <CircleAlert aria-hidden="true" />
          <AlertTitle>릴레이를 제어하지 못했습니다</AlertTitle>
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}
      <ConfirmDialog
        device={device}
        open={confirmOpen}
        onOpenChange={setConfirmOpen}
        title="릴레이를 끌까요?"
        description="연결된 기기의 전원이 꺼집니다."
        confirmLabel="끄기"
        onConfirm={() => void send(false)}
      />
    </div>
  );
}
