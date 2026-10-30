using RimCampaign.Runtime;
using Verse;

namespace PMP.Campaign {
    // Persona chapter scripts require project, campaign, story node and owning map.
    internal static class MissionScope {
        internal static bool Matches(CampaignController c,string node=null)=>
            c?.StoryActive==true&&c.Project.Id=="pmp-mission-one"&&c.CampaignId=="pmp_archive"&&
            (node==null?(c.NodeId=="mission-one"||c.NodeId=="mission-two"||c.NodeId=="mission-three"||c.NodeId=="mission-four"):c.NodeId==node);
        internal static bool Owns(CampaignController c,Map map,string node=null)=>
            map!=null&&Matches(c,node)&&c.Map==map;
    }
}
