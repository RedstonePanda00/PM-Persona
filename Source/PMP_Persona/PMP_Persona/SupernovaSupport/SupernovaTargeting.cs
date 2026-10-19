using Verse;

namespace PMP_Persona.SupernovaSupport
{
    public static class SupernovaTargeting
    {
        public static bool Active { get; private set; }
        public static Map Map { get; private set; }

        public static float RadiusCells => SupernovaConstants.FieldRadiusCells;

        public static void Begin(Map map)
        {
            Map = map;
            Active = map != null;
        }

        public static void Clear()
        {
            Active = false;
            Map = null;
        }
    }
}
