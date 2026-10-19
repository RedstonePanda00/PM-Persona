using Verse;

namespace PMP_Persona.RecomposerSupport
{
    public static class RecomposerTargeting
    {
        public static bool Active { get; private set; }
        public static Map Map { get; private set; }

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
