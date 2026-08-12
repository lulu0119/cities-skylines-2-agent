import { useEffect, useRef, useState } from "react";
import type { CSSProperties, FormEvent, ReactNode } from "react";
import { bindEvent, bindValue, trigger, useValue } from "cs2/api";
import { Button, Panel, Portal, Scrollable } from "cs2/ui";
import mod from "mod.json";

type ChatRole = "user" | "assistant" | "system" | "error";

type UiMessage = {
  id: number;
  role: ChatRole;
  text: string;
  streaming?: boolean;
};

type StateMessage = {
  role: string;
  text: string;
  tool: string | null;
};

type AgentContext = {
  windowTokens: number;
  estimatedTokens: number;
  compactAtTokens: number;
  source: string;
  vision: boolean;
};

type AgentState = {
  status: string;
  busy: boolean;
  pendingInputs: number;
  session: string;
  context?: AgentContext;
  messages: StateMessage[];
};

type AgentEvent = {
  kind: "delta" | "tool" | "status" | "user" | "error" | "compact" | "turn" | "progress";
  text: string;
  tool?: string;
  status?: string;
};

type ChatStore = {
  session: string;
  messages: UiMessage[];
  nextId: number;
  status: string;
  busy: boolean;
  pending: number;
};

const state$ = bindValue<string>(mod.id, "state", "{}");
const events$ = bindEvent<string>(mod.id, "event");

const getStore = (): ChatStore => {
  const root = window as Window & { __cs2AgentChat?: ChatStore };
  if (!root.__cs2AgentChat) {
    root.__cs2AgentChat = {
      session: "",
      messages: [],
      nextId: 0,
      status: "Idle",
      busy: false,
      pending: 0,
    };
  }
  return root.__cs2AgentChat;
};

// Fixed placement — Portal escapes GameBottomRight; no Panel.draggable (can hide the panel).
const rootStyle: CSSProperties = {
  position: "absolute",
  right: "24px",
  bottom: "200px",
  zIndex: 999999,
  width: "480px",
  minWidth: "480px",
  pointerEvents: "auto",
};

const statusStyle: CSSProperties = {
  fontSize: "12px",
  opacity: 0.75,
  marginBottom: "8px",
  whiteSpace: "nowrap",
  overflow: "hidden",
  textOverflow: "ellipsis",
};

const composerStyle: CSSProperties = {
  display: "flex",
  flexDirection: "row",
  alignItems: "center",
  width: "100%",
};

const inputStyle: CSSProperties = {
  flex: "1 1 auto",
  minWidth: 0,
  height: "36px",
  padding: "0 10px",
  boxSizing: "border-box",
  border: "1px solid rgba(255, 255, 255, 0.22)",
  borderRadius: "4px",
  backgroundColor: "rgba(0, 0, 0, 0.45)",
  color: "#f0f4fa",
  fontSize: "14px",
  marginRight: "8px",
};

const actionButtonStyle: CSSProperties = {
  flex: "0 0 auto",
  height: "36px",
  padding: "0 14px",
  lineHeight: "normal",
  marginLeft: "4px",
};

const interruptStyle: CSSProperties = {
  ...actionButtonStyle,
  display: "block",
  lineHeight: "36px",
  textAlign: "center",
  border: "1px solid rgba(255, 255, 255, 0.35)",
  borderRadius: "4px",
  backgroundColor: "rgba(0, 0, 0, 0.35)",
  color: "#f0f4fa",
  cursor: "pointer",
};

const lineStyle: CSSProperties = {
  marginBottom: "6px",
  fontSize: "13px",
  lineHeight: 1.35,
};

const listStyle: CSSProperties = {
  height: "220px",
  width: "100%",
};

const roleLabel: Record<ChatRole, string> = {
  user: "You",
  assistant: "Agent",
  system: "System",
  error: "Error",
};

const formatTokenCount = (value: number): string => {
  if (value >= 1_000_000) {
    return `${(value / 1_000_000).toFixed(1)}M`;
  }
  if (value >= 1_000) {
    return `${Math.round(value / 1_000)}k`;
  }
  return `${Math.max(0, Math.round(value))}`;
};

const toUiMessages = (messages: StateMessage[]): UiMessage[] =>
  messages
    .filter(
      (message) =>
        message.role === "user" || message.role === "assistant" || message.role === "error",
    )
    .filter((message) => (message.text ?? "").trim().length > 0)
    .map((message, index) => ({
      id: index,
      role: message.role as ChatRole,
      text: message.text ?? "",
    }));

export const ChatPanel = ({ children }: { children?: ReactNode }) => {
  const store = getStore();
  const [open, setOpen] = useState(true);
  const [draft, setDraft] = useState("");
  const [messages, setMessages] = useState<UiMessage[]>(store.messages);
  const [status, setStatus] = useState(store.status);
  const [busy, setBusy] = useState(store.busy);
  const [pending, setPending] = useState(store.pending);
  const [session, setSession] = useState(store.session);
  const [context, setContext] = useState<AgentContext | null>(null);
  const stateJson = useValue(state$);
  const subscribed = useRef(false);

  const syncMessages = (next: UiMessage[]) => {
    store.messages = next;
    setMessages(next);
  };

  const pushMessage = (message: Omit<UiMessage, "id">) => {
    const id = store.nextId++;
    syncMessages([...store.messages, { ...message, id }]);
  };

  const finalizeStream = () => {
    syncMessages(
      store.messages.map((message) =>
        message.streaming ? { ...message, streaming: false } : message,
      ),
    );
  };

  const applyEvent = (event: AgentEvent) => {
    switch (event.kind) {
      case "user":
        finalizeStream();
        pushMessage({ role: "user", text: event.text });
        store.busy = true;
        setBusy(true);
        break;
      case "delta":
        {
          const next = [...store.messages];
          const last = next[next.length - 1];
          if (last && last.role === "assistant" && last.streaming) {
            next[next.length - 1] = { ...last, text: last.text + event.text };
          } else {
            next.push({
              id: store.nextId++,
              role: "assistant",
              text: event.text,
              streaming: true,
            });
          }
          syncMessages(next);
          store.busy = true;
          setBusy(true);
        }
        break;
      case "tool":
        store.busy = true;
        setBusy(true);
        break;
      case "status":
        store.status = event.status ?? event.text;
        setStatus(store.status);
        store.busy = event.status === "Thinking" || event.status === "Working";
        setBusy(store.busy);
        if (event.status === "Idle" || event.status === "Interrupted") {
          finalizeStream();
        }
        break;
      case "progress":
        store.status = event.text;
        setStatus(event.text);
        break;
      case "error":
        finalizeStream();
        pushMessage({ role: "error", text: event.text });
        store.busy = false;
        setBusy(false);
        break;
      case "compact":
        break;
      case "turn":
        finalizeStream();
        store.busy = false;
        setBusy(false);
        break;
    }
  };

  useEffect(() => {
    if (subscribed.current) {
      return;
    }
    subscribed.current = true;
    const subscription = events$.subscribe((json) => {
      applyEvent(JSON.parse(json) as AgentEvent);
    });
    return () => {
      subscribed.current = false;
      subscription.dispose();
    };
  }, []);

  useEffect(() => {
    const parsed = JSON.parse(stateJson) as AgentState;
    if (!parsed.session) {
      return;
    }
    if (parsed.session === store.session) {
      store.pending = parsed.pendingInputs ?? 0;
      store.status = parsed.status;
      store.busy = parsed.busy;
      setPending(store.pending);
      setStatus(store.status);
      setBusy(store.busy);
      setContext(parsed.context ?? null);
      return;
    }
    store.session = parsed.session;
    store.status = parsed.status;
    store.busy = parsed.busy;
    store.pending = parsed.pendingInputs ?? 0;
    setSession(store.session);
    setStatus(store.status);
    setBusy(store.busy);
    setPending(store.pending);
    setContext(parsed.context ?? null);
    const hydrated = toUiMessages(parsed.messages ?? []);
    store.nextId = hydrated.length;
    syncMessages(hydrated);
  }, [stateJson]);

  const send = () => {
    const text = draft.trim();
    if (!text) {
      return;
    }
    trigger(mod.id, "send", text);
    store.busy = true;
    setBusy(true);
    setDraft("");
  };

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    send();
  };

  return (
    <>
      {children}
      <Portal>
        <div style={rootStyle}>
          {open ? (
            <Panel
              header="Cities Skylines 2 Agent"
              onClose={() => setOpen(false)}
              footer={
                <form style={composerStyle} onSubmit={onSubmit}>
                  <input
                    style={inputStyle}
                    value={draft}
                    placeholder={busy ? "Type to queue…" : "Message…"}
                    onChange={(event) => setDraft(event.target.value)}
                  />
                  {busy ? (
                    <div
                      style={interruptStyle}
                      onClick={() => trigger(mod.id, "interrupt")}
                    >
                      Interrupt
                    </div>
                  ) : null}
                  <Button
                    variant="primary"
                    style={actionButtonStyle}
                    onSelect={send}
                  >
                    Send
                  </Button>
                </form>
              }
            >
              <div style={statusStyle}>
                {`${status}${busy ? " | working" : ""}${pending > 0 ? ` | ${pending} queued` : ""}${session ? ` | ${session}` : ""}${context ? ` | ctx ${formatTokenCount(context.estimatedTokens)}/${formatTokenCount(context.windowTokens)}` : ""}`}
              </div>
              <Scrollable vertical trackVisibility="scrollable" style={listStyle}>
                {messages.map((message) => (
                  <div key={message.id} style={lineStyle}>
                    {`${roleLabel[message.role]}: ${message.text}${message.streaming ? " ..." : ""}`}
                  </div>
                ))}
              </Scrollable>
            </Panel>
          ) : (
            <Button variant="primary" onSelect={() => setOpen(true)}>
              Open Agent Chat
            </Button>
          )}
        </div>
      </Portal>
    </>
  );
};
