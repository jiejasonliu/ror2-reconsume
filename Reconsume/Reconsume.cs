using BepInEx;
using BepInEx.Configuration;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using RoR2;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Reconsume
{

    [BepInDependency(R2API.R2API.PluginGUID)]
    [BepInPlugin("com.jiejasonliu.Reconsume", "Reconsume", "1.0.0")]
	public class Reconsume : BaseUnityPlugin
	{
        protected Dictionary<ItemDef, ItemDef> candidateItems;
        protected Dictionary<ItemDef, CandidateItemConfigData> candidateItemsConfigData;

        // power elixir config
        protected static ConfigEntry<bool> RefillPowerElixir, ScrapConsumedPowerElixir;
        protected static ConfigEntry<float> HealStrengthPowerElixir;

        // delicate watch config
        protected static ConfigEntry<bool> RefillDelicateWatch, ScrapConsumedDelicateWatch;

        public void Awake()
        {
            Log.Init(Logger);

            SetupConfiguration();

            // hooks
            On.RoR2.Run.Start += Init_CandidateItems;
            On.RoR2.SceneDirector.PopulateScene += StageRestore_CandidateItems;
            On.EntityStates.Scrapper.ScrappingToIdle.OnEnter += DropScrap_CandidateItems;
            IL.RoR2.PickupPickerController.SetOptionsFromInteractor += IL_ScrapperWhiteList_CandidateItems;
            IL.RoR2.HealthComponent.UpdateLastHitTime += IL_AlterHealStrength_PowerElixir;

            Log.LogInfo($"{nameof(Reconsume)}::{nameof(Awake)}() done.");
        }

        private void SetupConfiguration()
        {
            // power elixir
            RefillPowerElixir = Config.Bind("PowerElixir", nameof(RefillPowerElixir), true, "Restore power elixir at the beginning of each stage");
            ScrapConsumedPowerElixir = Config.Bind("PowerElixir", nameof(ScrapConsumedPowerElixir), true, "Allow scrapping consumed power elixirs");
            HealStrengthPowerElixir = Config.Bind("PowerElixir", nameof(HealStrengthPowerElixir), 0.25f, "Heal strength of power elixir (vanilla default is 0.75)");

            // delicate watch
            RefillDelicateWatch = Config.Bind("DelicateWatch", nameof(RefillDelicateWatch), true, "Restore delicate watch at the beginning of each stage");
            ScrapConsumedDelicateWatch = Config.Bind("DelicateWatch", nameof(ScrapConsumedDelicateWatch), true, "Allow scrapping consumed delicate watches");
        }

        /// find items from game content and setup mappings with configuration
        private void Init_CandidateItems(On.RoR2.Run.orig_Start orig, Run self)
        {
            orig(self);

            // consumed item -> unconsumed item
            candidateItems = new()
            {
                { DLC1Content.Items.HealingPotionConsumed, DLC1Content.Items.HealingPotion },
                { DLC1Content.Items.FragileDamageBonusConsumed, DLC1Content.Items.FragileDamageBonus },
            };

            // consumed item -> polymorphic config data
            candidateItemsConfigData = new()
            {
                { DLC1Content.Items.HealingPotionConsumed, new CandidateItemConfigData(RefillPowerElixir, ScrapConsumedPowerElixir) },
                { DLC1Content.Items.FragileDamageBonusConsumed, new CandidateItemConfigData(RefillDelicateWatch, ScrapConsumedDelicateWatch) },
            };
        }

        /// restore candidate items at the beginning of each stage
        private void StageRestore_CandidateItems(On.RoR2.SceneDirector.orig_PopulateScene orig, SceneDirector self)
        {
            orig(self);

            // host: apply for each player on the server
            foreach (var playerController in PlayerCharacterMasterController.instances)
            {
                var player = playerController.master;
                var playerInventory = player.inventory;
                List<ItemIndex> itemIndexList = new(playerInventory.itemAcquisitionOrder);

                // find candidate items in player's inventory
                foreach (var itemIndex in itemIndexList)
                {
                    var consumedItemDef = ItemCatalog.GetItemDef(itemIndex);

                    if (candidateItems.TryGetValue(consumedItemDef, out ItemDef refilledItemDef))
                    {
                        // config guard check
                        if (!candidateItemsConfigData[consumedItemDef].RefillOnStage)
                        {
                            continue;
                        }

                        var itemCount = playerInventory.GetItemCount(consumedItemDef);
                        playerInventory.RemoveItem(consumedItemDef, itemCount);
                        playerInventory.GiveItem(refilledItemDef, itemCount);
                    }
                }
            }
        }


        /// drop the correct scrap tier from the candidate items
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Member Access", "Publicizer001:Accessing a member that was not originally public", Justification = "Alternative to Reflection")]
        private void DropScrap_CandidateItems(On.EntityStates.Scrapper.ScrappingToIdle.orig_OnEnter orig, EntityStates.Scrapper.ScrappingToIdle self)
        {
            orig(self);

            foreach (var candidateItem in candidateItems)
            {
                var consumedItem = candidateItem.Key;
                var refilledItem = candidateItem.Value;

                // check if last scrapped item was candidate item
                var lastScrappedItemIndex = self.scrapperController.lastScrappedItemIndex;
                if (lastScrappedItemIndex == consumedItem.itemIndex)
                {
                    PickupIndex pickupIndex = PickupIndex.none;
                    switch (refilledItem.tier)
                    {
                        case ItemTier.Tier1:
                            pickupIndex = PickupCatalog.FindPickupIndex("ItemIndex.ScrapWhite");
                            break;
                        case ItemTier.Tier2:
                            pickupIndex = PickupCatalog.FindPickupIndex("ItemIndex.ScrapGreen");
                            break;
                        case ItemTier.Tier3:
                            pickupIndex = PickupCatalog.FindPickupIndex("ItemIndex.ScrapRed");
                            break;
                        case ItemTier.Boss:
                            pickupIndex = PickupCatalog.FindPickupIndex("ItemIndex.ScrapYellow");
                            break;
                    }

                    // scrap with the corresponding scrap tier
                    if (pickupIndex != PickupIndex.none)
                    {
                        self.foundValidScrap = true;
                        Transform transform = self.FindModelChild(EntityStates.Scrapper.ScrappingToIdle.muzzleString);
                        PickupDropletController.CreatePickupDroplet(
                          pickupIndex,
                          transform.position,
                          Vector3.up * EntityStates.Scrapper.ScrappingToIdle.dropUpVelocityStrength +
                            transform.forward *
                              EntityStates.Scrapper.ScrappingToIdle.dropForwardVelocityStrength
                        );
                        self.scrapperController.itemsEaten -= 1;
                    }
                }
            }
        }

        /// whitelist the candidate items from interactables (i.e. scrappers)
        private void IL_ScrapperWhiteList_CandidateItems(ILContext il)
        {
            ILCursor c = new ILCursor(il);
            c.GotoNext(
                c => c.MatchLdarg(0),    // 00EF (ldarg.0)
                c => c.MatchLdloc(2),    // 00F0 (ldloc.2) 
                c => c.MatchCallvirt("System.Collections.Generic.List`1<RoR2.PickupPickerController/Option>", "ToArray")
            );

            c.Emit(OpCodes.Ldarg_1);    // push Interactor
            c.Emit(OpCodes.Ldloc_2);    // push List<Pickup...Option>
            
            // perform scoped CIL virtcall and update options with whitelisted items
            // pops ldarg_1 (activator) and ldloc_2 (optionList) after delegate is invoked
            c.EmitDelegate<Action<Interactor, List<PickupPickerController.Option>>>((activator, optionList) =>
            {
                if (activator)
                {
                    CharacterBody component = activator.GetComponent<CharacterBody>();
                    if (component && component.inventory)
                    {
                        Inventory playerInventory = component.inventory;
                        List<ItemIndex> itemIndexList = new(playerInventory.itemAcquisitionOrder);

                        // find candidate items in player's inventory
                        foreach (var itemIndex in itemIndexList)
                        {
                            var consumedItemDef = ItemCatalog.GetItemDef(itemIndex);
                            if (candidateItems.TryGetValue(consumedItemDef, out ItemDef refilledItemDef))
                            {
                                // config guard check
                                if (!candidateItemsConfigData[consumedItemDef].CanScrap)
                                {
                                    continue;
                                }

                                optionList.Add(new PickupPickerController.Option
                                {
                                    available = true,
                                    pickupIndex = PickupCatalog.FindPickupIndex(itemIndex)
                                });
                            }
                        }
                    }
                }
            });
        }

        /// alter the heal strength of the power elixir to balance it being "refillable"
        private void IL_AlterHealStrength_PowerElixir(ILContext il)
        {
            ILCursor c = new ILCursor(il);
            c.GotoNext(
                c => c.MatchLdarg(0),                       // 00B5 (ldarg.0)
                c => c.MatchLdcR4(0.75f),                   // 00B6 (ldc.r4 0.75f)
                c => c.MatchLdloca(out var _),              // 00BB (ldloca.s V_1)
                c => c.MatchInitobj("RoR2.ProcChainMask")   // 00BD (initobj RoR2.ProcChainMask)
            );

            // remove (ldc.r4 0.75f)
            c.Index += 1;
            c.Remove();

            // emit (ldc.r4 <float32>)
            float healStrength = HealStrengthPowerElixir.Value;
            c.Emit(OpCodes.Ldc_R4, healStrength);
        }
    }
}
