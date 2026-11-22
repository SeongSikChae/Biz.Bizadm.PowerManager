import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { CircleAlert } from "lucide-react";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { DeviceLabel } from "@/features/device-status/DeviceLabel";
import { useSaveNameMutation } from "@/features/device-status/deviceStatusApi";
import { describeApiError } from "@/features/device-status/format";
import type { DeviceStatus } from "@/features/device-status/types";
import { MAX_NAME_LENGTH, normalizeName, validateName } from "./deviceName";

/** 창이 열릴 때마다 새로 마운트되어 지금 장비명으로 시작한다. */
function NameForm({ device, onSaved }: { device: DeviceStatus; onSaved: () => void }) {
  const [save] = useSaveNameMutation();
  const [text, setText] = useState(device.name);
  const [error, setError] = useState<string | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [sending, setSending] = useState(false);
  const id = useId();
  // 저장하는 동안 닫고 다시 연 창을 앞 저장의 성공이 닫지 않게, 이 폼이 화면에 있는지 본다.
  const mounted = useRef(false);
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (sending) return;
    const name = normalizeName(text);
    const invalid = validateName(name);
    setError(invalid);
    setFailure(null);
    if (invalid !== null) return;
    setSending(true);
    const result = await save({ mac: device.mac, name });
    if (result.error === undefined) {
      // 닫히는 동안 다시 보내지 않게 보내는 중으로 둔다.
      if (mounted.current) onSaved();
      return;
    }
    setFailure(describeApiError(result.error));
    setSending(false);
  };

  return (
    <form className="flex flex-col gap-4" onSubmit={(event) => void submit(event)}>
      <div className="flex min-w-0 flex-col gap-1.5">
        <Label htmlFor={id}>장비 이름</Label>
        <Input
          id={id}
          autoComplete="off"
          value={text}
          onChange={(e) => setText(e.target.value)}
          aria-invalid={error !== null}
          aria-describedby={error === null ? `${id}-hint` : `${id}-hint ${id}-error`}
        />
        <p id={`${id}-hint`} className="text-xs text-muted-foreground">
          {MAX_NAME_LENGTH}자까지. 비워서 저장하면 이름을 지웁니다
        </p>
        {error !== null && (
          <p id={`${id}-error`} className="text-xs text-destructive">
            {error}
          </p>
        )}
      </div>
      {failure !== null && (
        <Alert variant="destructive">
          <CircleAlert aria-hidden="true" />
          <AlertTitle>이름을 저장하지 못했습니다</AlertTitle>
          <AlertDescription>{failure}</AlertDescription>
        </Alert>
      )}
      <DialogFooter>
        <DialogClose asChild>
          <Button type="button" variant="outline">
            취소
          </Button>
        </DialogClose>
        <Button type="submit" disabled={sending}>
          저장
        </Button>
      </DialogFooter>
    </form>
  );
}

/** "이름 변경" 버튼과 창. 장비가 오프라인이어도 쓸 수 있다(장비에 보내지 않고 서버에만 저장한다). */
export function DeviceNameDialog({ device }: { device: DeviceStatus }) {
  const [open, setOpen] = useState(false);
  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button variant="outline" size="sm">
          이름 변경
        </Button>
      </DialogTrigger>
      <DialogContent showCloseButton={false}>
        <DialogHeader>
          <DialogTitle>이름 변경</DialogTitle>
          <DialogDescription className="wrap-anywhere">
            <DeviceLabel device={{ ...device, name: "" }} />
          </DialogDescription>
        </DialogHeader>
        <NameForm device={device} onSaved={() => setOpen(false)} />
      </DialogContent>
    </Dialog>
  );
}
