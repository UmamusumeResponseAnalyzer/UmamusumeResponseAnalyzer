namespace Gallop
{
    public class SingleModeBreedersDataSetLoad
    {
        public int last_select_bc_group_id;
        public int deck_id;
        public SingleModeBreederTeamReviewResult[] team_review_result_array;
        public SingleModeBreederEnhanceGroup[] enhance_group_array;
    }

    public class SingleModeBreederTeamReviewResult
    {
        public int schedule_id;
        public int result_type;
    }

    public class SingleModeBreederEnhanceGroup
    {
        public int group_type;
        public int level;
    }
}
