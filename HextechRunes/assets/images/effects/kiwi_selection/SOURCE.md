# ARAM: Mayhem augment selection VFX

Source: League of Legends 16.20 (Riot client), `Game/DATA/FINAL/UI.wad.client`.
The same files are also published on CommunityDragon under
`https://raw.communitydragon.org/latest/game/<source path, lowercase, .tex → .png>`.

`kiwi_selection_vfx.json` is a lossless re-encoding of the particle definitions
`ClientStates/Gameplay/UX/LoL/Kiwi/KiwiAugmentSelection/Particles/*` from the
UI bin of that WAD (11 systems, 118 emitters):

| System | Used for |
| --- | --- |
| `Augment_GoldReroll` | Golden reroll button, looping idle glow |
| `Augment_GoldReroll_Click` | Golden reroll button, click burst |
| `Augment_<Silver/Gold/Prismatic>_FlashInVFX` | Card appears when the screen opens |
| `Augment_<tier>_RefreshVFX` | Card rebuilds after a reroll (drawn under the card content) |
| `Augment_<tier>_RefreshOverlayVFX` | Flash over the card after a reroll (same emitters as FlashIn) |

Field names follow the original `VfxEmitterDefinitionData`. Every `Value*` is
normalized to `{t, v, p}`: key times, key values (always float lists), and the
optional per-component probability tables that multiply birth values. Texture
references are paths relative to `images/` without the extension.

`space` is not an original field. The mod's reroll button is larger relative to
the card than the original (125.6×76 on a 344×592 card instead of 78×48 on
364×586), so emitters drawn on the button outline (`Reroll_Button_Gold_*`
masks, edge sparkles and lens flares within 30 units of the button) are tagged
`button` and scaled with the button; everything else is tagged `card`.

Textures were decoded from the `.tex` files (BC1/BC3/BC7) to PNG without
recolouring. `AugmentCard_GoldReroll_CardSwipe` was downscaled from 2048² to
512² and the `SheenGlow` frames are kept at 1024²; the rest are original size.
The three card frames are identical to the existing
`images/ui/augmentcard_frame_*.png` and are referenced from there.

| File | Size | Source |
| --- | --- | --- |
| `effects/kiwi_selection/15.png` | 64x4 | `ASSETS/Shared/Particles/15.tex` |
| `effects/kiwi_selection/3026_items_ball32_02.png` | 128x128 | `ASSETS/Maps/Particles/Kiwi/3026_Items_ball32_02.tex` |
| `effects/kiwi_selection/3026_items_noise_02.png` | 128x128 | `ASSETS/Maps/Particles/Kiwi/3026_Items_Noise_02.tex` |
| `effects/kiwi_selection/4636_active_disslovel_wisp_tile.png` | 256x256 | `ASSETS/Maps/Particles/Kiwi/4636_Active_disslovel_Wisp_tile.tex` |
| `effects/kiwi_selection/6676_color-goldcoin22.png` | 64x64 | `ASSETS/Maps/Particles/Kiwi/6676_color-goldcoin22.tex` |
| `effects/kiwi_selection/ahri_base_mult_windblast.png` | 64x64 | `ASSETS/Maps/Particles/Kiwi/Ahri_Base_Mult_Windblast.tex` |
| `effects/kiwi_selection/ahri_base_q_speed.png` | 128x64 | `ASSETS/Maps/Particles/Kiwi/Ahri_Base_Q_speed.tex` |
| `effects/kiwi_selection/ahri_skin15_glow_1.png` | 512x512 | `ASSETS/Maps/Particles/Kiwi/Ahri_Skin15_Glow_1.tex` |
| `effects/kiwi_selection/akshan_skin10_colorgrad_02.png` | 128x64 | `ASSETS/Maps/Particles/Kiwi/Akshan_Skin10_colorGrad_02.tex` |
| `effects/kiwi_selection/ashe_skin84_prestige_speark.png` | 256x256 | `ASSETS/Maps/Particles/Kiwi/Ashe_Skin84_prestige_speark.tex` |
| `effects/kiwi_selection/augment_chauffeur_shape3.png` | 256x256 | `ASSETS/Maps/Particles/Kiwi/Augment_Chauffeur_shape3.tex` |
| `effects/kiwi_selection/augment_flashbang_noise_color.png` | 64x64 | `ASSETS/Maps/Particles/Kiwi/Augment_Flashbang_Noise_color.tex` |
| `effects/kiwi_selection/augmentcard_bg.png` | 512x512 | `ASSETS/UX/Cherry/Augments/AugmentSelection/AugmentCard_BG.tex` |
| `effects/kiwi_selection/augmentcard_goldreroll_cardswipe.png` | 512x512 | `ASSETS/UX/Kiwi/Augments/AugmentSelection/AugmentCard_GoldReroll_CardSwipe.tex` |
| `effects/kiwi_selection/augmentcard_sheenglow_gold.png` | 1024x1024 | `ASSETS/UX/Cherry/Augments/AugmentSelection/AugmentCard_SheenGlow_Gold.tex` |
| `effects/kiwi_selection/augmentcard_sheenglow_prismatic.png` | 1024x1024 | `ASSETS/UX/Cherry/Augments/AugmentSelection/AugmentCard_SheenGlow_Prismatic.tex` |
| `effects/kiwi_selection/augmentcard_sheenglow_silver.png` | 1024x1024 | `ASSETS/UX/Cherry/Augments/AugmentSelection/AugmentCard_SheenGlow_Silver.tex` |
| `effects/kiwi_selection/augmentcard_sheenglow_silver2.png` | 1024x1024 | `ASSETS/UX/Cherry/Augments/AugmentSelection/AugmentCard_SheenGlow_Silver2.tex` |
| `effects/kiwi_selection/augmentrefresh_mask.png` | 512x512 | `ASSETS/UX/Cherry/Augments/AugmentSelection/AugmentRefresh_Mask.tex` |
| `effects/kiwi_selection/augmenttag_frame_glow_smaller.png` | 256x256 | `ASSETS/Maps/Particles/Kiwi/AugmentTag_Frame_Glow_Smaller.tex` |
| `effects/kiwi_selection/base_glow_2.png` | 256x256 | `ASSETS/UX/Cherry/Augments/AugmentSelection/Base_Glow_2.tex` |
| `effects/kiwi_selection/defaultcoloroverlifetime.png` | 8x8 | `ASSETS/Maps/Particles/Cherry/DefaultColorOverlifetime.tex` |
| `effects/kiwi_selection/diana_skin47_r_up_cylinder.png` | 128x128 | `ASSETS/Maps/Particles/Kiwi/Diana_Skin47_R_up_cylinder.tex` |
| `effects/kiwi_selection/gradient_vert.png` | 4x128 | `ASSETS/UX/Cherry/Augments/AugmentSelection/Gradient_Vert.tex` |
| `effects/kiwi_selection/janna_base_r_aura_flames_softer.png` | 256x256 | `ASSETS/Maps/Particles/Kiwi/Janna_Base_R_Aura_Flames_Softer.tex` |
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
