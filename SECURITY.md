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
- Data exfiltration: the plugin makes one kind of network call, and only when you turn on **Buy on the cheapest world** under Market: before a purchase it asks [Universalis](https://universalis.app) for the map's listings, sending the data centre's name and the map's item id. Any other network traffic would be a bug.

What doesn't:

- The fact that Auto Treasure Maps runs the treasure map loop for you. That's against Square Enix's Terms of Service and is the whole feature; turning it on is the user's choice.

I'll aim to acknowledge reports within a few days and to ship a fix or workaround as soon as I've verified the issue.
