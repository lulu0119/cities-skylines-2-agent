import { useState } from "react";
import type { CSSProperties, FormEvent } from "react";
import { Button, Panel, Portal } from "cs2/ui";

type ChatRole = "user" | "assistant";

type ChatLine = {
  role: ChatRole;
  text: string;
};

const rootStyle: CSSProperties = {
  position: "absolute",
  left: "1.5rem",
  bottom: "8rem",
  zIndex: 1000,
  width: "22rem",
  pointerEvents: "auto",
};

const listStyle: CSSProperties = {
  maxHeight: "14rem",
  overflow: "auto",
  marginBottom: "0.75rem",
};

const lineStyle: CSSProperties = {
  marginBottom: "0.5rem",
  fontSize: "0.85rem",
  lineHeight: 1.35,
};

const formStyle: CSSProperties = {
  display: "flex",
  flexDirection: "row",
  alignItems: "center",
};

const inputStyle: CSSProperties = {
  flex: 1,
  marginRight: "0.5rem",
  minWidth: 0,
};

const placeholderReply =
  "Local echo only — C# IChatClient + ReAct comes next. Tools will use ToolQueueSystem.";

export const ChatPanel = () => {
  const [open, setOpen] = useState(true);
  const [draft, setDraft] = useState("");
  const [lines, setLines] = useState<ChatLine[]>([
    { role: "assistant", text: "Cities Skylines 2 Agent — chat shell ready." },
  ]);

  const send = () => {
    const text = draft.trim();
    if (!text) {
      return;
    }
    setLines((current) => [
      ...current,
      { role: "user", text },
      { role: "assistant", text: placeholderReply },
    ]);
    setDraft("");
  };

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    send();
  };

  return (
    <Portal>
      <div style={rootStyle}>
        {open ? (
          <Panel
            header="Cities Skylines 2 Agent"
            onClose={() => setOpen(false)}
          >
            <div style={listStyle}>
              {lines.map((line, index) => (
                <div key={`${line.role}-${index}`} style={lineStyle}>
                  <strong>{line.role === "user" ? "You" : "Agent"}: </strong>
                  {line.text}
                </div>
              ))}
            </div>
            <form style={formStyle} onSubmit={onSubmit}>
              <input
                style={inputStyle}
                value={draft}
                placeholder="Message…"
                onChange={(event) => setDraft(event.target.value)}
              />
              <Button variant="primary" onSelect={send}>
                Send
              </Button>
            </form>
          </Panel>
        ) : (
          <Button variant="primary" onSelect={() => setOpen(true)}>
            Open Agent Chat
          </Button>
        )}
      </div>
    </Portal>
  );
};
