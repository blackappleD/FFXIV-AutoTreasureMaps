# Security policy

## Supported versions

Only the latest release published to the custom Dalamud repo is supported. If you're on an older `vX.Y.Z`, please update before reporting.

## Reporting a vulnerability

Please report security issues privately via GitHub's private vulnerability reporting:

https://github.com/XeldarAlz/FFXIV-AutoTreasureMaps/security/advisories/new

Please don't open a public issue or Discussion for anything that could let someone else exploit users of the plugin before a fix is out.

What counts:

- Code execution or crashes triggerable by crafted game state.
- The plugin teleporting, engaging mobs, or persisting something it shouldn't (beyond the documented treasure map automation, which is the feature).
- Data exfiltration: the plugin makes two kinds of network call, both to [Universalis](https://universalis.app) and both only when you turn them on:
  - **Buy on the cheapest world** (Market): once per purchase in a run, it asks in one request for the listings of the maps it may buy and when each world was last uploaded, sending the data centre's name and the maps' item ids.
  - **Estimate loot prices** (General): while a run is going or the stats are on screen, at most every half hour, it asks for the rare loot's prices, sending your home data centre's name and the rare loot's item ids.

  Any other network traffic would be a bug.

What doesn't:

- The fact that Auto Treasure Maps runs the treasure map loop for you. That's against Square Enix's Terms of Service and is the whole feature; turning it on is the user's choice.

I'll aim to acknowledge reports within a few days and to ship a fix or workaround as soon as I've verified the issue.
