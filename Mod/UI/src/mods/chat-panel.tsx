import { useEffect, useRef, useState } from "react";
import type { CSSProperties, FormEvent, ReactNode } from "react";
import { bindEvent, bindValue, trigger, useValue } from "cs2/api";
import { Button, Panel, Portal } from "cs2/ui";
import mod from "mod.json";

type ChatRole = "user" | "assistant" | "tool" | "system" | "error";

type UiMessage = {
  id: number;
  role: ChatRole;
  text: string;
  tool?: string;
  streaming?: boolean;
};

type StateMessage = {
  role: string;
  text: string;
  tool: string | null;
};

type AgentState = {
  status: string;
  busy: boolean;
  pendingInputs: number;
  session: string;
  turn: string;
  contextBlocks: unknown[];
  messages: StateMessage[];
};

type AgentEvent = {
  kind: "delta" | "tool" | "status" | "user" | "error" | "compact" | "turn";
  text: string;
  tool?: string;
  status?: string;
};

const state$ = bindValue<string>(mod.id, "state", "{}");
const events$ = bindEvent<string>(mod.id, "event");

// GameBottomRight is a narrow icon column; Portal + fixed px width escapes it
// (M1 smoke: rem/% collapsed into a one-glyph vertical strip).
const rootStyle: CSSProperties = {
  position: "absolute",
  right: "24px",
  bottom: "200px",
  zIndex: 999999,
  width: "480px",
  minWidth: "480px",
  pointerEvents: "auto",
};

const listStyle: CSSProperties = {
  height: "220px",
  overflow: "auto",
};

const lineStyle: CSSProperties = {
  marginBottom: "6px",
  fontSize: "13px",
  lineHeight: 1.35,
};

const toolStyle: CSSProperties = {
  ...lineStyle,
  padding: "4px 8px",
  borderRadius: "4px",
  backgroundColor: "rgba(120, 160, 220, 0.15)",
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
  gap: "8px",
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
};

const sendStyle: CSSProperties = {
  flex: "0 0 auto",
  height: "36px",
};

const roleLabel: Record<ChatRole, string> = {
  user: "You",
  assistant: "Agent",
  tool: "Tool",
  system: "System",
  error: "Error",
};

export const ChatPanel = ({ children }: { children?: ReactNode }) => {
  const [open, setOpen] = useState(true);
  const [draft, setDraft] = useState("");
  const [messages, setMessages] = useState<UiMessage[]>([]);
  const [status, setStatus] = useState("Idle");
  const [busy, setBusy] = useState(false);
  const [pending, setPending] = useState(0);
  const [session, setSession] = useState("");
  const stateJson = useValue(state$);
  const nextId = useRef(0);
  const listRef = useRef<HTMLDivElement>(null);

  const pushMessage = (message: Omit<UiMessage, "id">) => {
    const id = nextId.current++;
    setMessages((current) => [...current, { ...message, id }]);
  };

  const finalizeStream = () => {
    setMessages((current) =>
      current.map((message) =>
        message.streaming ? { ...message, streaming: false } : message,
      ),
    );
  };

  const applyEvent = (event: AgentEvent) => {
    switch (event.kind) {
      case "user":
        finalizeStream();
        pushMessage({ role: "user", text: event.text });
        setBusy(true);
        break;
      case "delta":
        setMessages((current) => {
          const next = [...current];
          const last = next[next.length - 1];
          if (last && last.role === "assistant" && last.streaming) {
            next[next.length - 1] = { ...last, text: last.text + event.text };
          } else {
            next.push({
              id: nextId.current++,
              role: "assistant",
              text: event.text,
              streaming: true,
            });
          }
          return next;
        });
        setBusy(true);
        break;
      case "tool":
        finalizeStream();
        pushMessage({
          role: "tool",
          text: event.text,
          tool: event.tool ?? "tool",
        });
        setBusy(true);
        break;
      case "status":
        setStatus(event.status ?? event.text);
        setBusy(event.status === "Thinking" || event.status === "Working");
        if (event.status === "Idle" || event.status === "Interrupted") {
          finalizeStream();
        }
        break;
      case "error":
        finalizeStream();
        pushMessage({ role: "error", text: event.text });
        setBusy(false);
        break;
      case "compact":
        pushMessage({ role: "system", text: event.text });
        break;
      case "turn":
        finalizeStream();
        setBusy(false);
        break;
    }
  };

  useEffect(() => {
    const subscription = events$.subscribe((json) => {
      try {
        applyEvent(JSON.parse(json) as AgentEvent);
      } catch {
        // ignore malformed events
      }
    });
    return () => subscription.dispose();
  }, []);

  useEffect(() => {
    let parsed: AgentState;
    try {
      parsed = JSON.parse(stateJson) as AgentState;
    } catch {
      return;
    }
    if (!parsed.session) {
      return;
    }
    if (parsed.session !== session || messages.length === 0) {
      setSession(parsed.session);
      setStatus(parsed.status);
      setBusy(parsed.busy);
      setPending(parsed.pendingInputs ?? 0);
      setMessages(
        (parsed.messages ?? [])
          .filter((message) => message.role !== "system")
          .map((message) => ({
            id: nextId.current++,
            role: message.role as ChatRole,
            text: message.text,
            tool: message.tool ?? undefined,
          })),
      );
    } else {
      setPending(parsed.pendingInputs ?? 0);
    }
  }, [stateJson, session, messages.length]);

  useEffect(() => {
    const element = listRef.current;
    if (element) {
      element.scrollTop = element.scrollHeight;
    }
  }, [messages, status]);

  const send = () => {
    const text = draft.trim();
    if (!text) {
      return;
    }
    trigger(mod.id, "send", text);
    finalizeStream();
    pushMessage({ role: "user", text });
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
                    placeholder={
                      busy
                        ? "Type to interleave… (queued, injected after this round)"
                        : "Message…"
                    }
                    onChange={(event) => setDraft(event.target.value)}
                  />
                  <Button variant="primary" style={sendStyle} onSelect={send}>
                    Send
                  </Button>
                </form>
              }
            >
              <div style={statusStyle}>
                {status}
                {busy ? " · working" : ""}
                {pending > 0 ? ` · ${pending} queued` : ""}
                {session ? ` · ${session}` : ""}
              </div>
              <div ref={listRef} style={listStyle}>
                {messages.map((message) => (
                  <MessageLine key={message.id} message={message} />
                ))}
              </div>
              {busy && (
                <div style={{ display: "flex", gap: "8px", marginTop: "8px" }}>
                  <Button variant="primary" onSelect={() => trigger(mod.id, "interrupt")}>
                    Interrupt
                  </Button>
                </div>
              )}
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

const MessageLine = ({ message }: { message: UiMessage }) => {
  if (message.role === "tool") {
    return (
      <div style={toolStyle}>
        <strong>⚙ {message.tool}</strong>
        <pre
          style={{
            margin: "0.15rem 0 0",
            whiteSpace: "pre-wrap",
            wordBreak: "break-all",
            maxHeight: "96px",
            overflow: "auto",
            font: "inherit",
          }}
        >
          {message.text}
        </pre>
      </div>
    );
  }
  return (
    <div style={lineStyle}>
      <strong>{roleLabel[message.role]}: </strong>
      {message.text}
      {message.streaming ? "▍" : ""}
    </div>
  );
};

