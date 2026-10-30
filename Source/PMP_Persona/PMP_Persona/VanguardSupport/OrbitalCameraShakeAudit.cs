using System;
using System.IO;
using RimWorld;
using Verse;

namespace PMP_Persona.VanguardSupport
{
    // Called only by the opt-in Alpha/Zeta native audits. Exercise the patched camera
    // entry point, not merely the custom impact-shake property.
    internal static class OrbitalCameraShakeAudit
    {
        internal static void Run(string folder)
        {
            if (!VanguardFireControl.OwnsView || !VanguardFireControl.CurrentController.Ship.Ready)
                throw new InvalidOperationException("Shake audit requires active orbital fire control.");
            var camera = Find.CameraDriver;
            var shaker = camera.shaker;
            float delta = RealTime.realDeltaTime;
            int tick = Find.TickManager.TicksGame;
            var speed = Find.TickManager.CurTimeSpeed;
            try
            {
                RealTime.realDeltaTime = .01f;
                shaker.DoShake(shaker.GetMaxShakeMag());
                float initial = shaker.CurShakeMag;
                if (initial <= 0) throw new Exception("Shake disabled in native audit preferences.");
                camera.Update();
                if (shaker.CurShakeMag >= initial || shaker.CurShakeMag <= 0)
                    throw new Exception("Orbital camera bypasses native shake decay.");
                RealTime.realDeltaTime = .1f;
                for (int i = 0; i < 10; i++) camera.Update();
                if (shaker.CurShakeMag != 0) throw new Exception("Impulse shake never ended.");

                shaker.DoShake(shaker.GetMaxShakeMag(), 12);
                RealTime.realDeltaTime = .01f;
                camera.Update();
                if (shaker.CurShakeMag <= 0) throw new Exception("Timed shake did not begin.");
                Find.TickManager.DebugSetTicksGame(tick + 13);
                RealTime.realDeltaTime = .1f;
                for (int i = 0; i < 10; i++) camera.Update();
                if (shaker.CurShakeMag != 0) throw new Exception("Timed shake request never expired.");

                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                shaker.DoShake(shaker.GetMaxShakeMag());
                for (int i = 0; i < 10; i++) camera.Update();
                if (shaker.CurShakeMag != 0) throw new Exception("Paused fire control retained native shake.");
                File.AppendAllText(Path.Combine(folder, "camera-shake.txt"),
                    "PASS patched CameraDriver.Update: impulse decays; timed request expires; pause clears residual impulse.\n");
            }
            finally
            {
                RealTime.realDeltaTime = delta;
                Find.TickManager.DebugSetTicksGame(tick);
                Find.TickManager.CurTimeSpeed = speed;
            }
        }
    }
}
