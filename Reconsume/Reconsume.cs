using BepInEx;
using BepInEx.Configuration;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using RoR2;
using System;
using System.Collections.Generic;

namespace Reconsume
{

    [BepInDependency(R2API.R2API.PluginGUID)]
    [BepInPlugin("com.jiejasonliu.Reconsume", "Reconsume", "1.0.3")]
    public class Reconsume : BaseUnityPlugin
    {
        protected Dictionary<ItemDef, ItemDef> candidateItems;
        protected Dictionary<ItemDef, CommonConfigData> candidateItemsConfigData;

        // power elixir config
        protected static ConfigEntry<bool> RefillPowerElixir, ScrapConsumedPowerElixir;
        protected static ConfigEntry<float> HealStrengthPowerElixir;

        // delicate watch config
        protected static ConfigEntry<bool> RefillDelicateWatch, ScrapConsumedDelicateWatch;

        // dio's config
        protected static ConfigEntry<bool> RefillDiosBestFriend, ScrapConsumedDiosBestFriend;

        public void Awake()
        {
            Log.Init(Logger);

            SetupConfiguration();

            // hooks
            On.RoR2.Run.Start += Init_CandidateItems;
            On.RoR2.SceneDirector.PopulateScene += StageRestore_CandidateItems;
            IL.RoR2.ScrapperController.BeginScrapping_UniquePickup += IL_FixItemTier_CandidateItems;
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

            // dio's
            RefillDiosBestFriend = Config.Bind("DiosBestFriend", nameof(RefillDiosBestFriend), false, "Restore dio's best friend at the beginning of each stage");
            ScrapConsumedDiosBestFriend = Config.Bind("DiosBestFriend", nameof(ScrapConsumedDiosBestFriend), false, "Allow scrapping consumed dio's best friend");
        }

        /// <summary>
        /// Mappings of consumed to unconsumed items.
        /// </summary>
        /// <param name="orig"></param>
        /// <param name="self"></param>
        private void Init_CandidateItems(On.RoR2.Run.orig_Start orig, Run self)
        {
            orig(self);

            // consumed item -> unconsumed item
            candidateItems = new()
            {
                { DLC1Content.Items.HealingPotionConsumed, DLC1Content.Items.HealingPotion },
                { DLC1Content.Items.FragileDamageBonusConsumed, DLC1Content.Items.FragileDamageBonus },
                { RoR2Content.Items.ExtraLifeConsumed, RoR2Content.Items.ExtraLife },
            };

            // consumed item -> polymorphic common config data
            candidateItemsConfigData = new()
            {
                { DLC1Content.Items.HealingPotionConsumed, new CommonConfigData(RefillPowerElixir, ScrapConsumedPowerElixir) },
                { DLC1Content.Items.FragileDamageBonusConsumed, new CommonConfigData(RefillDelicateWatch, ScrapConsumedDelicateWatch) },
                { RoR2Content.Items.ExtraLifeConsumed, new CommonConfigData(RefillDiosBestFriend, ScrapConsumedDiosBestFriend) },

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

                        // DLC 3 (alloyed collective): ignore any temporary or disabled items.
                        var itemCount = playerInventory.GetItemCountPermanent(consumedItemDef);
                        playerInventory.RemoveItemPermanent(consumedItemDef, itemCount);
                        playerInventory.GiveItemPermanent(refilledItemDef, itemCount);
                    }
                }
            }
        }

        /// <summary>
        /// Since the item tier of consumed items are usually `NoTier`, we need to use the unconsumed item's tier in place of it
        /// Otherwise we end up finding no scrap index and hence no scrap drops for those items.
        /// </summary>
        private void IL_FixItemTier_CandidateItems(ILContext il)
        {
            ILCursor c = new(il);
            c.GotoNext(
                MoveType.Before,
                x => x.MatchCallvirt(typeof(RoR2.ItemDef), "get_tier"),                     // IL_0041: callvirt instance valuetype RoR2.ItemTier RoR2.ItemDef::get_tier()
                x => x.MatchCall(typeof(RoR2.PickupCatalog), "FindScrapIndexForItemTier")   // IL_0046: call valuetype RoR2.PickupIndex RoR2.PickupCatalog::FindScrapIndexForItemTier(valuetype RoR2.ItemTier)
            );

            // move after `get_tier()` and push `pickupToTake` onto stack
            c.Index += 1;
            c.Emit(OpCodes.Ldarg_1);

            // stack has the [RoR2.ItemTier (retval of get_tier), pickupToTake]
            // push overriden Ror2.ItemTier for FindScrapIndexForItemTier to consume
            c.EmitDelegate<Func<RoR2.ItemTier, RoR2.UniquePickup, RoR2.ItemTier>>((pickupToTakeItemTier, pickupToTake) =>
            {
                if (pickupToTakeItemTier == ItemTier.NoTier)
                {
                    PickupDef pickupDef = PickupCatalog.GetPickupDef(pickupToTake.pickupIndex);
                    ItemIndex pickupItemIndex = pickupDef?.itemIndex ?? ItemIndex.None;

                    foreach (var (consumedItemDef, unconsumedItemDef) in candidateItems)
                    {
                        // use the unconsumed item's tier instead
                        if (pickupItemIndex == consumedItemDef.itemIndex)
                            return unconsumedItemDef.tier;
                    }
                }
                return pickupToTakeItemTier;
            });
        }


        /// <summary>
        /// Whitelist the candidate items from interactables (i.e. scrappers)
        /// </summary>
        private void IL_ScrapperWhiteList_CandidateItems(ILContext il)
        {
            ILCursor c = new(il);
            c.GotoNext(
                MoveType.Before,
                x => x.MatchLdarg(0),    // IL_0008 (ldarg.0)
                x => x.MatchLdloc(0),    // IL_0009 (ldloc.0) 
                x => x.MatchCallvirt("System.Collections.Generic.List`1<RoR2.PickupPickerController/Option>", "ToArray") // IL_000A (callvirt instance !0[] class)
            );

            c.Emit(OpCodes.Ldarg_1);    // push Interactor; SetOptionsFromInteractor(Interactor activator)
            c.Emit(OpCodes.Ldloc_0);    // push List<PickupPickerController.Option>

            // perform scoped CIL virtcall and update options with whitelisted items
            // pops ldarg_1 (activator) and ldloc_0 (optionList) after delegate is invoked
            c.EmitDelegate<Action<Interactor, List<PickupPickerController.Option>>>((activator, optionList) =>
            {
                Inventory playerInventory = activator?.GetComponent<CharacterBody>()?.inventory;
                if (!playerInventory)
                    return;

                List<ItemIndex> itemIndexList = new(playerInventory.itemAcquisitionOrder);

                // find candidate items in player's inventory
                foreach (var itemIndex in itemIndexList)
                {
                    ItemDef consumedItemDef = ItemCatalog.GetItemDef(itemIndex);
                    if (candidateItems.TryGetValue(consumedItemDef, out ItemDef refilledItemDef))
                    {
                        // config guard check
                        if (!candidateItemsConfigData[consumedItemDef].CanScrap)
                            continue;

                        PickupIndex pickupIndex = PickupCatalog.FindPickupIndex(itemIndex);
                        optionList.Add(new PickupPickerController.Option
                        {
                            available = true,
                            pickup = new UniquePickup(pickupIndex)
                            {
                                decayValue = 0f,
                            }
                        });
                    }
                }
            });
        }

        /// <summary>
        /// Alter the heal strength of the power elixir to balance it being refillable.
        /// </summary>
        private void IL_AlterHealStrength_PowerElixir(ILContext il)
        {
            ILCursor c = new(il);
            c.GotoNext(
                MoveType.Before,
                x => x.MatchLdarg(0),                       // IL_00c0 (ldarg.0)
                x => x.MatchLdcR4(0.75f),                   // IL_00c1 (ldc.r4 0.75f)
                x => x.MatchLdloca(out var _),              // IL_00c6 (ldloca.s 4)
                x => x.MatchInitobj("RoR2.ProcChainMask")   // IL_00c8 (initobj RoR2.ProcChainMask)
            );

            // remove (ldc.r4 0.75f) which is 00c0 + 1
            c.Index += 1;
            c.Remove();

            // insert (ldc.r4 <float32>)
            float healStrength = HealStrengthPowerElixir.Value;
            c.Emit(OpCodes.Ldc_R4, healStrength);
        }
    }
}
