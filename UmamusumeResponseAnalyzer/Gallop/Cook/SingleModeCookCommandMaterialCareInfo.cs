using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    public class SingleModeCookCommandMaterialCareInfo
    {
        public int command_type; 
        public int command_id; 
        public int material_id; 
        public int boost_type; 
        public SingleModeCookMaterialHarvestInfo[] material_harvest_info_array; 
        public int care_point; 
        public SingleModeCookMaterialHarvestInfo[] failure_material_harvest_info_array; 
        public int failure_care_point; 
    }
}
