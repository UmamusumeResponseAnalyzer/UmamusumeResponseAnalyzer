using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    public class SingleModeCookDataSet
    {
        public SingleModeCookInfo cook_info;
        public SingleModeCookCommandInfo[] command_info_array;
        public SingleModeCookEvaluationInfo[] evaluation_info_array;
        public SingleModeCookMaterialInfo[] material_info_array;
        public SingleModeCookMaterialHarvestInfo[] material_harvest_info_array;
        public SingleModeCookCareHistoryInfo[] care_history_info_array;
        public SingleModeCookDishInfo dish_info;
        public SingleModeCookFacilityInfo[] facility_info_array;
        public int cooking_success_rate;
        public SingleModeCookGainMaterialInfo gain_material_info;
        public SingleModeCookResultInfo[] cook_result_info_array;
        public SingleModeCookPowerEffectInfo[] cook_power_effect_info_array;
        public SingleModeCookDishSkillInfo dish_skill_info;
        public int[] success_effect_id_array;
        public SingleModeCookSubCommandCharaInfo[] sub_command_chara_info_array;
        public int care_point_gain_num;
        public int? care_special_home_id;
        public SingleModeCookAvailableDishInfo[] available_dish_info_array;
        public SingleModeCookCommandMaterialCareInfo[] command_material_care_info_array;
        public int tasting_result_state;
        public SingleModeCookLastCommandInfo last_command_info;
        public SingleModeCookMaterialInfo[] event_gain_material_info_array;
        public int unlock_degree_type;
        public int[] unlock_flavor_text_id_array;
        public int[] unread_flavor_text_id_array;
        public int display_flavor_text_id;
    }
}
