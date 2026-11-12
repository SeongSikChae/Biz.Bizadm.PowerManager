import { configureStore } from "@reduxjs/toolkit";

export const store = configureStore({
  reducer: {
    // 빈 reducer 객체는 개발 모드에서 "Store does not have a valid reducer" 콘솔 오류를 낸다.
    // feature slice를 추가하면 이 자리 표시 reducer를 지운다.
    app: (state: Record<string, never> = {}) => state,
    // feature: featureReducer,
  },
});

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;
