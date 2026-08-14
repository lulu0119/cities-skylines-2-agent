# Session follows the loaded city

Status: accepted

The Agent is a mayor of the current save, not a cloud workspace. Switching cities clears the session. Leaving GameMode.Game (main menu or other non-Game modes) disposes the session and rejects Send. Restoring a save into a continuing session, and multiple concurrent sessions, remain long-term goals.

Runtime logs, screenshots, and overlays live under `ModsData/CitiesSkylines2Agent`. The product has not shipped, so unknown on-disk shapes are rejected and there is no migration; development data is moved by hand. Logs and screenshots stay until the player clears them.
