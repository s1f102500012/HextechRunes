# ARAM: Mayhem augment selection VFX

Source: League of Legends 16.20 (Riot client), `Game/DATA/FINAL/UI.wad.client`.
The same files are also published on CommunityDragon under
`https://raw.communitydragon.org/latest/game/<source path, lowercase, .tex → .png>`;
the UI layout that places the systems is
`game/clientstates/gameplay/ux/lol/kiwi/kiwiaugmentselection/uibase.cdtb.bin.json`.

## Systems

The UI bin of that WAD defines **25** particle systems under
`ClientStates/Gameplay/UX/LoL/Kiwi/KiwiAugmentSelection/Particles/`.
`kiwi_selection_vfx.json` is a lossless re-encoding of the **21** that the mod
plays (223 emitters). Each one is attached by a `UiElementParticleSystemData`
node in `.../KiwiAugmentSelection/UIBase`; the node and its `Layer` decide when
and where it plays:

| UI node (Layer) | System | Mod trigger |
| --- | --- | --- |
| `RerollButton/GoldenReroll` (5) | `Augment_GoldReroll` | Golden reroll available: loops on the button |
| `GoldenRerollClick` (9) | `Augment_GoldReroll_Click` | Golden reroll pressed |
| `Augment_IdleVFX` (5) | `Augment_<tier>_IdleVFX` | Card on screen: runs until the card is freed, under the card content |
| `Augment_HoverVFX` (6) | `Augment_<tier>_HoverVFX` | Mouse hover or controller focus on the card; fades out on leave, under the content |
| `Augment_NotPickedVFX` (6) | `Augment_StaticVFX` | A choice is confirmed: on every card that was not chosen, under the content |
| `Augment_RefreshVFX` (7) | `Augment_<tier>_RefreshVFX` | Card rerolled: card rebuild, under the content |
| `Augment_FlashInVFX` (21) | `Augment_<tier>_FlashInVFX` | Screen opens: all cards at once, over the content |
| `Augment_RefreshOverlayVFX` (21) | `Augment_<tier>_RefreshOverlayVFX` | Card rerolled: flash over the content (same emitters as FlashIn) |
| `Augment_PickedVFX` (25) | `Augment_<tier>_SelectedVFX` | A choice is confirmed: on the chosen card, over the content |

The UI data names the Silver variant; `<tier>` is replaced by the card's rarity
at runtime. Card icon is Layer 15 and name/description 20, so systems with a
lower Layer are inserted between the card frame and the card content.

Not played by the mod (not exported):

| UI node (Layer) | System | Why |
| --- | --- | --- |
| `AugmentSelection_Background/AugmentSelection_BackgroundVFX` (2) | `Augment_ChoiceBGVFX` | A blue "magic flow" band drawn on the bare screen behind the cards (rect 586×586 centred on the middle card). The mod's cards sit inside a container panel with its own background, so there is no free layer between the panel and the cards to host it without restructuring the layout; left for a later pass. |
| `AugmentQuestGroup/Augment_Quest_VFX` (40) | `Augment_QuestCard_VFX` | Glow on the quest badge of quest augments. The mod has no quest augments. |
| `GuaranteedElementsGroup/NewVFX_Card` (21) | `Augment_TextNew_Card` | Plays with the "guaranteed new" label (`Kiwi_Augment_Guaranteed_New_Text`). The mod has no guaranteed-new rule. |
| `GuaranteedElementsGroup/NewVFX_Text` (7) | `Augment_TextNew_TextGlow` | Glow behind that label. |

The bin's other two entries (`0xbb109c9b`, `0xe0d5fc7e`) are not particle
systems.

## Particle origin

Every card node above has `UIRect` Position `[618, 237]`, Size `[364, 586]` in
the 1600×1200 source layout, Anchors `(0.5, 1.0)` and `IgnoreGlobalScale`.
**The particle origin is the centre of that rect** (800, 530), not the anchor
point. The anchors only say how the rect follows the screen's bottom-centre when
the resolution changes. The data agrees with a centred origin everywhere:

- `BASE_Frame` quads are 2×187.5 units = 590 px at 1.575 px/unit, i.e. the card
  height (586), centred on the origin; with a bottom-centre origin they would
  sit half a card too high.
- Card edge sparkles are placed at y = +180 / −185 and x = ±110 units, symmetric
  about the rect centre (half card = 186 × 116 units).
- On the reroll button (rect `[765, 848]`, 78×48) the edge sparkles sit at
  ±23 / ±14 units, symmetric about the button centre (half button = 24.8 × 15.2
  units).

That is why the mod's FlashIn/Refresh (and now Idle/Hover/Selected/Static) use
the card centre as origin and line up. For the button systems, the rects put the
original card centre 342 px = 217 units above the button centre; the mod anchors
their card-space emitters 200 units above the button (`Sparklies_CENTER` at
y = 200), measured from the mod's actual card centre.

## Data format

Field names follow the original `VfxEmitterDefinitionData`. Every `Value*` is
normalized to `{t, v, p}`: key times, key values (always float lists), and the
optional per-component probability tables that multiply birth values. Texture
references are paths relative to `images/` without the extension. A particle
lifetime of −1 means the particle stays until the system is stopped (Idle and
Hover use this for the persistent frame glow).

`space` is not an original field. The mod's reroll button is larger relative to
the card than the original (125.6×76 on a 344×592 card instead of 78×48 on
364×586), so in the two golden reroll systems the emitters drawn on the button
outline (`Reroll_Button_Gold_*` masks, edge sparkles and lens flares within 30
units of the button) are tagged `button` and scaled with the button. Every
emitter of a system attached to the card is tagged `card`.

## Textures

Textures were decoded from the `.tex` files (BC1/BC3/BC7) to PNG without
recolouring. `AugmentCard_GoldReroll_CardSwipe` was downscaled from 2048² to
512² and `AugmentHover_Glow_Mask` from 1024² to 512²; the `SheenGlow` frames are
kept at 1024²; the rest are original size. The three card frames are identical
to the existing `images/ui/augmentcard_frame_*.png` and are referenced from
there.

| File | Size | Source |
| --- | --- | --- |
| `effects/kiwi_selection/15.png` | 64x4 | `ASSETS/Shared/Particles/15.tex` |
| `effects/kiwi_selection/3026_base_glow02.png` | 128x128 | `ASSETS/Maps/Particles/Kiwi/3026_Base_Glow02.tex` |
| `effects/kiwi_selection/3026_items_ball32_02.png` | 128x128 | `ASSETS/Maps/Particles/Kiwi/3026_Items_ball32_02.tex` |
| `effects/kiwi_selection/3026_items_noise_02.png` | 128x128 | `ASSETS/Maps/Particles/Kiwi/3026_Items_Noise_02.tex` |
| `effects/kiwi_selection/4636_active_disslovel_1.png` | 256x256 | `ASSETS/Maps/Particles/Kiwi/4636_Active_disslovel_1.tex` |
| `effects/kiwi_selection/4636_active_disslovel_wisp_tile.png` | 256x256 | `ASSETS/Maps/Particles/Kiwi/4636_Active_disslovel_Wisp_tile.tex` |
| `effects/kiwi_selection/6676_color-goldcoin22.png` | 64x64 | `ASSETS/Maps/Particles/Kiwi/6676_color-goldcoin22.tex` |
| `effects/kiwi_selection/ahri_base_mult_windblast.png` | 64x64 | `ASSETS/Maps/Particles/Kiwi/Ahri_Base_Mult_Windblast.tex` |
| `effects/kiwi_selection/ahri_base_q_speed.png` | 128x64 | `ASSETS/Maps/Particles/Kiwi/Ahri_Base_Q_speed.tex` |
| `effects/kiwi_selection/ahri_skin15_glow_1.png` | 512x512 | `ASSETS/Maps/Particles/Kiwi/Ahri_Skin15_Glow_1.tex` |
| `effects/kiwi_selection/akshan_skin10_colorgrad_02.png` | 128x64 | `ASSETS/Maps/Particles/Kiwi/Akshan_Skin10_colorGrad_02.tex` |
| `effects/kiwi_selection/alphaslice_mesh.png` | 256x256 | `ASSETS/Particles/alphaslice_mesh.tex` |
| `effects/kiwi_selection/alphaslice_mesh2.png` | 512x512 | `ASSETS/Particles/alphaslice_mesh2.tex` |
| `effects/kiwi_selection/ashe_skin84_prestige_speark.png` | 256x256 | `ASSETS/Maps/Particles/Kiwi/Ashe_Skin84_prestige_speark.tex` |
| `effects/kiwi_selection/augment_ceremony_generic_prism_color.png` | 512x512 | `ASSETS/Maps/Particles/Kiwi/Augment_Ceremony_Generic_Prism_color.tex` |
| `effects/kiwi_selection/augment_chauffeur_shape3.png` | 256x256 | `ASSETS/Maps/Particles/Kiwi/Augment_Chauffeur_shape3.tex` |
| `effects/kiwi_selection/augment_flashbang_noise_color.png` | 64x64 | `ASSETS/Maps/Particles/Kiwi/Augment_Flashbang_Noise_color.tex` |
| `effects/kiwi_selection/augment_jeweledgauntlet_lens-rainbow.png` | 256x256 | `ASSETS/Maps/Particles/Kiwi/Augment_JeweledGauntlet_lens-rainbow.tex` |
| `effects/kiwi_selection/augmentcard_bg.png` | 512x512 | `ASSETS/UX/Cherry/Augments/AugmentSelection/AugmentCard_BG.tex` |
| `effects/kiwi_selection/augmentcard_goldreroll_cardswipe.png` | 512x512 | `ASSETS/UX/Kiwi/Augments/AugmentSelection/AugmentCard_GoldReroll_CardSwipe.tex` |
| `effects/kiwi_selection/augmentcard_notpicked.png` | 512x512 | `ASSETS/UX/Cherry/Augments/AugmentSelection/AugmentCard_NotPicked.tex` |
| `effects/kiwi_selection/augmentcard_sheenglow_gold.png` | 1024x1024 | `ASSETS/UX/Cherry/Augments/AugmentSelection/AugmentCard_SheenGlow_Gold.tex` |
| `effects/kiwi_selection/augmentcard_sheenglow_prismatic.png` | 1024x1024 | `ASSETS/UX/Cherry/Augments/AugmentSelection/AugmentCard_SheenGlow_Prismatic.tex` |
| `effects/kiwi_selection/augmentcard_sheenglow_silver.png` | 1024x1024 | `ASSETS/UX/Cherry/Augments/AugmentSelection/AugmentCard_SheenGlow_Silver.tex` |
| `effects/kiwi_selection/augmentcard_sheenglow_silver2.png` | 1024x1024 | `ASSETS/UX/Cherry/Augments/AugmentSelection/AugmentCard_SheenGlow_Silver2.tex` |
| `effects/kiwi_selection/augmenthover_glow_mask.png` | 512x512 | `ASSETS/UX/Cherry/Augments/AugmentSelection/AugmentHover_Glow_Mask.tex` |
| `effects/kiwi_selection/augmenthover_mask.png` | 512x512 | `ASSETS/UX/Cherry/Augments/AugmentSelection/AugmentHover_Mask.tex` |
| `effects/kiwi_selection/augmentrefresh_mask.png` | 512x512 | `ASSETS/UX/Cherry/Augments/AugmentSelection/AugmentRefresh_Mask.tex` |
| `effects/kiwi_selection/augmenttag_frame_glow_smaller.png` | 256x256 | `ASSETS/Maps/Particles/Kiwi/AugmentTag_Frame_Glow_Smaller.tex` |
| `effects/kiwi_selection/base_glow_2.png` | 256x256 | `ASSETS/UX/Cherry/Augments/AugmentSelection/Base_Glow_2.tex` |
| `effects/kiwi_selection/cherry_gradient_diagonal.png` | 256x256 | `ASSETS/UX/Cherry/Augments/AugmentSelection/Cherry_Gradient_Diagonal.tex` |
| `effects/kiwi_selection/defaultcoloroverlifetime.png` | 8x8 | `ASSETS/Maps/Particles/Cherry/DefaultColorOverlifetime.tex` |
| `effects/kiwi_selection/diana_skin47_r_up_cylinder.png` | 128x128 | `ASSETS/Maps/Particles/Kiwi/Diana_Skin47_R_up_cylinder.tex` |
| `effects/kiwi_selection/gradient_vert.png` | 4x128 | `ASSETS/UX/Cherry/Augments/AugmentSelection/Gradient_Vert.tex` |
| `effects/kiwi_selection/janna_base_r_aura_flames_softer.png` | 256x256 | `ASSETS/Maps/Particles/Kiwi/Janna_Base_R_Aura_Flames_Softer.tex` |
| `effects/kiwi_selection/kiwi_augmentcard_dissolve_01.png` | 256x256 | `ASSETS/Maps/Particles/Kiwi/Kiwi_AugmentCard_Dissolve_01.tex` |
| `effects/kiwi_selection/kogmaw_skin55_diamondtexture_1.png` | 256x256 | `ASSETS/Maps/Particles/Kiwi/KogMaw_Skin55_DiamondTexture_1.tex` |
| `effects/kiwi_selection/reroll_button_gold_1.png` | 256x128 | `ASSETS/UX/Kiwi/Augments/AugmentSelection/Reroll_Button_Gold_1.tex` |
| `effects/kiwi_selection/reroll_button_gold_2.png` | 256x128 | `ASSETS/UX/Kiwi/Augments/AugmentSelection/Reroll_Button_Gold_2.tex` |
| `effects/kiwi_selection/samira_base_p_screen_flames_soft.png` | 256x256 | `ASSETS/Maps/Particles/Kiwi/Samira_Base_P_Screen_Flames_Soft.tex` |
| `effects/kiwi_selection/shen_skin49_vertical_mask_z02_01.png` | 256x256 | `ASSETS/Maps/Particles/Kiwi/Shen_Skin49_Vertical_Mask_Z02_01.tex` |
| `effects/kiwi_selection/white.png` | 8x8 | `ASSETS/Maps/Particles/Kiwi/White.tex` |
| `ui/augmentcard_frame_gold.png` | 512x512 | `ASSETS/UX/Cherry/Augments/AugmentSelection/AugmentCard_Frame_Gold.tex` |
| `ui/augmentcard_frame_prismatic.png` | 512x512 | `ASSETS/UX/Cherry/Augments/AugmentSelection/AugmentCard_Frame_Prismatic.tex` |
| `ui/augmentcard_frame_silver.png` | 512x512 | `ASSETS/UX/Cherry/Augments/AugmentSelection/AugmentCard_Frame_Silver.tex` |

`reroll_button_gold_1/2.png` replace the earlier copies in `images/ui/`, which
had been quantized to a palette / 1-bit alpha and lost the soft glow edge.
