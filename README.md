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
- 坦克死刑减伤（设置 → 通用 → 行为，默认关闭）：运行中个人减伤由插件接管。宝物库 boss 读死刑时开短减伤和一个大减伤（先戮罪后铁壁）；boss 会读死刑期间按血量开短减伤的门槛降到 50%，把短减伤留给死刑；死刑以外只在被 3 个以上敌人攻击、血量低于 40% 时开一个大减伤
  （原初的血气、圣盾阵、至黑之夜、刚玉之心）；已有减伤生效时不重复开，大减伤交给战斗插件。
- 宝物库奖励怪物：设置 → 宝物库 → 奖励怪物，可优先攻击黄金猫、金库守卫、财富羊驼等奖励怪物，
  以及按头顶标记顺序击杀蔓德拉战队（单人不建议开启）；两项都开时奖励怪物优先。奖励怪物优先还可以按宝物库分别设置。
- G17 宝物库（加加财富天坑）：每层先开宝箱、打完怪再开一次（含奖励怪物掉的宝箱），然后选门、穿过门后的传送阵飞往下一层，
  最后一层击败首领后从退出点离开。
- 历史 → 挖宝日志：逐张记录宝图名称、是否出现魔纹、宝物库层数、死亡次数和稀有物品。
- 战利品清理：宝物库进行中即可在待清理列表看到库内所得；可设置数量阈值，单种超过阈值的物品不丢弃；稀有品默认保留，可在设置里按宝图和宝库展开，逐个点选是否清理。
- 开跑时背包已有解读好的宝图，开启“第一次挖宝前先买备用图”后会先去市场板买一张备用图再去挖。
- 组队模式（设置 → 通用 → 行为，默认关闭）：两个号都开插件、在本服组队后各自点开始。队长负责挖宝，出发去每个宝藏点前在小队频道发 `<flag>`；
  队员收到后自己传送、飞过去，停在宝藏点旁边，跟队长一起打宝箱守卫并按设置 Roll 点。队长到达后会等队员到齐再挖，并在日志里记录队员晚到了几秒。
  组队时传送门和宝物库仍交给玩家处理。
- 以下新功能按上游要求默认关闭，需要在设置里手动开启：进入传送魔纹、同大区最便宜服务器买图、提前买一张图、
  第一次挖宝前先买备用图、死刑减伤。跨服买图在组队时不会换服，运行结束或停止后会回到原服务器。
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

Works through the timeworn maps in your bags for you. Press **Start** and the plugin deciphers a map, travels to its dig spot, digs up the coffer, fights off the monsters that guard it, opens it, and moves on to the next map until your bags are empty. Once they are, it can gather the day's free map and buy more on the market board, if you turn those on. Portals are left to you unless you turn on treasure dungeons, and undersea maps are always left to you.

## Features

- **Decipher and dig**: reads each timeworn map you carry, works out where it points, and digs at the spot.
- **Travel**: teleports to the nearest aetheryte, then flies, rides or walks to the dig spot, and lands where it can dig.
- **Hands-off combat**: fights off every wave that guards a coffer with a bundled combat preset, then opens the coffer. As a tank, it can also use your short mitigation in the last seconds of an enemy cast aimed at you, and answer a treasure dungeon boss's tankbuster with the short mitigation plus one bigger cooldown such as Rampart, which nothing else uses (off by default).
- **Treasure dungeons** (solo, off by default): walks into the portal a coffer leaves and clears the dungeon behind it: coffers, guards, the gates or slot draws and the bonus round, then goes on with the next map. Cenote Ja Ja Gural (G17) and Vault Oneiron (G18) are supported; any other dungeon, and any you enter by hand, is left to you. Pick the gate, how to answer the Gambler's Lure (left to you by default, or the coffer opened as it is, or challenged by the loot: the run reads the card shown, plays for the most on average and plays safer when the coffer holds rare loot the cleanup keeps) and which enemies go first under Settings, Dungeons; bonus enemies can be left out per dungeon. A boss's adds that hit hard are killed before the boss, the mandragora party is left alone unless you ask for it, and the dungeon's loot is cleaned up as soon as the run is back outside.
- **Map choice**: pick which map tiers to run and whether the highest or the lowest goes first.
- **Loot rolls**: answers the Need or Greed window for the coffer's items the way you choose, or leaves it to you.
- **Daily map**: once a day, gathers the free timeworn map as a Miner or Botanist when the run has no map left, then switches back to your gearset (off by default). A Miner skips Gargantuaskin, whose only Miner nodes hold nothing but collectables.
- **Market board**: when no map is left in your bags and no daily map can be gathered, buys the next one on the market board under a price cap you set per tier, within a gil budget per run and never below the gil you choose to keep (off by default). With Lifestream installed, it can look the map up on Universalis and buy it on the cheapest world of your data center, then travel back once the run ends (off by default). It can also buy a second map ahead so the next one needs no trip to the board (off by default).
- **Recovery**: gets back up after a death; re-paths, jumps, or teleports out when it gets stuck.
- **After-run action**: stay logged in, return to the inn, log out, or close the game once the last map is done.
- **Auto-repair**: Dark Matter first, then your Grand Company mender or a repair NPC you pick.
- **Auto-consume**: keeps food and medicine buffs up between maps, at the start of each map and between treasure dungeon rounds, HQ first.
- **Loot cleanup** (off by default): notes what each coffer and treasure dungeon gives and discards whatever is not kept, between maps, before walking into a portal, and when a run finishes. Only an item your bags held none of before the coffer opened can go (an item that does not stack is the exception, as each copy has its own slot), never more copies than the coffer gave, and nothing from a map you paused the run in after its coffer opened. The noted list is forgotten when the run stops or ends and when you log out. Maps, anything you whitelist (right-click an item in your inventory) and, unless you turn it off, anything notable (green rarity and above, unique items, equipment, minions, mounts, orchestrion rolls furnishings and other unlocks) are always kept, and so is each map's rare loot unless you pick it for discarding.
- **Humanizer**: waits a random moment before each teleport, dig, coffer and fight, and takes city breaks between maps, so long sessions look less mechanical.
- **Pause & resume**: park a run without losing your place, and auto-pause while you're in a duty.
- **Party invites**: auto-declines incoming invites during a run after a random delay, with an optional reply message.
- **GM alert**: stops the bot when a GM is near, with optional toast, beeps, or custom commands.
- **Stop conditions**: a run ends when no enabled map is left, after a set number of maps or minutes, after failed maps in a row, or after a second knockout.
- **History**: every run recorded with its maps, gil, tomestones, portals, dungeons and how it ended. The totals show the cost (gil spent on maps) next to the revenue (gil looted, plus the rare loot at today's prices when loot prices are on, with the net below it), and they count the run under way as it goes. Per-map cards show each map's rare loot with an estimate of its worth from Universalis, and a map log lists every map with its dungeon floor, knockouts, rare loot and time taken.
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
