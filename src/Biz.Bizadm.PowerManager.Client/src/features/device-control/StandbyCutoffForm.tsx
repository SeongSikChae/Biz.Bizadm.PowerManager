import { useId, useState } from "react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { useSaveStandbyCutoffMutation } from "@/features/device-status/deviceStatusApi";
import { toOutcome, type SendOutcome } from "@/features/device-status/format";
import type { StandbyCutoff } from "@/features/device-status/types";
import { fieldText } from "./fields";
import { Field, FIELDS, Section } from "./PowerSection";
import type { SettingsProps } from "./ReportConditionForm";
import { validateStandbyCutoff, type StandbyCutoffInput } from "./validation";

function toInput(value: StandbyCutoff | null): StandbyCutoffInput {
  return {
    enabled: value?.enabled ?? false,
    thresholdWatts: value?.thresholdWatts.toFixed(2) ?? "",
    delay: value?.delay.toString() ?? "",
  };
}

/** "대기전력 차단" 묶음. 설정을 읽기 전에는 비활성이고, 읽은 뒤 한 번 채운다. */
export function StandbyCutoffForm({ device, initial, ready, loading, note }: SettingsProps<StandbyCutoff>) {
  const [save] = useSaveStandbyCutoffMutation();
  const [input, setInput] = useState(() => toInput(null));
  const [filled, setFilled] = useState(false);
  const [errors, setErrors] = useState<Partial<Record<"thresholdWatts" | "delay", string>>>({});
  const [sending, setSending] = useState(false);
  const [outcome, setOutcome] = useState<SendOutcome | null>(null);
  const switchId = useId();
  // 다시 마운트하지 않고 렌더링 중 상태를 맞춰 한 번만 채운다.
  if (ready && !filled) {
    setFilled(true);
    setInput(toInput(initial));
  }

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
      notes={[note]}
      outcome={outcome}
      loading={loading}
      actions={
        <Button disabled={!device.controllable || sending || !ready} onClick={() => void submit()}>
          저장
        </Button>
      }
    >
      <div className="flex items-center gap-2.5">
        <Switch
          id={switchId}
          checked={input.enabled}
          disabled={!ready}
          onCheckedChange={(enabled) => setInput((current) => ({ ...current, enabled }))}
        />
        <Label htmlFor={switchId}>사용</Label>
      </div>
      <div className={FIELDS}>
        <Field
          text={fieldText("thresholdWatts", device.modelId)}
          value={input.thresholdWatts}
          onChange={(thresholdWatts) => setInput((current) => ({ ...current, thresholdWatts }))}
          error={errors.thresholdWatts}
          disabled={!ready}
        />
        <Field
          text={fieldText("delay", device.modelId)}
          value={input.delay}
          onChange={(delay) => setInput((current) => ({ ...current, delay }))}
          error={errors.delay}
          disabled={!ready}
        />
      </div>
    </Section>
  );
}
