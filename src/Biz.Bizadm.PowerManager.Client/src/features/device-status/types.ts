export type Relay = "on" | "off" | "unknown";

/** 서버 `GET /api/admin/devices/status`와 상태 스트림의 원소. `lastReportAt`은 ISO 8601 문자열이다. */
export interface DeviceStatus {
  mac: string;
  /** 사용자가 정한 장비명. 없으면 빈 문자열. */
  name: string;
  modelId: string;
  online: boolean;
  relay: Relay;
  voltage: number | null;
  current: number | null;
  powerFactor: number | null;
  power: number | null;
  lastReportAt: string | null;
  /** 온라인이고 서버가 그 장비의 제어 토픽을 안다. */
  controllable: boolean;
}

export interface ReportCondition {
  minInterval: number;
  maxInterval: number;
  changeThreshold: number;
}

export interface StandbyCutoff {
  enabled: boolean;
  thresholdWatts: number;
  delay: number;
}

/** `days`는 일~토 순서 7개, `time`은 `HH:MM:SS`, `seconds`는 다시 켤 때까지의 초. */
export interface Schedule {
  enabled: boolean;
  days: boolean[];
  time: string;
  seconds: number;
}

/** 서버가 장비에 마지막으로 보낸 설정. 보낸 적이 없으면 null. */
export interface DeviceSettings {
  reportCondition: ReportCondition | null;
  standbyCutoff: StandbyCutoff | null;
  schedule: Schedule | null;
}
