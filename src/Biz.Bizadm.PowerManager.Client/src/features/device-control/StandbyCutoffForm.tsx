import { useId, useState } from "react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { useSaveStandbyCutoffMutation } from "@/features/device-status/deviceStatusApi";
import { toOutcome, type SendOutcome } from "@/features/device-status/format";
import type { DeviceStatus, StandbyCutoff } from "@/features/device-status/types";
import { Field, FIELDS, Section } from "./PowerSection";
import { validateStandbyCutoff, type StandbyCutoffInput } from "./validation";

function toInput(value: StandbyCutoff | null): StandbyCutoffInput {
  return {
    enabled: value?.enabled ?? false,
    thresholdWatts: value?.thresholdWatts.toFixed(2) ?? "",
    delay: value?.delay.toString() ?? "",
  };
}

/** "대기전력 차단" 묶음. `initial`은 서버가 마지막으로 보낸 값이다. */
export function StandbyCutoffForm({ device, initial, note }: { device: DeviceStatus; initial: StandbyCutoff | null; note: string }) {
  const [save] = useSaveStandbyCutoffMutation();
  const [input, setInput] = useState(() => toInput(initial));
  const [errors, setErrors] = useState<Partial<Record<"thresholdWatts" | "delay", string>>>({});
  const [sending, setSending] = useState(false);
  const [outcome, setOutcome] = useState<SendOutcome | null>(null);
  const switchId = useId();

  const submit = async () => {
    const checked = validateStandbyCutoff(input);
    setErrors(checked.errors ?? {});
    setOutcome(null);
    if (checked.errors !== null) return;
    setSending(true);
    setOutcome(toOutcome(await save({ mac: device.mac, value: checked.value })));
    setSending(false);
  };

  return (
    <Section
      title="대기전력 차단"
      notes={[note, "값의 의미와 단위는 추정입니다"]}
      outcome={outcome}
      actions={
        <Button disabled={!device.controllable || sending} onClick={() => void submit()}>
          저장
        </Button>
      }
    >
      <div className="flex items-center gap-2.5">
        <Switch id={switchId} checked={input.enabled} onCheckedChange={(enabled) => setInput((current) => ({ ...current, enabled }))} />
        <Label htmlFor={switchId}>사용</Label>
      </div>
      <div className={FIELDS}>
        <Field
          label="기준 전력 (W)"
          value={input.thresholdWatts}
          onChange={(thresholdWatts) => setInput((current) => ({ ...current, thresholdWatts }))}
          error={errors.thresholdWatts}
        />
        <Field label="지연" value={input.delay} onChange={(delay) => setInput((current) => ({ ...current, delay }))} error={errors.delay} />
      </div>
    </Section>
  );
}
