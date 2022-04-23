using BepInEx.Configuration;

namespace Reconsume
{
    /// <summary>
    /// Each config data represents common binded configuration of a reconsumable item.
    /// A reconsumable item should have some relation to a CommonConfigData indirectly.
    /// </summary>
    public sealed class CommonConfigData
    {
        public bool RefillOnStage;  // refill at the beginning of the stage?
        public bool CanScrap;       // should be able to be scrapped?

        public CommonConfigData(bool RefillOnStage, bool CanScrap)
        {
            this.RefillOnStage = RefillOnStage;
            this.CanScrap = CanScrap;
        }

        public CommonConfigData(ConfigEntry<bool> RefillOnStageEntry, ConfigEntry<bool> CanScrapEntry)
        {
            this.RefillOnStage = RefillOnStageEntry.Value;
            this.CanScrap = CanScrapEntry.Value;
        }
    }
}
