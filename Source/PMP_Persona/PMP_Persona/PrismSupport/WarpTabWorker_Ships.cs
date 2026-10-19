using System.Collections.Generic;
using PMP_Persona.ShipSupport;
using ProtossMech;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona.PrismSupport
{
    public class WarpTabWorker_Ships : WarpTabWorker
    {
        private const float HeaderHeight = 68f;
        private const float DetailHeight = 224f;
        private const float Margin = 10f;

        private readonly List<ShipSupportAction> secondaryActions = new List<ShipSupportAction>(2);
        private Vector2 scrollPosition;
        private string expandedDefName;
        private static List<ShipSupportDef> orderedDefs;

        public override bool Visible()
        {
            return base.Visible() && GetOrderedDefs().Count > 0;
        }

        public override void DoContents(Dialog_Warp dialog, Rect inRect)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = Color.white;

            List<ShipSupportDef> supports = GetOrderedDefs();
            if (supports.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(inRect, "PMP.NoShipsAvailable".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            float contentHeight = Margin;
            for (int i = 0; i < supports.Count; i++)
            {
                contentHeight += HeaderHeight;
                if (supports[i].defName == expandedDefName)
                    contentHeight += DetailHeight;
            }

            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, contentHeight + Margin);
            Widgets.BeginScrollView(inRect, ref scrollPosition, viewRect);

            float y = Margin;
            for (int i = 0; i < supports.Count; i++)
            {
                ShipSupportDef support = supports[i];
                ShipSupportWorker worker = support.Worker;
                if (worker == null)
                    continue;

                bool expanded = support.defName == expandedDefName;
                ShipSupportContext context = new ShipSupportContext(support, Find.CurrentMap, dialog);
                DrawHeader(support, worker, context, viewRect.width, y, expanded);
                y += HeaderHeight;

                if (expanded)
                {
                    DrawDetails(support, worker, context, viewRect.width, y);
                    y += DetailHeight;
                }
            }

            Widgets.EndScrollView();
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = Color.white;
        }

        private void DrawHeader(
            ShipSupportDef support,
            ShipSupportWorker worker,
            ShipSupportContext context,
            float viewWidth,
            float y,
            bool expanded)
        {
            Rect rowRect = new Rect(Margin, y, viewWidth - Margin * 2f, HeaderHeight - 6f);
            WarpConsoleUI.DrawListItem(rowRect, selected: expanded);

            Rect iconRect = new Rect(rowRect.x + 10f, rowRect.y + 9f, 44f, 44f);
            GUI.DrawTexture(iconRect, support.Icon, ScaleMode.ScaleToFit, true);

            float rightEdge = rowRect.xMax - 10f;
            Rect foldRect = new Rect(rightEdge - 30f, rowRect.y + 16f, 30f, 30f);
            rightEdge = foldRect.x - 8f;

            secondaryActions.Clear();
            worker.GetSecondaryActions(context, secondaryActions);
            bool mouseOverAction = false;
            for (int i = secondaryActions.Count - 1; i >= 0; i--)
            {
                ShipSupportAction action = secondaryActions[i];
                Rect actionRect = new Rect(rightEdge - 96f, rowRect.y + 13f, 96f, 36f);
                rightEdge = actionRect.x - 8f;
                mouseOverAction |= Mouse.IsOver(actionRect);
                if (WarpConsoleUI.ButtonText(actionRect, action.label, action.Enabled) && action.Enabled)
                    action.action();
                if (!action.disabledReason.NullOrEmpty())
                    TooltipHandler.TipRegion(actionRect, action.disabledReason);
            }

            Rect textRect = new Rect(iconRect.xMax + 12f, rowRect.y + 7f,
                Mathf.Max(40f, rightEdge - iconRect.xMax - 18f), 24f);
            Text.Font = GameFont.Small;
            Widgets.Label(textRect, support.LabelCap);

            string status = worker.GetStatusText(context);
            string cost = ShipSupportPaymentService.GetCostString(support);
            GUI.color = Color.gray;
            Widgets.Label(new Rect(textRect.x, textRect.yMax + 2f, textRect.width, 22f),
                status + "  ·  " + cost);
            GUI.color = Color.white;

            if (WarpConsoleUI.IconButton(
                    foldRect,
                    expanded ? TexButton.Collapse : TexButton.Reveal,
                    selected: expanded))
            {
                ToggleExpanded(support);
            }

            if (!mouseOverAction && !Mouse.IsOver(foldRect) && Widgets.ButtonInvisible(rowRect))
                ToggleExpanded(support);
        }

        private static void DrawDetails(
            ShipSupportDef support,
            ShipSupportWorker worker,
            ShipSupportContext context,
            float viewWidth,
            float y)
        {
            Rect panelRect = new Rect(Margin + 18f, y, viewWidth - Margin * 2f - 18f, DetailHeight - 8f);
            WarpConsoleUI.DrawListItem(panelRect, selected: true);

            if (support.Background != null)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.16f);
                GUI.DrawTexture(panelRect, support.Background, ScaleMode.ScaleAndCrop, true);
                GUI.color = Color.white;
                Widgets.DrawBoxSolid(panelRect, new Color(0.03f, 0.05f, 0.07f, 0.72f));
            }

            Rect largeIcon = new Rect(panelRect.x + 16f, panelRect.y + 16f, 88f, 88f);
            GUI.DrawTexture(largeIcon, support.Icon, ScaleMode.ScaleToFit, true);

            float infoX = largeIcon.xMax + 18f;
            float infoWidth = panelRect.xMax - infoX - 16f;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(infoX, panelRect.y + 12f, infoWidth, 34f), support.LabelCap);

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.85f, 0.88f, 0.92f);
            Widgets.Label(new Rect(infoX, panelRect.y + 49f, infoWidth, 51f), support.description ?? string.Empty);
            GUI.color = Color.white;

            Text.Font = GameFont.Small;
            string effectLabel = "PMP.Support.Effect".Translate();
            Widgets.Label(new Rect(panelRect.x + 16f, largeIcon.yMax + 10f, 72f, 24f), effectLabel);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(panelRect.x + 88f, largeIcon.yMax + 10f,
                    panelRect.width - 104f, 45f),
                support.effectDescription ?? string.Empty);

            Text.Font = GameFont.Small;
            string costText = "PMP.Support.Cost".Translate(ShipSupportPaymentService.GetCostString(support));
            Widgets.Label(new Rect(panelRect.x + 16f, panelRect.yMax - 48f,
                panelRect.width - 180f, 32f), costText);

            ShipSupportAction primary = worker.GetPrimaryAction(context);
            Rect primaryRect = new Rect(panelRect.xMax - 154f, panelRect.yMax - 52f, 138f, 38f);
            if (WarpConsoleUI.ButtonText(primaryRect, primary.label, primary.Enabled) && primary.Enabled)
                primary.action();
            if (!primary.disabledReason.NullOrEmpty())
                TooltipHandler.TipRegion(primaryRect, primary.disabledReason);
        }

        private void ToggleExpanded(ShipSupportDef support)
        {
            expandedDefName = expandedDefName == support.defName ? null : support.defName;
        }

        private static List<ShipSupportDef> GetOrderedDefs()
        {
            if (orderedDefs != null)
                return orderedDefs;
            orderedDefs = new List<ShipSupportDef>(DefDatabase<ShipSupportDef>.AllDefsListForReading);
            orderedDefs.Sort(delegate(ShipSupportDef a, ShipSupportDef b)
            {
                int orderCompare = a.order.CompareTo(b.order);
                return orderCompare != 0
                    ? orderCompare
                    : string.CompareOrdinal(a.defName, b.defName);
            });
            return orderedDefs;
        }
    }
}
