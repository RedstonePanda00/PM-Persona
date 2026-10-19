using System;
using System.Collections.Generic;
using System.Linq;
using ProtossMech;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona.Conclave
{
    public class CompProperties_Console : CompProperties
    {
        public CompProperties_Console() { compClass = typeof(CompConsole); }
    }
    public class CompConsole : ThingComp
    {
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action {
                defaultLabel = "PMP.Conclave.Open".Translate(), defaultDesc = "PMP.Conclave.OpenDesc".Translate(),
                icon = parent.def.uiIcon, action = () => Find.WindowStack.Add(new Dialog_Conclave(parent))
            };
        }
    }
    // Independent pages keep future council information out of the hero-status renderer.
    public abstract class ConclavePage
    {
        public abstract string Id { get; }
        public abstract string Label { get; }
        public virtual int Order => 0;
        public virtual bool Visible(Thing council) => true;
        public abstract void Draw(Rect rect, Thing council);
    }
    public static class ConclavePages
    {
        private static readonly Dictionary<string, Func<ConclavePage>> factories = new Dictionary<string, Func<ConclavePage>> {
            { "heroes", () => new HeroStatusPage() }
        };
        public static void Register(string id, Func<ConclavePage> factory)
        {
            if (string.IsNullOrWhiteSpace(id) || factory == null) throw new ArgumentException("A page ID and factory are required.");
            if (factories.ContainsKey(id)) throw new InvalidOperationException("Duplicate conclave page: " + id);
            factories.Add(id, factory);
        }
        internal static List<ConclavePage> Create() => factories.Values.Select(f => f()).OrderBy(p => p.Order).ThenBy(p => p.Id).ToList();
    }
    public class Dialog_Conclave : Window_ProtossAnimated
    {
        private readonly Thing council;
        private readonly List<ConclavePage> pages = ConclavePages.Create();
        private ConclavePage selected;
        public override Vector2 InitialSize => WarpConsoleUI.GetWindowSize();
        protected override float Margin => WarpConsoleUI.WindowMargin;
        public Dialog_Conclave(Thing council)
        {
            this.council = council; forcePause = false; absorbInputAroundWindow = false;
            closeOnClickedOutside = false; onlyOneOfTypeAllowed = true; layer = WindowLayer.GameUI;
        }
        protected override void DrawContents(Rect rect)
        {
            var color = GUI.color; var font = Text.Font; var anchor = Text.Anchor;
            try {
                GUI.color = ProtossUiTransition.ContentTint(Color.white); Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
                var shell = WarpUiLayout.DrawShell(WarpConsoleUI.GetDisplayContentRect(rect));
                var visible = pages.Where(p => p.Visible(council)).ToList();
                if (!visible.Contains(selected)) selected = visible.FirstOrDefault();
                float y = shell.leftInner.y;
                foreach (var page in visible) {
                    var row = new Rect(shell.leftInner.x, y, shell.leftInner.width, WarpUiLayout.LeftRowHeight);
                    WarpConsoleUI.DrawListItem(row, page == selected);
                    if (Widgets.ButtonInvisible(row)) selected = page;
                    Text.Anchor = TextAnchor.MiddleCenter; Widgets.Label(row, page.Label); Text.Anchor = TextAnchor.UpperLeft;
                    y += WarpUiLayout.LeftRowHeight + WarpUiLayout.LeftRowSpacing;
                }
                selected?.Draw(shell.contentInner, council);
            } finally { GUI.color = color; Text.Font = font; Text.Anchor = anchor; }
        }
    }
    public class HeroStatusPage : ConclavePage
    {
        private Vector2 scroll;
        private readonly Dictionary<PawnKindDef, Pawn> previews = new Dictionary<PawnKindDef, Pawn>();
        private List<PawnKindDef> heroes;
        public override string Id => "heroes";
        public override string Label => "PMP.Conclave.Heroes".Translate();

        private Pawn Preview(PawnKindDef kind)
        {
            if (previews.TryGetValue(kind, out var pawn)) return pawn;
            // Preview-only, never spawned or registered as an active persona. Preserve gameplay RNG.
            Rand.PushState(GenText.StableStringHash(kind.defName));
            try {
                pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, null,
                    PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true,
                    allowDead: false, allowDowned: false, canGeneratePawnRelations: false,
                    forceAddFreeWarmLayerIfNeeded: false, allowPregnant: false));
                if (GestaltZero.GestaltZeroUtility.IsZero(pawn))
                    GestaltZero.GestaltZeroUtility.ApplyCanonicalAppearance(pawn);
                previews.Add(kind, pawn);
                return pawn;
            } finally { Rand.PopState(); }
        }

        public override void Draw(Rect rect, Thing council)
        {
            if (heroes == null) {
                heroes = DefDatabase<ThingDef>.AllDefsListForReading
                    .Where(d => d.HasModExtension<PersonaHeroLifecycleExtension>())
                    .Select(d => DefDatabase<PawnKindDef>.AllDefsListForReading.FirstOrDefault(k => k.race == d))
                    .Where(k => k != null).ToList();
                heroes.AddRange(DefDatabase<PawnKindDef>.AllDefsListForReading
                    .Where(k => k.HasModExtension<GestaltZero.GestaltZeroLifecycleExtension>() && !heroes.Contains(k)));
            }
            var pawns = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction.ToList();
            var persistence = Current.Game?.GetComponent<GameComponent_PersonaHeroPersistence>();
            const float gap = 10f, height = 104f;
            float width = (rect.width - 20f - gap) / 2f;
            float contentHeight = ((heroes.Count + 1) / 2) * (height + gap);
            var tint = GUI.color;
            var anchor = Text.Anchor;
            Widgets.BeginScrollView(rect, ref scroll, new Rect(0, 0, rect.width - 20f, contentHeight));
            try {
                for (int i = 0; i < heroes.Count; i++) {
                    var kind = heroes[i];
                    bool zero = kind.HasModExtension<GestaltZero.GestaltZeroLifecycleExtension>();
                    string key = zero ? GestaltZero.GestaltZeroUtility.LifecycleKey : kind.race.defName;
                    var active = pawns.FirstOrDefault(p => !p.Dead && !p.Destroyed &&
                        (zero ? p.kindDef == kind : p.def == kind.race));
                    var portraitPawn = active ?? persistence?.PeekPooledPawn(key, true) ?? Preview(kind);
                    var cell = new Rect((i % 2) * (width + gap), (i / 2) * (height + gap), width, height);
                    GUI.color = tint;
                    WarpUiLayout.DrawPanel(cell);
                    var portrait = new Rect(cell.x + 6, cell.y + 6, 92, 92);
                    var drawSize = portraitPawn.RaceProps.Humanlike ? Vector2.one :
                        portraitPawn.ageTracker.CurKindLifeStage?.bodyGraphicData?.drawSize ?? Vector2.one;
                    float zoom = portraitPawn.RaceProps.Humanlike ? 1.15f : Mathf.Clamp(2.2f / Mathf.Max(drawSize.x, drawSize.y), .45f, 1f);
                    var rendered = PortraitsCache.Get(portraitPawn, portrait.size, Rot4.South,
                        cameraZoom: zoom, renderHeadgear: true, renderClothes: true);
                    Widgets.DrawTextureFitted(portrait, rendered, 1f);
                    Text.Anchor = TextAnchor.MiddleLeft;
                    var label = new Rect(cell.x + 104, cell.y + 18, width - 112, 32);
                    Widgets.Label(label, zero ? kind.LabelCap : kind.race.LabelCap);
                    GUI.color = tint * (active != null ? new Color(.2f, .85f, .3f) : new Color(.95f, .25f, .25f));
                    Widgets.Label(new Rect(label.x, cell.y + 52, label.width, 30), active != null ? "activate" : "offline");
                    GUI.color = tint;
                    if (active?.Spawned == true && Widgets.ButtonInvisible(portrait))
                        CameraJumper.TryJumpAndSelect(active);
                }
            } finally { Widgets.EndScrollView(); GUI.color = tint; Text.Anchor = anchor; }
        }
    }
}
