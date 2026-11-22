import { createSlice, type PayloadAction } from "@reduxjs/toolkit";
import type { DeviceStatus } from "./types";

/** `connecting`은 스트림을 처음 여는 중(아직 열리지도 끊기지도 않음). */
export type StreamConnection = "connecting" | "open" | "lost";

interface DeviceStatusState {
  devices: Record<string, DeviceStatus>;
  /** 전체 상태를 한 번이라도 받았는지. */
  received: boolean;
  connection: StreamConnection;
}

const initialState: DeviceStatusState = { devices: {}, received: false, connection: "connecting" };

function toRecord(devices: DeviceStatus[]): Record<string, DeviceStatus> {
  return Object.fromEntries(devices.map((d) => [d.mac, d]));
}

const deviceStatusSlice = createSlice({
  name: "deviceStatus",
  initialState,
  reducers: {
    /** 상태 조회 API의 첫 결과. 스트림이 먼저 전체 상태를 보냈으면 그쪽이 더 새 값이라 버린다. */
    statusesLoaded(state, action: PayloadAction<DeviceStatus[]>) {
      if (state.received) return;
      state.devices = toRecord(action.payload);
      state.received = true;
    },
    streamOpened(state) {
      state.connection = "open";
    },
    streamLost(state) {
      state.connection = "lost";
    },
    snapshotReceived(state, action: PayloadAction<DeviceStatus[]>) {
      state.devices = toRecord(action.payload);
      state.received = true;
    },
    deviceReceived(state, action: PayloadAction<DeviceStatus>) {
      state.devices[action.payload.mac] = action.payload;
    },
  },
});

export const { statusesLoaded, streamOpened, streamLost, snapshotReceived, deviceReceived } = deviceStatusSlice.actions;
export const deviceStatusReducer = deviceStatusSlice.reducer;
