# [Reconsume](https://thunderstore.io/package/jiejasonliu/Reconsume/)

Survivors of the Void brings consumable items; but unfortunately, they feel underwhelming.\
Reconsume **restores a portion of consumed items at the beginning of each stage** and **allows them to be scrappable**.

## Features

- **Partial Restoration:** By default, restores 33% of your consumed items (rounded up) at the start of every stage.
  - Supports `power elixir`, `delicate watch`, and `dio's best friend`<sup><i>(off by default)</i></sup>
- **Item Scrapping:** Allows consumed items to be scrappable.
  - Note: consumed `dio's best friend` is off by default
- Highly configurable
    - Change the percentage of how many consumed items are restored each stage 
	- Change whether an item can be scrappable or refilled at the beginning of each stage
	- Change the healing strength of the `power elixir`

## Images

![Scrapping Consumed Items](https://i.imgur.com/ACZ9T4E.png)

## Installation

- Dependencies: [BepInExPack](https://thunderstore.io/package/bbepis/BepInExPack/) and [R2API](https://thunderstore.io/package/tristanmcpherson/R2API/)
- Use [r2modman](https://thunderstore.io/package/ebkr/r2modman/) to make the installation seamless and pain-free.
- Config files are generated when launching this mod for this first time.

## Changelog

- `1.0.4` **Balance Update**: Items no longer restore 100% by default.
  - Now restores 33% of a consumed stack per stage (configurable).
- `1.0.3` Fix broken scrapper due to Alloyed Collective (DLC3)
- `1.0.2` Fix broken scrapper due to `ItemDef.tier` changes in RoR2 v1.2.3
- `1.0.1` Allow Dio's Best Friend to be restored and scrappable; however, this is **disabled by default.**
- `1.0.0` Initial release