import { displayValue } from "@/features/devices/deviceList";
import type { DeviceStatus } from "./types";

type Identity = Pick<DeviceStatus, "name" | "modelId" | "mac">;

function Mac({ mac }: { mac: string }) {
  return <span className="font-mono">{mac}</span>;
}

/** 확인 창 등에서 장비를 밝히는 "장비명 — 모델 (mac)". 장비명이 없으면 "모델 (mac)"이다. */
export function DeviceLabel({ device }: { device: Identity }) {
  return (
    <>
      {device.name !== "" && `${device.name} — `}
      {displayValue(device.modelId)} (<Mac mac={device.mac} />)
    </>
  );
}

/** 제목(장비명, 없으면 모델) 아래의 줄. 제목에 나온 것을 되풀이하지 않는다. */
export function DeviceSubtitle({ device }: { device: Identity }) {
  return (
    <>
      {device.name !== "" && `${displayValue(device.modelId)} · `}
      <Mac mac={device.mac} />
    </>
  );
}
