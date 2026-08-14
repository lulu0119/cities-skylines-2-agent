# Context budget: Auto from the model name, Custom from the player

Status: accepted

Visual tools already use Auto / On / Off. The token window should match: Auto infers the window from the model name; Custom uses the player setting and wins over the profile.

## Current gap

Named-model profiles already implement Auto (DeepSeek v4 is 1M). A hidden `WindowTokens=200000` exists but only as fallback for unknown names, so it cannot yet do Custom. The settings page has no Auto / Custom control.

## Considered Options

- **One hidden integer for every model.** Rejected: a 200k cap on a 1M model is a silent override the player cannot see; a 1M default on a 128k model would compact too late.
- **Infer from Endpoint / provider.** Rejected: the same Endpoint can serve many models; [ADR-0005](0005-player-permissions.md) already resolved capabilities from the model name.
