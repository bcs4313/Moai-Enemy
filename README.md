### This mod only adds a moai enemy and its variants. If you want Easter Island with its secrets and items, check out the mod **Legend of the Moai**.

# Moai Enemy

Adds a Moai Enemy with Variants to help kill your entire squad :)

<br>
This enemy spawns outside on any planet, and has 
elemental variants to keep you on your toes.
<br>
<br>

Click below to see all the variants in action!
[![Moai Variant Showcase](https://cdn.allthepics.net/images/2025/02/13/Thumbnail6.png)](https://www.youtube.com/watch?v=0Ril2Wkut3g)

The Newest Variant! Check down below for more information:
<img src="https://i.imgur.com/E8Xp9na.png" width="200" height="200"/>&nbsp;&nbsp;&nbsp;&nbsp;
 
# Variants

**Normal**&nbsp;
* Your average Moai. Chases down the player to give them a big hug. *
* Spawn Time: Day, Rarity: Common

<img src="https://i.imgur.com/tvLW22f.png" width="200" height="250"/>&nbsp;&nbsp;&nbsp;&nbsp;

**Blue**&nbsp;
* Summons lightning in a range that gets more and more violent the closer he is.
* Do NOT mess with him.
* Spawn Time: Evening, Rarity: Rare

<img src="https://i.imgur.com/GO0xFzs.png" width="200" height="250"/>&nbsp;&nbsp;&nbsp;&nbsp;

**Angel**&nbsp;
* An Angel moai will patrol around players, looking for enemies.
* These moai are identified by their shiny halo on their heads.
* Spawn Time: Any, Rarity: ???

<img src="https://i.imgur.com/ta7DFl7.png" width="200" height="200"/>&nbsp;&nbsp;&nbsp;&nbsp;

**Red**&nbsp;
* A relentless, explosive psychopath.
* Rushes down players at extreme speeds.
* Likes to keep weak and lonely players organized (this is a warning)
* Spawn Time: Evening, Rarity: Uncommon

<img src="https://i.imgur.com/xmxqne4.png" width="200" height="200"/>&nbsp;&nbsp;&nbsp;&nbsp;

**Green**&nbsp;
* A picky yet messy carpenter.
* Capable of constructing various hazards (and rituals???)
* Wields ionizing plasma. Not sure how exactly but he won't tell me his secrets.
* Spawn Time: Evening, Rarity: Rare

<img src="https://i.imgur.com/KjwOUNp.png" width="200" height="200"/>&nbsp;&nbsp;&nbsp;&nbsp;

**Gold**
* A legendary Treasure Hunter.
* Very friendly (for a moai).
* Can be commanded to retrieve scrap with his keen sense of smell.
* Spawn Time: Day, Rarity: Epic

<img src="https://i.imgur.com/2wCeCkr.png" width="200" height="200"/>&nbsp;&nbsp;&nbsp;&nbsp;

**Purple**
* A master of quantum energy
* Captures and throws enemies at the player to quickly silence them
* Can capture and throw players as well.
* Somehow can teleport too? Oh god.
* Spawn Time: Evening, Rarity: Uncommon

<img src="https://i.imgur.com/jF66euT.png" width="200" height="200"/>&nbsp;&nbsp;&nbsp;&nbsp;

**Orange**
* An underground tomahawk (and beekeeper?)
* Can burrow underground, giving them a stealth advantage.
* Can shoot out of the ground, causing a damaging shockwave that launches players.
* Tends to sneak up on players underground and drag them underground, slowly suffocating and mauling them.
* May summon electric bees to assist them.
* Spawn Time: Daytime, Rarity: Uncommon

<img src="https://i.imgur.com/E8Xp9na.png" width="200" height="200"/>&nbsp;&nbsp;&nbsp;&nbsp;

**Size**&nbsp;
* Moai have a 20% chance to be larger/smaller than usual.
* Could be minor, or so severe that you can see him from a mile away.
* Affects sfx pitch.

# Special Behaviors
**Vroom Vroom**
* Normal moai have a special interaction when they are angels.
* If you have a key, you can "unlock" the moai and drive him.

<img src="https://i.imgur.com/quGQQkd.gif" width="380" height="240"/>&nbsp;&nbsp;&nbsp;&nbsp;

**Consumption**
* Moai need a lot of energy dragging their heavy bodies around, so they tend to get hungry.
* Moai may seek out very questionable items and devour them without a second thought.
* Devouring an object will turn the moai into an angel variant, for some time.

**Guardian Mode**
* Angel Moais charge and murder any threat it sees in order to protect the player.
* Angel Moais are sophisticated, refusing to eat any disgusting scrap/player.

**There is no escape**
* Moais may enter/exit the facility at any time.
* The closer a moai is to an entrance/exit, the more likely they will enter it.

<img src="https://i.imgur.com/2scO4jB.gif" width="380" height="240"/>&nbsp;&nbsp;&nbsp;&nbsp;

<br>
The mod is configurable. You can set a moai's rarity, size, speed, and sound volume in the main menu.

## Currently Planned Features: 
* Angel interactions for remaining variants (blue, red, green)
* Removing LethalNetworkAPI as a dependency.
* Optimizing AI performance.

I am open to any feedback and/or requests for new features.
Check my thread on the lethal company modding discord: 

https://discord.com/channels/1168655651455639582/1231401205234667571


or message me through my email at bcs4313@rit.edu. 

Also I would love to watch your friends be slaughtered by moais (yes I am a sadist)!</h4>

<details>
<summary>Changelog (PRE 2.0)</summary>

1.0.0
* Mod Release 

1.01
* Manifest.json fix.

1.02-04
* Increased sight range. Made them much less common.

1.14
* Added configuration options for the mod, including:
- Enemy Rarity
- Enemy Size
- Enemy Speed
- Chase Volume
- Idle Volume
Lowered the default volume of moai chase sound fx.

1.15
- Sound distance adjustments and AI bugfix.\

1.16
- Increased mod compatibility with other enemy mods.

1.17
- Added strict spawn control params, cutting moai spawn rates by 1/6.
- Added group spawn chance, (18% per spawn success, adding 1-5 extra moai).

1.20
- Added moai size variants (with pitch adjustments). Big moais are horrifying.
- Adjusted Nav-mesh hitbox and hit collision to accurately represent moai size in navigation.
- Moai should no longer be able to phase through doors (if experienced, please report).
- Size Variant Chance Adjustable in settings.
- Made moai give up chase from a significantly closer distance.
- Moai give up chase closer if they do not see the player.

1.32
- Moai sounds/actions/sizes/pitch are completely synchronized on client/server.
- Gave the moai the ability to consume various things, with the desired objects being questionable sometimes.
- Gave the ability to eat (spoiler)-> ---------------------------------------------------------------------------------------------------------------> corpses, with a custom disgusting sound effect
- Fixed bug when host dies from moai while inside ship/factory/etc, causing ship desync.

1.5.0
Added Angel Variant:
- Angel moai have a glowing halo over their head
- Angel Moai will follow the player, essentially guarding them and attack enemies that are nearby. 
- They also have additional health proportional to the scrap value of the eaten object.
- Angel Moai don't try to eat scrap or players in this state.
- Angel Moai heal the player if they collide with them,
- Angel Moai stay as an angel for a fixed time, depending on how valuable the loot fed was.
- Moais may spawn as an angel (15% chance).
- moai can now be killed
- added death animation
- added death and hit sound effect
- added ability for moai to become angels after eating scrap
- some moai are not distracted by scrap during chase (33% chance)
- moai follow distance nerfed when they can't see the player
- moai synchronizes destruction of corpse after it is eaten

1.5.5
- Added ability for moai to enter indoor areas.
The closer they are to the entrance, the higher the chance.
- Angel moai will follow players indoors to guard them.
- Fixed v50 error that is caused from an updated HitEnemy method.
- Fixed a really annoying navigation spam error.
- Fixed an issue where moai would sometimes have a much larger follow range than they should.
- Nerfed angel variant duration from eating scrap.
- Nerfed blue moai rarity.

1.5.7
- Completely fixed Blue Moai Lightning synchronization (fully tested).
- Angel moai now will heal both clients and the host if they collide with them.

1.6.0
- Migrated from out-of-date dependency LC_API to LethalNetworkAPI.
- This should greatly increase mod compatibility and reduce synchronization issues.

1.6.2
- Added walk animation.
- Added scrap eating animation (fairly minor, may be improved later).
- Reduced chance for moai to ignore scrap during chase.
- Added transition smoothing to all animations.

1.6.3
- CRITICAL BUGFIX: Moai are no longer invisible on client side due to spawn control parameters.
- Moai vision area degrees boosted due to movement sway.

1.6.5
- Spawnrate overhaul:
- Normal moai have an exact 1/3 chance to be able to spawn AT ALL on each planet.
- 13% chance for spawnrate multiplier to be 16%
- 10% chance for spawnrate multiplier to be 50%
- 10% chance for spawnrate multiplier to be 100% (moai hell).
- Increased tendency for many normal moai to spawn in one day (if applicable).
- Blue moais are unaffected by this change.

1.7.0
- Changed moai and blue moai textures to be more detailed and fit properly in the game.
- Made it so you can't hear all moai sounds as far away as before.
- Nerfed moai hp to 6 (previously 8).
- Nerfed moai dmg to enemies (2->1).
- Fixed moai ai being buggy while inside a ship (camping) 
and getting confused when the player is at certain elevations.
- Moais no longer go for food during chase (making it safe to drop items and run)
- Added stamina mechanic.
- Moai chase players/enemies for a limited time, before having to recover.
- Moai may chase for a shorter period if their stamina isn't fully recovered.
- Moai start with 0 stamina on spawn.
- Moai attach corpses to their mouth now when eating them.
- Fixed client animation bugs from client using an RPC it shouldn't.
- Other general bugfixes.

1.7.2
- Redesigned idle/search sounds.
- Reduced chance for moai to be spawning on any day (33%->20%)
- 8% chance for spawnrate multiplier to be 16%
- 6% chance for spawnrate multiplier to be 50%
- 6% chance for spawnrate multiplier to be 100% (moai hell).

1.8.0
- Added Red Moai (Berzerk Moai)
- Red Moai pursue and stare at players, attacking when they get angry enough.
- Red Moai will attack via a blitz attack, or kidnap a player.
- Blitz:
- Moai charges up and launches 4 times, causing explosions in its wake.
- A blitz can be dodged effectively with good footwork.
- Kidnap:
- Steals the player, taking them somewhere inside the factory.
- The kidnapping red moai may decide to lock you behind a door if it feels like it.
- Conditions for stealing the player:
Carry weight < 40
No defensive item in inventory
Player is Alone
Player is not standing on a rocky surface 

- Moai now retailiate when they are hit.
- Made sliding fx louder.

1.8.2
- Added daily spawn distribution settings (advanced)
- Allows you to define a probability distribution for moai spawnrates on any given day.
- (For example: 10%300%, 5%100% would be a 10% chance on a day to have 300% spawnrate, and a 5% chance for 100% spawnrate for ALL moai variants.
- Added separate spawnrate multipliers for each variant. 
- Moai now only influence enemy spawnrates depending on their rarity (they used to reduce enemy spawnrates no matter what)
</details>
<details>
<summary>Changelog (POST 2.0)</summary>

# 2.0
- Added Moon: Easter island
Properties:
Risk: S/S++
Cost: 650
Guaranteed moai spawns (unless disabled)
For now supports 3 weather types (Stormy, Eclipsed, Rainy)
Unique Features: 
Volcanic Eruptions
- There is a 50% chance for the volcano to erupt on a day (1h duration).
- Eruption sends volcanic rocks flying all over the map, causing explosions on colliding with terrain.
Cabin:
- has tools and a secret to help survive on the island.
Golden Moai Item:
- Chance to spawn indoors. Guaranteed outdoor spawn on easter island.
- Red Moais get angrier faster, making them more likely to attack/kidnap.

2.0.1
- Asset bundle loading bugfix.

2.1.0
- Split moai enemy from easter island as separate dependencies 
- Legend of the Moai installs easter island and moai enemy.

2.2.2
- Added Green Variant (Builder)
- Green Moais have lasers built into their eyes that are capable of producing matter.
- They can currently construct mines, turrets, and a consumption circle (evil ritual).
- They also have a ranged attack, of which they fire bouncy plasma balls at players/enemies.
- Angel moais are much more prone to attacking enemies (extremely passive last update)
- Moais now react to being hit by the player regardless of what they are doing (eating, heading to entrance, angel, etc)
- Moais now auto-prioritize the nearest and most expensive food scrap/corpses.
- Heavily reduced lightning strike rate for blue moai when close.
- Swapped rarities for variants (Blue->Rare) (Red->Uncommon)
- Added Moai Size cap option.

2.2.3
- Updated ReadMe.

2.2.4
- Nerfed spawnrates for all variants (didn't account for green moai taking up more of the spawn pool)

2.2.5
- Update for v55 compatibility.

2.2.6
- AI performance optimizations. Removed around 92% of overhead.

2.3.0
- Added Gold Moai variant.
- Spawn Time: Day, Rarity: Epic
- Golden Moais are ascended, making them almost always friendly to the player.
- You can interact with a Gold Moai (E key) to make it hunt for factory scrap.
- Gold moais bring back the scrap for the player to retrieve.
- Moai no longer get stuck in place looking for an item, guarding, chasing, etc.
- All moais can now be scanned (No real bestiary entry yet though).
- Made entering/exiting factory behavior more reliable.
- Angel moais have more variance in their travel locations, preventing them from clumping together.

2.3.5
- Added ability for the normal moai variant to be "tamed" and driven as a vehicle.
- To tame, the moai must be an angel and you need a key.
- Moai vehicles automatically park, navigate ladders, gaps, etc.
- Angel navigation radius reduced to more effectively guard the player.

2.3.7
- Red, green, and blue moai spawn times have been moved.
- They start spawning at 12PM and peak at 2PM, with a moderate spawn rate through the night.
- Normal moai spawnrate reduced moderately to compensate for the change.
- Moai no longer eat the golden head item (easter island compatability)
- Made gold moai more rare.

2.3.8
- Moai eating hives destroys the bees with it, preventing error spam and lag.
- Blue moai rarity increased.

2.4.0
- BETA RELEASE OF NEW ENEMY (Soul Devourer)
- He will become more complex in future updates.

2.4.1 
- Removed natural of the Soul Devourer.
- Changed the chance for the Soul Devourer to spawn from eating a player (50%->20%)

2.4.2
- Added option to disable moai from being able to eat scrap.

2.5.0
- Added purple moai variant
- Purple moais seek out enemies and trap them in a quantum field. This disables the enemy from acting.
- A purple moai will throw this enemy at a player when they see them.
- Purple moais will teleport around the player to confuse them.
- Purple moais place quantum field traps to grab the player.
- After some time from grabbing the player, the player will be thrown a randomized distance.


- Gave all moai angels Christmas hats :D
- Added setting to adjust the chance for moais to spawn as angels
- reduced the mod's size (48MB->18.7MB)

2.5.1
- Minor bugfix for clientside animations on normal moai.

2.5.2
- Removed Christmas Hats.

2.5.3
- Added Soul Devourer Death Animation.
- Slightly buffed its hp.
- Prevented Soul Devourers from damaging each other.

2.5.4
- Added Map Radar icons for all variants.

2.5.5
- Updated Readme.

2.5.6
- Improved existing mod trailer.

2.6.0
- Added an orange moai variant.
- Orange moais can burrow underground, giving them a stealth advantage.
- Orange moais can shoot out of the ground, causing a damaging shockwave that launches players.
- Be careful! Orange moais can sneak up on players underground and drag them in, slowly suffocating 
- and mauling them.
- Orange moais have the ability to summon electric bees to assist them.

2.6.1
- Fixed the blue moai being broken for some reason.

2.6.2
- Fixed a bug where orange moai would permanently leave hives around that are uninteractable.
- Removed LethalNetworkAPI as a dependency
- Updated Soul Devourer Secret Enemy to have the same animations and AI as the up-to-date one
- Made Orange Moais moderately more rare, made normal moais more common
- Reduced the space requirement for the mod 19.4MB -> 14.2MB
- Added a setting to easily adjust ALL moai rarity values and chance to spawn on a day at the same time.

2.6.3
- V73 Compatibility update
- Large boost to spawnrates (moai are generally just too rare according to feedback)
- For people who want to keep moai's rare, change the simple spawn multiplier in the config to 0.67
</details>
