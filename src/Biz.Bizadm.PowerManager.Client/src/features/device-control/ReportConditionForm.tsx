import { useState } from "react";
import { Button } from "@/components/ui/button";
import { useSaveReportConditionMutation } from "@/features/device-status/deviceStatusApi";
import { toOutcome, type SendOutcome } from "@/features/device-status/format";
import type { DeviceStatus, ReportCondition } from "@/features/device-status/types";
import { fieldText } from "./fields";
import { Field, FIELDS, Section } from "./PowerSection";
import { validateReportCondition, type ReportConditionInput } from "./validation";

/** 설정 묶음이 받는 값. `ready`는 이번에 읽은 설정이 있다는 뜻이고, `initial`은 그때 서버가 마지막으로 보낸 값이다. */
export interface SettingsProps<T> {
  device: DeviceStatus;
  initial: T | null;
  ready: boolean;
  loading: boolean;
  note: string;
}

function toInput(value: ReportCondition | null): ReportConditionInput {
  return {
    minInterval: value?.minInterval.toString() ?? "",
    maxInterval: value?.maxInterval.toString() ?? "",
    changeThreshold: value?.changeThreshold.toString() ?? "",
  };
}

/** "보고 조건" 묶음. 설정을 읽기 전에는 비활성이고, 읽은 뒤 한 번 채운다. */
export function ReportConditionForm({ device, initial, ready, loading, note }: SettingsProps<ReportCondition>) {
  const [save] = useSaveReportConditionMutation();
  const [input, setInput] = useState(() => toInput(null));
  const [filled, setFilled] = useState(false);
  const [errors, setErrors] = useState<Partial<Record<keyof ReportCondition, string>>>({});
  const [sending, setSending] = useState(false);
  const [outcome, setOutcome] = useState<SendOutcome | null>(null);
  // 다시 마운트하지 않고 렌더링 중 상태를 맞춰 한 번만 채운다.
  if (ready && !filled) {
    setFilled(true);
    setInput(toInput(initial));
  }
  const change = (key: keyof ReportCondition) => (text: string) => setInput((current) => ({ ...current, [key]: text }));

  const submit = async () => {
    const checked = validateReportCondition(input);
    setErrors(checked.errors ?? {});
    setOutcome(null);
    if (checked.errors !== null) return;
    setSending(true);
    setOutcome(toOutcome(await save({ mac: device.mac, value: checked.value })));
    setSending(false);
  };

  return (
    <Section
      title="보고 조건"
      notes={[note]}
      outcome={outcome}
      loading={loading}
      actions={
        <Button disabled={!device.controllable || sending || !ready} onClick={() => void submit()}>
          저장
        </Button>
      }
    >
      <div className={FIELDS}>
        {(["minInterval", "maxInterval", "changeThreshold"] as const).map((key) => (
          <Field
            key={key}
            text={fieldText(key, device.modelId)}
            value={input[key]}
            onChange={change(key)}
            error={errors[key]}
            disabled={!ready}
          />
        ))}
      </div>
    </Section>
  );
}
