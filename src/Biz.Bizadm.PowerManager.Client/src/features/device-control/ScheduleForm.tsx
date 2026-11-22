import { useId, useState } from "react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { ConfirmDialog } from "@/features/device-status/ConfirmDialog";
import { useDeleteScheduleMutation, useSaveScheduleMutation } from "@/features/device-status/deviceStatusApi";
import { toOutcome, type SendOutcome } from "@/features/device-status/format";
import type { DeviceStatus, Schedule } from "@/features/device-status/types";
import { Field, FIELDS, Section } from "./PowerSection";
import { validateSchedule, type ScheduleField, type ScheduleInput } from "./validation";

const DAYS = ["일", "월", "화", "수", "목", "금", "토"];

function toInput(value: Schedule | null): ScheduleInput {
  const [hour = "", minute = "", second = ""] = value?.time.split(":") ?? [];
  return {
    enabled: value?.enabled ?? false,
    days: value?.days ?? DAYS.map(() => false),
    hour,
    minute,
    second,
    seconds: value?.seconds.toString() ?? "",
  };
}

/** "예약 전원 재투입" 묶음. 삭제는 서버가 보낸 예약이 있는지와 상관없이 할 수 있다. */
export function ScheduleForm({ device, initial, note }: { device: DeviceStatus; initial: Schedule | null; note: string }) {
  const [save] = useSaveScheduleMutation();
  const [remove] = useDeleteScheduleMutation();
  const [input, setInput] = useState(() => toInput(initial));
  const [errors, setErrors] = useState<Partial<Record<ScheduleField, string>>>({});
  const [sending, setSending] = useState(false);
  const [outcome, setOutcome] = useState<SendOutcome | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const id = useId();
  const change = (key: "hour" | "minute" | "second" | "seconds") => (text: string) => setInput((current) => ({ ...current, [key]: text }));

  const submit = async () => {
    const checked = validateSchedule(input);
    setErrors(checked.errors ?? {});
    setOutcome(null);
    if (checked.errors !== null) return;
    setSending(true);
    setOutcome(toOutcome(await save({ mac: device.mac, value: checked.value })));
    setSending(false);
  };

  const submitDelete = async () => {
    setErrors({});
    setOutcome(null);
    setSending(true);
    const result = toOutcome(await remove(device.mac));
    // 지웠으면 서버가 보낸 예약이 없는 상태이므로 입력을 비운다.
    if (result.ok) setInput(toInput(null));
    setOutcome(result);
    setSending(false);
  };

  const disabled = !device.controllable || sending;
  return (
    <Section
      title="예약 전원 재투입"
      notes={[note]}
      outcome={outcome}
      actions={
        <>
          <Button disabled={disabled} onClick={() => void submit()}>
            저장
          </Button>
          <Button variant="outline" disabled={disabled} onClick={() => setConfirmOpen(true)}>
            삭제
          </Button>
        </>
      }
    >
      <div className="flex items-center gap-2.5">
        <Switch id={`${id}-enabled`} checked={input.enabled} onCheckedChange={(enabled) => setInput((current) => ({ ...current, enabled }))} />
        <Label htmlFor={`${id}-enabled`}>사용</Label>
      </div>
      <fieldset className="flex flex-col gap-2" aria-describedby={errors.days !== undefined ? `${id}-days-error` : undefined}>
        <legend className="mb-2 text-sm font-medium">요일</legend>
        <div className="flex flex-wrap gap-x-5 gap-y-3">
          {DAYS.map((name, i) => (
            <span key={name} className="inline-flex items-center gap-2">
              <Checkbox
                id={`${id}-day-${i}`}
                checked={input.days[i]}
                aria-invalid={errors.days !== undefined}
                onCheckedChange={(checked) =>
                  setInput((current) => ({ ...current, days: current.days.map((d, j) => (j === i ? checked === true : d)) }))
                }
              />
              <Label htmlFor={`${id}-day-${i}`}>{name}</Label>
            </span>
          ))}
        </div>
        {errors.days !== undefined && (
          <p id={`${id}-days-error`} className="text-xs text-destructive">
            {errors.days}
          </p>
        )}
      </fieldset>
      <div className={FIELDS}>
        <Field label="시" value={input.hour} onChange={change("hour")} error={errors.hour} />
        <Field label="분" value={input.minute} onChange={change("minute")} error={errors.minute} />
        <Field label="초" value={input.second} onChange={change("second")} error={errors.second} />
        <Field label="다시 켤 때까지의 초" value={input.seconds} onChange={change("seconds")} error={errors.seconds} />
      </div>
      <ConfirmDialog
        device={device}
        open={confirmOpen}
        onOpenChange={setConfirmOpen}
        title="예약을 삭제할까요?"
        description="장비에 저장된 예약 전원 재투입을 지웁니다."
        confirmLabel="삭제"
        onConfirm={() => void submitDelete()}
      />
    </Section>
  );
}
