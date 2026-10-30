using System;
using System.Collections.Generic;
using RimCampaign.Runtime;
using RimCampaign.SDK;
using Verse;

namespace PMP.Campaign.BossPlugins
{
    [CampaignModule("persona.champion.retaliation","BossComponent","冠军：记录压缩前伤害，下次成功命中一次释放")]
    public sealed class ChampionRetaliation : BossComponentPlugin
    {
        public override void ObserveIncoming(BossComponentContext context,DamageInfo original)
        {
            if(!float.IsFinite(original.Amount)||original.Amount<=0)return;
            // Numerical overflow protection only; no gameplay charge cap.
            context.Write("charge",Math.Min(float.MaxValue,context.Read("charge")+original.Amount));
        }
        public override BossAttackReservation PrepareOutgoing(BossComponentContext context,Thing target,DamageInfo damage)
        {
            double reserved=context.Read("charge");
            if(reserved<=0||target==context.Pawn)return null;
            return new BossAttackReservation{Bonus=reserved,Complete=success=>{
                // Damage received during resolution belongs to the following hit.
                if(success)context.Write("charge",Math.Max(0,context.Read("charge")-reserved));
            }};
        }
        public override IEnumerable<Gizmo> Gizmos(BossComponentContext context)
        {
            var command=new Command_Action{defaultLabel="PMP.Champion.Charge".Translate(context.Read("charge").ToString("N0")),defaultDesc="PMP.Champion.ChargeDesc".Translate(),action=()=>{}};
            command.Disable("PMP.Champion.Passive".Translate());yield return command;
        }
    }
}
