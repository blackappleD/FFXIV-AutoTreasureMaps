<p align="center">
  <img src="AutoTreasureMaps/Images/Icon.png" width="180" alt="TreasureHelper icon" />
</p>

<h1 align="center">TreasureHelper</h1>

> **Based on Auto Treasure Maps by XeldarAlz**
> https://github.com/XeldarAlz/FFXIV-AutoTreasureMaps

## 关于本 fork

TreasureHelper 是 [blackappleD](https://github.com/blackappleD) 维护的国服版本，并非原作者发布或维护的版本，
问题请反馈到 [blackappleD/FFXIV-AutoTreasureMaps](https://github.com/blackappleD/FFXIV-AutoTreasureMaps/issues)，
不要提交到上游仓库。

相对上游的改动：

- 按上游 [TRADEMARK.md](TRADEMARK.md) 的要求使用独立身份：名称“TreasureHelper”、InternalName `TreasureHelper`
  （程序集改名，项目路径与命名空间保持不变以便合并上游）、独立图标，可与原版并存且不共享设置。
- 内置 BossMod 战斗预设改名为“TreasureHelper”；关于页面改为指向本 fork，并保留原作者署名。
- 版本号：前三段跟随上游，第 4 段从 2001 起递增（上游 1.0.0.0 → 1.0.0.2001）。
- 不使用上游的自动打 tag / 发布 workflow，Release 在本地构建后上传 `TreasureHelper.zip`。

通过插件仓库安装：在 Dalamud 自定义插件仓库中添加
`https://raw.githubusercontent.com/blackappleD/DalamudPlugins/main/repo.json`。

以下为上游 README 原文。

---

<p align="center">
  <a href="https://discord.gg/hppkAvdBEE"><img alt="Discord" src="https://img.shields.io/badge/Discord-join-5865F2?style=flat-square&logo=discord&logoColor=white"></a>
  <a href="https://github.com/XeldarAlz/FFXIV-AutoTreasureMaps/releases/latest"><img alt="Release" src="https://img.shields.io/github/v/release/XeldarAlz/FFXIV-AutoTreasureMaps?style=flat-square&color=blue"></a>
  <a href="https://github.com/XeldarAlz/FFXIV-AutoTreasureMaps/releases"><img alt="Downloads" src="https://img.shields.io/github/downloads/XeldarAlz/FFXIV-AutoTreasureMaps/total?style=flat-square&color=blue&cacheSeconds=300"></a>
  <a href="https://github.com/XeldarAlz/FFXIV-AutoTreasureMaps/actions/workflows/release.yml"><img alt="Build" src="https://img.shields.io/github/actions/workflow/status/XeldarAlz/FFXIV-AutoTreasureMaps/release.yml?style=flat-square"></a>
  <a href="LICENSE.md"><img alt="License" src="https://img.shields.io/badge/license-AGPL--3.0--or--later-blue?style=flat-square"></a>
</p>

<p align="center">
  <em>Treasure maps, dug up for you. Built on Dalamud.</em>
</p>

---

## What it does

Works through the timeworn maps in your bags for you. Press **Start** and the plugin deciphers a map, travels to its dig spot, digs up the coffer, fights off the monsters that guard it, opens it, and moves on to the next map until your bags are empty. Once they are, it can gather the day's free map and buy more on the market board, if you turn those on. Portals and undersea maps are left to you.

## Features

- **Decipher and dig**: reads each timeworn map you carry, works out where it points, and digs at the spot.
- **Travel**: teleports to the nearest aetheryte, then flies, rides or walks to the dig spot, and lands where it can dig.
- **Hands-off combat**: fights off every wave that guards a coffer with a bundled combat preset, then opens the coffer.
- **Map choice**: pick which map tiers to run and whether the highest or the lowest goes first.
- **Loot rolls**: answers the Need or Greed window for the coffer's items the way you choose, or leaves it to you.
- **Daily map**: once a day, gathers the free timeworn map as a Miner or Botanist when the run has no map left, then switches back to your gearset (off by default). A Miner skips Gargantuaskin, whose only Miner nodes hold nothing but collectables.
- **Market board**: when no map is left in your bags and no daily map can be gathered, buys the next one on the market board under a price cap you set per tier, within a gil budget per run and never below the gil you choose to keep (off by default).
- **Recovery**: gets back up after a death; re-paths, jumps, or teleports out when it gets stuck.
- **After-run action**: stay logged in, return to the inn, log out, or close the game once the last map is done.
- **Auto-repair**: Dark Matter first, then your Grand Company mender or a repair NPC you pick.
- **Auto-consume**: keeps food and medicine buffs up between maps, HQ first.
- **Loot cleanup** (off by default): notes what each coffer and treasure dungeon gives and discards whatever is not on your whitelist, between maps and when a run finishes. Maps and each map's high-value loot are always kept; right-click an item in your inventory to whitelist it.
- **Humanizer**: waits a random moment before each teleport, dig, coffer and fight, and takes city breaks between maps, so long sessions look less mechanical.
- **Pause & resume**: park a run without losing your place, and auto-pause while you're in a duty.
- **Party invites**: auto-declines incoming invites during a run after a random delay, with an optional reply message.
- **GM alert**: stops the bot when a GM is near, with optional toast, beeps, or custom commands.
- **Stop conditions**: a run ends when no enabled map is left, after a set number of maps or minutes, after failed maps in a row, or after a second knockout.
- **History**: every run recorded with its maps, gil (less what it spent on maps), portals seen and how it ended.
- **Console**: a live log of every step, with filters and a copy button for bug reports.

## Install

In-game: `/xlsettings` → **Experimental** → paste into **Custom Plugin Repositories**:

```
https://raw.githubusercontent.com/XeldarAlz/DalamudPlugins/main/repo.json
```

Tick **Enabled**, click **+**, then **Save and Close**. Open `/xlplugins` → **All Plugins**, search for **Auto Treasure Maps**, and install.

The plugin needs a few helpers for movement and combat to be installed and loaded. Open `/atm deps` after install to see the list and one-click each missing one.

## Commands

| Command | Action |
|---|---|
| `/atm` | Toggle the main window |
| `/treasuremaps` | Alias for `/atm` |
| `/atm config` | Open the Settings page |
| `/atm stats` | Open the History page |
| `/atm deps` | Open the Plugins page |
| `/atm log` | Open the Console page |
| `/atm changelog` | Open the Changelog page |
| `/atm about` | Open the About page |
| `/atm start` | Start a run: decipher and run your maps until a stop condition |
| `/atm stop` | Stop the current run |
| `/atm pause` | Pause or resume the current run |
| `/atm target` | Log targeted object's BaseId (debug helper) |
| `/atm spot` | Print the deciphered map you hold and its dig spot |
| `/atm flag` | Flag that dig spot on the map |
| `/atm goto spot` | Travel to that dig spot and land on it |
| `/atm dig` | Run the held map: travel, dig, fight the guards, loot the coffer |
| `/atm decipher [map]` | Decipher the next timeworn map in your bags by the order in Settings, or the one named (a piece of its name or its item id) |
| `/atm buy [map]` | Buy the next map on the market board under its price cap, or the one named (a piece of its name or its item id); market buying must be on in Settings, Maps |
| `/atm gather` | Gather today's timeworn map once as a Miner or Botanist, `/atm gather stop` cancels |
| `/atm tiers` | List every map tier in the Console (debug helper) |
| `/atm goto <territory> <x> <y> <z>` | Travel to a point, `/atm goto stop` cancels (debug helper) |

## Languages

The windows are available in English, Deutsch, Français, Español, Português (Brasil), Русский, Türkçe, 日本語, and 中文. The plugin picks a language from your Dalamud and game client settings on first launch; change it any time under Settings, General, Language. Game data such as zone and item names always follows the game client.

Spotted a wrong or awkward translation? Open a [translation issue](https://github.com/XeldarAlz/FFXIV-AutoTreasureMaps/issues/new?template=translation_report.yml) and tell me what it should say instead.

## Community

Questions, ideas, or just want to hang out with other players? Come say hi on Discord.

→ [Join our Discord](https://discord.gg/hppkAvdBEE)

## More from me

If you liked this plugin, take a look at my other Dalamud work. You might find something else there for you.

→ [XeldarAlz Dalamud Plugins](https://github.com/XeldarAlz/DalamudPlugins)

## License

AGPL-3.0-or-later. See [LICENSE.md](LICENSE.md). [NOTICE](NOTICE) adds the attribution terms the AGPL allows: a fork, or any project that reuses this code, must credit the original author and must not pass itself off as the original. The license covers the code, not the name or the icon: read the [trademark and naming policy](TRADEMARK.md) before you publish a fork.

Data taken from other projects, with its source and license, is listed in [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md).

How AI is used to build this plugin is written down in [AI usage](AI-USAGE.md).
