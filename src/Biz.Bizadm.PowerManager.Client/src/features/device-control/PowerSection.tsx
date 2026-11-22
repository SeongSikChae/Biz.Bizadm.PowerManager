import { useId, useState, type ReactNode } from "react";
import { CircleAlert } from "lucide-react";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ConfirmDialog } from "@/features/device-status/ConfirmDialog";
import { usePowerCycleMutation, useRebootMutation, useSetRelayMutation } from "@/features/device-status/deviceStatusApi";
import { relayCacheKey, toOutcome, type SendOutcome } from "@/features/device-status/format";
import { RelayControl } from "@/features/device-status/RelayControl";
import type { DeviceStatus } from "@/features/device-status/types";
import { fieldText, isB540, type FieldText } from "./fields";
import { validatePowerCycle } from "./validation";

/** 글자 입력을 놓는 격자. 모든 묶음의 입력 칸 폭이 같게 두 칸으로 나눈다. */
export const FIELDS = "grid grid-cols-2 items-start gap-3";

/**
 * 제어 화면의 묶음 틀. 제목, 안내, 입력, 버튼 줄 순서이고, 같은 줄의 묶음과 높이가 같아져도 버튼 줄은 맨 아래에 온다.
 * 보낸 결과의 성공 안내(장비 큐에 넣었다는 뜻)는 높이가 바뀌지 않게 버튼 줄에, 길어질 수 있는 실패 안내는 버튼 줄 위에 보인다.
 * 설정을 불러오는 중 안내도 버튼 줄에 두며, 성공 안내가 있으면 줄이 늘지 않게 화면에서만 숨긴다.
 */
export function Section({
  title,
  notes,
  actions,
  outcome,
  loading = false,
  children,
}: {
  title: string;
  notes: string[];
  actions: ReactNode;
  outcome: SendOutcome | null;
  loading?: boolean;
  children: ReactNode;
}) {
  return (
    <Card role="group" aria-label={title} aria-busy={loading}>
      <CardHeader>
        <CardTitle role="heading" aria-level={2}>
          {title}
        </CardTitle>
        {notes.map((note) => (
          <CardDescription key={note}>{note}</CardDescription>
        ))}
      </CardHeader>
      <CardContent className="flex grow flex-col gap-4">
        {children}
        {outcome !== null && !outcome.ok && (
          <Alert variant="destructive">
            <CircleAlert aria-hidden="true" />
            <AlertTitle>장비에 보내지 못했습니다</AlertTitle>
            <AlertDescription>{outcome.message}</AlertDescription>
          </Alert>
        )}
        <div className="mt-auto flex flex-wrap items-center gap-2">
          {actions}
          {outcome !== null && outcome.ok && (
            <p role="status" className="text-muted-foreground">
              장비에 보냈습니다
            </p>
          )}
          {loading && (
            <p role="status" className={outcome !== null && outcome.ok ? "sr-only" : "text-muted-foreground"}>
              설정을 불러오는 중
            </p>
          )}
        </div>
      </CardContent>
    </Card>
  );
}

/** 이름(단위)·입력·설명(뜻과 범위)·오류를 묶은 글자 입력. */
export function Field({
  text,
  value,
  onChange,
  error,
  disabled = false,
}: {
  text: FieldText;
  value: string;
  onChange: (value: string) => void;
  error?: string;
  disabled?: boolean;
}) {
  const id = useId();
  return (
    <div className="flex min-w-0 flex-col gap-1.5">
      <Label htmlFor={id}>{text.label}</Label>
      <Input
        id={id}
        inputMode="decimal"
        autoComplete="off"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        disabled={disabled}
        aria-invalid={error !== undefined}
        aria-describedby={error === undefined ? `${id}-hint` : `${id}-hint ${id}-error`}
      />
      <p id={`${id}-hint`} className="text-xs text-muted-foreground">
        {text.hint}
      </p>
      {error !== undefined && (
        <p id={`${id}-error`} className="text-xs text-destructive">
          {error}
        </p>
      )}
    </div>
  );
}

/** "전원" 묶음: 릴레이, 전원 재투입, 재부팅(B540만). */
export function PowerSection({ device }: { device: DeviceStatus }) {
  const [powerCycle] = usePowerCycleMutation();
  const [reboot] = useRebootMutation();
  const [, { isLoading: relaySending }] = useSetRelayMutation({ fixedCacheKey: relayCacheKey(device.mac) });
  const [seconds, setSeconds] = useState("10");
  const [error, setError] = useState<string | undefined>();
  const [confirm, setConfirm] = useState<"power-cycle" | "reboot" | null>(null);
  const [sending, setSending] = useState(false);
  const [outcome, setOutcome] = useState<SendOutcome | null>(null);

  const requestPowerCycle = () => {
    const checked = validatePowerCycle(seconds);
    setError(checked.errors?.seconds);
    setOutcome(null);
    if (checked.errors === null) setConfirm("power-cycle");
  };

  const send = async () => {
    const action = confirm;
    const checked = validatePowerCycle(seconds);
    if (action === "power-cycle" && checked.errors !== null) return;
    setSending(true);
    setOutcome(null);
    const result =
      action === "reboot"
        ? await reboot(device.mac)
        : await powerCycle({ mac: device.mac, seconds: checked.value?.seconds ?? 0 });
    setOutcome(toOutcome(result));
    setSending(false);
  };

  const disabled = !device.controllable || sending || relaySending;
  return (
    <Section
      title="전원"
      notes={[]}
      outcome={outcome}
      actions={
        <>
          <Button disabled={disabled} onClick={requestPowerCycle}>
            실행
          </Button>
          {isB540(device.modelId) && (
            <Button variant="outline" disabled={disabled} onClick={() => setConfirm("reboot")}>
              재부팅
            </Button>
          )}
        </>
      }
    >
      <div className="flex flex-col gap-1.5">
        <span className="text-sm font-medium">릴레이</span>
        <RelayControl device={device} disabled={sending} />
      </div>
      <div className={FIELDS}>
        <Field text={fieldText("powerCycle", device.modelId)} value={seconds} onChange={setSeconds} error={error} />
      </div>
      <ConfirmDialog
        device={device}
        open={confirm !== null}
        onOpenChange={(open) => {
          if (!open) setConfirm(null);
        }}
        title={confirm === "reboot" ? "장비를 재부팅할까요?" : "전원을 껐다 다시 켤까요?"}
        description="연결된 기기의 전원이 꺼질 수 있습니다."
        confirmLabel={confirm === "reboot" ? "재부팅" : "실행"}
        onConfirm={() => void send()}
      />
    </Section>
  );
}
