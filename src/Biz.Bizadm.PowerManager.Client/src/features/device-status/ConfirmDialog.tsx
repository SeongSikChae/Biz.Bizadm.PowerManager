import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { deviceLabel } from "./format";
import type { DeviceStatus } from "./types";

/** 장비를 밝힌 확인 창. "취소"나 Esc는 아무것도 하지 않고 닫는다. */
export function ConfirmDialog({
  device,
  open,
  onOpenChange,
  title,
  description,
  confirmLabel,
  onConfirm,
}: {
  device: Pick<DeviceStatus, "modelId" | "mac">;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description: string;
  confirmLabel: string;
  onConfirm: () => void;
}) {
  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{title}</AlertDialogTitle>
          <AlertDialogDescription>
            <span className="block font-medium text-foreground">{deviceLabel(device)}</span>
            {description}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>취소</AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            onClick={() => {
              // 닫히는 애니메이션 동안 다시 누른 것은 버린다.
              if (open) onConfirm();
            }}
          >
            {confirmLabel}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
