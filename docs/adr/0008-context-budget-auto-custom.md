# Context budget: Auto from the model name, Custom from the player

Status: accepted

Visual tools already use Auto / On / Off. The token window should match: Auto infers the window from the model name; Custom uses the player setting and wins over the profile.

## Current gap

Named-model profiles already implement Auto (DeepSeek v4 is 1M). A hidden `WindowTokens=200000` exists but only as fallback for unknown names, so it cannot yet do Custom. The settings page has no Auto / Custom control.

## Considered Options

- **One hidden integer for every model.** Rejected: a 200k cap on a 1M model is a silent override the player cannot see; a 1M default on a 128k model would compact too late.
- **Infer from Endpoint / provider.** Rejected: the same Endpoint can serve many models; [ADR-0005](0005-player-permissions.md) already resolved capabilities from the model name.

## Decision

Settings expose ContextBudgetMode Auto / Custom, matching visual tools. Auto keeps the named-model window (DeepSeek v4 1M, OpenAI ~1.05M) and uses WindowTokens only as the unknown-name fallback. Custom replaces ContextWindowTokens for every model name, known or unknown. Compact and output reserve stay derived from that resolved window. The Options copy describes the loop's budget; it does not claim to change the server model limit. WindowTokens is shown only when Custom. Both persist.
