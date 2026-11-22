import { useState } from "react";
import { Button } from "@/components/ui/button";
import { useSaveReportConditionMutation } from "@/features/device-status/deviceStatusApi";
import { toOutcome, type SendOutcome } from "@/features/device-status/format";
import type { DeviceStatus, ReportCondition } from "@/features/device-status/types";
import { Field, FIELDS, Section } from "./PowerSection";
import { validateReportCondition, type ReportConditionInput } from "./validation";

function toInput(value: ReportCondition | null): ReportConditionInput {
  return {
    minInterval: value?.minInterval.toString() ?? "",
    maxInterval: value?.maxInterval.toString() ?? "",
    changeThreshold: value?.changeThreshold.toString() ?? "",
  };
}

/** "보고 조건" 묶음. `initial`은 서버가 마지막으로 보낸 값이다. */
export function ReportConditionForm({ device, initial, note }: { device: DeviceStatus; initial: ReportCondition | null; note: string }) {
  const [save] = useSaveReportConditionMutation();
  const [input, setInput] = useState(() => toInput(initial));
  const [errors, setErrors] = useState<Partial<Record<keyof ReportCondition, string>>>({});
  const [sending, setSending] = useState(false);
  const [outcome, setOutcome] = useState<SendOutcome | null>(null);
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
      actions={
        <Button disabled={!device.controllable || sending} onClick={() => void submit()}>
          저장
        </Button>
      }
    >
      <div className={FIELDS}>
        <Field label="최소 간격" value={input.minInterval} onChange={change("minInterval")} error={errors.minInterval} />
        <Field label="최대 간격" value={input.maxInterval} onChange={change("maxInterval")} error={errors.maxInterval} />
        <Field label="변화 기준" value={input.changeThreshold} onChange={change("changeThreshold")} error={errors.changeThreshold} />
      </div>
    </Section>
  );
}
