import { useEffect, useState } from "react";
import { useAppDispatch } from "@/app/hooks";
import { useGetStatusesQuery } from "./deviceStatusApi";
import { deviceReceived, snapshotReceived, statusesLoaded, streamLost, streamOpened } from "./deviceStatusSlice";
import type { DeviceStatus } from "./types";

const STREAM_URL = "/api/admin/devices/status/stream";
const REOPEN_DELAY = 3_000;
const NOW_INTERVAL = 30_000;

/**
 * 상태 스트림을 받아 slice에 넣는다. 앱에서 한 번만 쓴다.
 * 네트워크 오류로 끊기면 브라우저가 다시 연결하지만, 응답이 200이 아니거나 `text/event-stream`이 아니면
 * `EventSource`가 `CLOSED`로 끝나므로 3초 뒤 새로 연다. 다시 연결되면 서버가 전체 상태를 먼저 보낸다.
 */
export function useDeviceStatusStream() {
  const dispatch = useAppDispatch();
  const { data } = useGetStatusesQuery();

  useEffect(() => {
    if (data !== undefined) dispatch(statusesLoaded(data));
  }, [data, dispatch]);

  useEffect(() => {
    let source: EventSource;
    let timer: ReturnType<typeof setTimeout> | undefined;

    const open = () => {
      const current = new EventSource(STREAM_URL);
      source = current;
      current.addEventListener("open", () => dispatch(streamOpened()));
      current.addEventListener("snapshot", (e) => dispatch(snapshotReceived(JSON.parse(e.data) as DeviceStatus[])));
      current.addEventListener("device", (e) => dispatch(deviceReceived(JSON.parse(e.data) as DeviceStatus)));
      current.addEventListener("error", () => {
        dispatch(streamLost());
        if (current.readyState === EventSource.CLOSED) {
          clearTimeout(timer);
          timer = setTimeout(open, REOPEN_DELAY);
        }
      });
    };

    open();
    return () => {
      clearTimeout(timer);
      source.close();
    };
  }, [dispatch]);
}

/** 상대 시간 표시에 쓰는 현재 시각(ms). 30초마다, 그리고 다시 그릴 때마다 새로 읽는다. */
export function useNow(getNow: () => number): number {
  const [, setTick] = useState(0);
  useEffect(() => {
    const timer = setInterval(() => setTick((t) => t + 1), NOW_INTERVAL);
    return () => clearInterval(timer);
  }, []);
  return getNow();
}
