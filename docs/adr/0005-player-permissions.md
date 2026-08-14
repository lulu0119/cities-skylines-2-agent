# Player permissions are settings, not call arguments

Status: accepted

Whether the Agent may demolish, spend Development Points, or use visual tools is a player choice, not a model-chosen flag. Settings show or hide those writes. When demolition is allowed, there is no extra confirmation modal.

Development tools (`replace_road_type`, `debug_zone_blocks`, `save_game`) stay default-off and do not bypass those permissions. `replace_road_type` is road-to-road only. District create/policy tools are off the model-facing surface until polygon ownership is designed. Visual tools use Auto / On / Off: Auto follows the model name; unknown names are non-visual.

Runtime configuration is Endpoint, API key, and model name. There is no Provider selector: the old enum only filled an Endpoint preset. Model capabilities resolve from model name, never from Endpoint.
