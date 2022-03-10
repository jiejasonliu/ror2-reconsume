using BepInEx.Configuration;

namespace Reconsume
{
    /// <summary>
    /// Each config data represents the binded configuration of the candidate item.
    /// A candidate item should have some relation to a CandidateItemConfigData indirectly.
    /// </summary>
    public sealed class CandidateItemConfigData
    {
        public bool RefillOnStage;  // refill at the beginning of the stage?
        public bool CanScrap;       // should be able to be scrapped?

        public CandidateItemConfigData(bool RefillOnStage, bool CanScrap)
        {
            this.RefillOnStage = RefillOnStage;
            this.CanScrap = CanScrap;
        }

        public CandidateItemConfigData(ConfigEntry<bool> RefillOnStageEntry, ConfigEntry<bool> CanScrapEntry)
        {
            this.RefillOnStage = RefillOnStageEntry.Value;
            this.CanScrap = CanScrapEntry.Value;
        }
    }
}
