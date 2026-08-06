import { ModRegistrar } from "cs2/modding";
import { ChatPanel } from "mods/chat-panel";

const register: ModRegistrar = (moduleRegistry) => {
  // In-city only. Portal escapes append-parent layout (never bare "Game").
  moduleRegistry.append("GameBottomRight", ChatPanel);
};

export default register;
