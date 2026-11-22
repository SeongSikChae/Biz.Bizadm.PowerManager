import { Route, Routes } from "react-router";
import { AppShell } from "@/components/AppShell";
import { DashboardPage } from "@/features/dashboard/DashboardPage";
import { DeviceControlPage } from "@/features/device-control/DeviceControlPage";
import { useDeviceStatusStream } from "@/features/device-status/useDeviceStatusStream";
import { DevicesPage } from "@/features/devices/DevicesPage";

function App() {
  // 대시보드와 장비 제어 화면이 같은 스트림을 보도록 앱에서 한 번만 연다.
  useDeviceStatusStream();
  return (
    <AppShell>
      <Routes>
        <Route path="/" element={<DashboardPage />} />
        <Route path="/devices" element={<DevicesPage />} />
        <Route path="/devices/:mac" element={<DeviceControlPage />} />
      </Routes>
    </AppShell>
  );
}

export default App;
